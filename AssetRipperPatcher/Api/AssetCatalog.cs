using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.GUI.Web;
using AssetRipper.GUI.Web.Paths;
using AssetRipper.Import.Logging;
using AssetRipper.Processing.Textures;
using AssetRipper.SourceGenerated.Extensions;

namespace AssetRipperPatcher.Api;

/// <summary>
/// 资源查询与分页。列表可能上万条，所以索引只建一次并缓存；
/// 导入（<see cref="GameFileLoader.LoadAndProcess"/>）或 Reset 后由 <see cref="Invalidate"/> 丢弃。
/// </summary>
internal static class AssetCatalog
{
	private static readonly object _lock = new();
	private static List<AssetEntry>? _entries;
	private static int _version;

	/// <summary>缓存版本号，供前端判断是否需要重取。</summary>
	public static int Version => _version;

	private sealed record AssetEntry(IUnityObjectBase Asset, string SearchText);

	/// <summary>导入/重置后调用，让下一次查询重建索引。</summary>
	public static void Invalidate()
	{
		lock (_lock)
		{
			_entries = null;
			_version++;
		}
	}

	private static List<AssetEntry> Entries
	{
		get
		{
			lock (_lock)
			{
				if (_entries is not null)
				{
					return _entries;
				}

				List<AssetEntry> entries = new();
				foreach (IUnityObjectBase asset in AssetProbe.EnumerateAssets())
				{
					// 预先拼好小写搜索串，避免每次查询都做字符串拼接。
					string searchText = string.Concat(
						asset.GetBestName(),
						'\u001f',
						asset.ClassName,
						'\u001f',
						asset.Collection.Name,
						'\u001f',
						asset.OriginalPath ?? string.Empty).ToLowerInvariant();
					entries.Add(new AssetEntry(asset, searchText));
				}
				entries.Sort(static (a, b) => string.CompareOrdinal(a.Asset.ClassName, b.Asset.ClassName));
				_entries = entries;
				Logger.Info(LogCategory.General, $"UI asset index built: {entries.Count} assets");
				return entries;
			}
		}
	}

	public static IReadOnlyList<IUnityObjectBase> Query(
		string? search,
		string? className,
		string? collectionPath,
		string? sort,
		bool descending,
		int offset,
		int limit,
		out int total)
	{
		IEnumerable<AssetEntry> query = Entries;

		if (!string.IsNullOrWhiteSpace(className))
		{
			string[] classes = className.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			query = query.Where(e => classes.Any(c => MatchesClassName(e.Asset.ClassName, c)));
		}

		if (!string.IsNullOrWhiteSpace(collectionPath))
		{
			query = query.Where(e => string.Equals(e.Asset.Collection.GetPath().ToJson(), collectionPath, StringComparison.Ordinal));
		}

		if (!string.IsNullOrWhiteSpace(search))
		{
			string[] terms = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			query = query.Where(e => terms.All(t => e.SearchText.Contains(t.ToLowerInvariant(), StringComparison.Ordinal)));
		}

		List<AssetEntry> list = query.ToList();
		total = list.Count;

		list.Sort(GetComparer(sort));
		if (descending)
		{
			list.Reverse();
		}

		IEnumerable<AssetEntry> page = list;
		if (offset > 0)
		{
			page = page.Skip(offset);
		}
		if (limit > 0)
		{
			page = page.Take(limit);
		}

		return page.Select(e => e.Asset).ToList();
	}

	private static bool MatchesClassName(string name, string pattern)
	{
		// 与命令行 --only 一样支持 * 通配，方便按类型过滤。
		if (pattern.IndexOf('*') < 0)
		{
			return string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
		}
		string[] parts = pattern.Split('*');
		int index = 0;
		foreach (string part in parts)
		{
			if (part.Length == 0)
			{
				continue;
			}
			int found = name.IndexOf(part, index, StringComparison.OrdinalIgnoreCase);
			if (found < 0)
			{
				return false;
			}
			index = found + part.Length;
		}
		return true;
	}

	private static Comparison<AssetEntry> GetComparer(string? sort) => sort switch
	{
		"name" => static (a, b) => string.Compare(a.Asset.GetBestName(), b.Asset.GetBestName(), StringComparison.OrdinalIgnoreCase),
		"pathId" => static (a, b) => a.Asset.PathID.CompareTo(b.Asset.PathID),
		"collection" => static (a, b) => string.Compare(a.Asset.Collection.Name, b.Asset.Collection.Name, StringComparison.OrdinalIgnoreCase),
		"class" => static (a, b) => string.CompareOrdinal(a.Asset.ClassName, b.Asset.ClassName),
		_ => static (a, b) => string.CompareOrdinal(a.Asset.ClassName, b.Asset.ClassName),
	};

	/// <summary>统计数据：用于状态栏和首页卡片。</summary>
	public static StatsDto GetStats()
	{
		GameBundle? bundle = AssetProbe.Bundle;
		if (bundle is null)
		{
			return new StatsDto(0, 0, 0, 0, 0, 0);
		}

		int bundles = 0;
		int resources = 0;
		int failed = 0;
		int scenes = 0;
		foreach (Bundle b in EnumerateBundles(bundle))
		{
			bundles++;
			resources += b.Resources.Count;
			failed += b.FailedFiles.Count;
			scenes += b.Collections.Count(c => c.IsScene);
		}

		return new StatsDto(
			Collections: bundle.FetchAssetCollections().Count(),
			Assets: Entries.Count,
			Bundles: bundles,
			Resources: resources,
			FailedFiles: failed,
			Scenes: scenes);
	}

	private static IEnumerable<Bundle> EnumerateBundles(Bundle root)
	{
		Stack<Bundle> stack = new();
		stack.Push(root);
		while (stack.Count > 0)
		{
			Bundle current = stack.Pop();
			yield return current;
			foreach (Bundle child in current.Bundles)
			{
				stack.Push(child);
			}
		}
	}

	/// <summary>资源树：根 → bundle → collection，最多三层，足够 UI 用。</summary>
	public static IReadOnlyList<TreeNodeDto> GetTree()
	{
		GameBundle? root = AssetProbe.Bundle;
		if (root is null)
		{
			return [];
		}

		List<TreeNodeDto> nodes = new();
		AddBundle(root, "root", nodes, depth: 0);
		return nodes;

		static void AddBundle(Bundle bundle, string id, List<TreeNodeDto> siblings, int depth)
		{
			List<TreeNodeDto> children = new();

			foreach ((Bundle child, int index) in bundle.Bundles.Select((b, i) => (b, i)))
			{
				List<TreeNodeDto> bucket = new();
				AddBundle(child, $"{id}.{index}", bucket, depth + 1);
				if (bucket.Count > 0)
				{
					// 单子节点直接提升，避免出现只有一层包装的空节点。
					children.Add(bucket.Count == 1 ? bucket[0] : new TreeNodeDto($"{id}.{index}", "bundle", child.Name, null, bucket));
				}
			}

			foreach ((AssetCollection collection, int index) in bundle.Collections.Select((c, i) => (c, i)))
			{
				string kind = collection.IsScene ? "scene" : "collection";
				children.Add(new TreeNodeDto(
					$"{id}.c{index}",
					kind,
					collection.Name,
					collection.Count,
					null));
			}

			if (depth == 0 || children.Count > 0)
			{
				siblings.Add(new TreeNodeDto(id, "bundle", bundle.Name, null, children.Count > 0 ? children : null));
			}
		}
	}

	/// <summary>按类名聚合的直方图，用于左侧「资源类型」面板。</summary>
	public static IReadOnlyList<(string ClassName, int Count)> GetClassHistogram()
	{
		return Entries
			.GroupBy(e => e.Asset.ClassName)
			.Select(g => (ClassName: g.Key, Count: g.Count()))
			.OrderByDescending(x => x.Count)
			.ThenBy(x => x.ClassName, StringComparer.Ordinal)
			.ToList();
	}
}