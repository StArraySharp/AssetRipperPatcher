using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Export.UnityProjects;
using AssetRipperPatcher.Patching;
using HarmonyLib;

namespace AssetRipperPatcher.Patches;

/// <summary>
/// <c>--only &lt;类型[,类型...]&gt;</c>：只导出指定类型的资源。
///
/// AssetRipper 没有原生的资源类型过滤（它总是遍历所有 collection），
/// 而全量导出一次动辄几分钟、几 GB，调 bug 时很难受。
/// 这里在 <see cref="ProjectExporter.CreateCollections(GameBundle)"/> 上挂钩：
/// 把不含目标类型的 collection 从导出列表里剔掉。
///
/// 匹配依据是 <see cref="IUnityObjectBase.ClassName"/>（不区分大小写，支持 <c>*</c> 通配），
/// 例如 <c>--only Sprite</c>、<c>--only Sprite,Texture2D</c>、<c>--only Sprite*</c>。
///
/// <para><b>依赖闭包。</b>
/// 默认会把匹配资源的**可达依赖**一并保留（沿 PPtr 做传递闭包），
/// 否则 <c>--only Sprite</c> 导出的 <c>.meta</c> 里全是悬空 guid。
/// 加 <c>--only-strict</c> 可关掉这一步，得到「只有目标类型」的最小集合——
/// 调试单个类型的导出布局时更快，但工程必然带悬空引用。</para>
/// </summary>
internal static class AssetTypeFilterPatch
{
    /// <summary>
    /// 过滤 <see cref="ProjectExporter.CreateCollections(GameBundle)"/> 的返回值。
    /// 补丁挂在 private 方法上，用 <c>__result</c> 改写结果集。
    /// </summary>
    [PatchMethod(typeof(ProjectExporter), "CreateCollections", PatchType = PatchType.Postfix)]
    public static void FilterCollections(GameBundle fileCollection, ref List<IExportCollection> __result)
    {
        AppState.AssetFilter? filter = AppState.CommandLineFilter;
        if (filter is null)
        {
            return;
        }

        int before = __result.Count;

        // 先按类名挑出「种子」collection。
        List<IExportCollection> kept = __result
            .Where(static collection => collection.Assets.Count() > 0)
            .Where(collection => HasMatchingAsset(collection, filter))
            .ToList();

        int matched = kept.Count;
        if (!AppState.StrictOnly && kept.Count > 0)
        {
            kept = ExpandWithDependencies(kept, __result);
        }

        __result = kept;
        int removed = before - __result.Count;
        string mode = AppState.StrictOnly ? " (strict: no dependencies)" : "";
        Console.WriteLine(
            $"[AssetFilter] --only '{filter.RawText}'{mode}: matched {matched} collection(s), " +
            $"kept {__result.Count}/{before}, removed {removed}");
    }

    /// <summary>
    /// 把 <paramref name="seeds"/> 沿 PPtr 引用做传递闭包，返回包含依赖的 collection 列表。
    ///
    /// <para>只保留**原本就在 <paramref name="all"/> 里**的 collection——被别的处理器剔除的
    /// collection 不该因为被引用而复活。</para>
    /// </summary>
    private static List<IExportCollection> ExpandWithDependencies(
        List<IExportCollection> seeds,
        List<IExportCollection> all)
    {
        // asset → 拥有它的 collection，避免每个依赖都线性扫一遍。
        Dictionary<IUnityObjectBase, IExportCollection> owner = new(ReferenceEqualityComparer.Instance);
        foreach (IExportCollection collection in all)
        {
            foreach (IUnityObjectBase asset in collection.Assets)
            {
                owner.TryAdd(asset, collection);
            }
        }

        HashSet<IExportCollection> kept = new(ReferenceEqualityComparer.Instance);
        Queue<IUnityObjectBase> pending = new();
        foreach (IExportCollection seed in seeds)
        {
            if (kept.Add(seed))
            {
                foreach (IUnityObjectBase asset in seed.Assets)
                {
                    pending.Enqueue(asset);
                }
            }
        }

        HashSet<IUnityObjectBase> visited = new(ReferenceEqualityComparer.Instance);
        while (pending.Count > 0)
        {
            IUnityObjectBase asset = pending.Dequeue();
            if (!visited.Add(asset))
            {
                continue;
            }
            foreach (IUnityObjectBase reference in PPtrCollector.DirectReferences(asset))
            {
                if (visited.Contains(reference))
                {
                    continue;
                }
                if (owner.TryGetValue(reference, out IExportCollection? referencedBy))
                {
                    // 只有当依赖所在的 collection 本来就在导出列表里才加回来。
                    if (kept.Add(referencedBy))
                    {
                        pending.Enqueue(reference);
                    }
                    else
                    {
                        pending.Enqueue(reference);
                    }
                }
            }
        }

        // 恢复原始顺序：导出顺序影响引用 id 的分配，重排会让每次导出产生不同结果。
        return all.Where(kept.Contains).ToList();
    }

    /// <summary>
    /// 集合里是否有匹配的资源。一个 collection 可能含多个 asset（例如纹理 + 它上面的 Sprite），
    /// 只要有一个匹配就整体保留，避免把 Sprite 的依赖纹理一起砍掉。
    /// </summary>
    private static bool HasMatchingAsset(IExportCollection collection, AppState.AssetFilter filter)
    {
        foreach (IUnityObjectBase asset in collection.Assets)
        {
            if (filter.IsMatch(asset.ClassName))
            {
                return true;
            }
        }
        return false;
    }
}
