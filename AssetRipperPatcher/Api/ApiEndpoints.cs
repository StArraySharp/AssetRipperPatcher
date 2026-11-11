using AssetRipper.Assets;
using AssetRipper.Export.Configuration;
using AssetRipper.GUI.Web;
using AssetRipper.GUI.Web.Paths;
using AssetRipper.IO.Files;
using AssetRipper.SourceGenerated.Classes.ClassID_189;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipperPatcher.WebUi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AssetRipperPatcher.Api;

/// <summary>
/// 新 Web UI 的 JSON 端点。全部挂在 <c>/api</c> 下，与 AssetRipper 原有的 HTML 页面完全隔离，
/// 所以两边可以同时存在：老 UI 出问题时只需把 Photino 外壳摘掉，Kestrel 还在。
/// </summary>
internal static class ApiEndpoints
{
	private const int MaxLimit = 2000;

	public static void Map(WebApplication app)
	{
		RouteGroupBuilder api = app.MapGroup("/api");

		// ---- 状态 ----
		api.MapGet("/status", () => Results.Json(GetStatus()));
		api.MapGet("/stats", () => Results.Json(AssetCatalog.GetStats()));

		// ---- 资源 ----
		api.MapGet("/assets", GetAssets);
		api.MapGet("/assets/detail", GetAssetDetail);
		api.MapGet("/classes", () => Results.Json(
					AssetCatalog.GetClassHistogram().Select(x => new ClassCountDto(x.ClassName, x.Count)).ToArray()));
		api.MapGet("/tree", () => Results.Json(AssetCatalog.GetTree().ToArray()));

		// ---- 预览 / 文本 ----
		api.MapGet("/assets/image", (HttpRequest request) => ServeAssetBytes(request, AssetKind.Image));
		api.MapGet("/assets/audio", (HttpRequest request) => ServeAssetBytes(request, AssetKind.Audio));
		api.MapGet("/assets/model", (HttpRequest request) => ServeAssetBytes(request, AssetKind.Model));
		api.MapGet("/assets/font", (HttpRequest request) => ServeAssetBytes(request, AssetKind.Font));
		api.MapGet("/assets/video", (HttpRequest request) => ServeAssetBytes(request, AssetKind.Video));
		api.MapGet("/assets/binary", (HttpRequest request) => ServeAssetBytes(request, AssetKind.Binary));
		api.MapGet("/assets/text", GetAssetText);
		api.MapGet("/assets/json", GetAssetJson);
		api.MapGet("/assets/yaml", GetAssetYaml);

		// ---- 语言 ----
		api.MapGet("/text", GetUiText);
		api.MapPost("/language", SetLanguage);

		// ---- 原生对话框 ----
		api.MapGet("/dialog/folder", (HttpRequest request) =>
			Results.Json(DialogService.OpenFolder(
				request.Query["title"].ToString() is { Length: > 0 } title ? title : LocalizedText.Get("dialog.pickGameFolder"),
				request.Query["basePath"])));
		api.MapGet("/dialog/file", (HttpRequest request) =>
			Results.Json(DialogService.OpenFile(
				request.Query["title"].ToString() is { Length: > 0 } title ? title : LocalizedText.Get("dialog.pickGameFile"),
				request.Query["basePath"],
				request.Query["multi"] == "true")));

		// ---- 设置 ----
		api.MapGet("/settings", () => Results.Json(SettingsService.Get()));
		api.MapPost("/settings", UpdateSettings);

		// ---- 导入 / 导出 ----
		api.MapPost("/load", StartLoad);
		api.MapPost("/export", StartExport);
		api.MapPost("/reset", () =>
		{
			GameFileLoader.Reset();
			AssetCatalog.Invalidate();
			return Results.NoContent();
		});
		api.MapGet("/operation", () => Results.Json(OperationService.Instance.Current));

		// ---- 日志 ----
		api.MapGet("/log", GetLog);
	}

	#region 状态

	private static StatusDto GetStatus()
	{
		string? unityVersion = null;
		if (GameFileLoader.IsLoaded)
		{
			try
			{
				unityVersion = GameFileLoader.GameBundle.GetMaxUnityVersion().ToString();
			}
			catch (Exception)
			{
				unityVersion = null;
			}
		}

		return new StatusDto(
			App: "AssetRipperPatcher",
			Version: typeof(StatusDto).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
			UnityVersion: unityVersion,
			Loaded: GameFileLoader.IsLoaded,
			Exporting: OperationService.Instance.IsBusy,
			WebView2: HostEnvironment.WebView2Version,
			Shell: HostEnvironment.ShellName,
			SettingsPath: AppSettings.SettingsFilePath);
	}

	#endregion

	#region 资源

	private static IResult GetAssets(HttpRequest request)
	{
		if (!GameFileLoader.IsLoaded)
		{
			return Results.Json(new PageDto<AssetRowDto>([], 0, 0, 0));
		}

		string? search = request.Query["q"];
		string? className = request.Query["class"];
		string? collection = request.Query["collection"];
		string? sort = request.Query["sort"];
		bool descending = request.Query["desc"] == "true";
		int offset = ParseInt(request.Query["offset"], 0);
		int limit = Math.Clamp(ParseInt(request.Query["limit"], 200), 1, MaxLimit);

		IReadOnlyList<IUnityObjectBase> assets = AssetCatalog.Query(search, className, collection, sort, descending, offset, limit, out int total);
		List<AssetRowDto> rows = new(assets.Count);
		foreach (IUnityObjectBase asset in assets)
		{
			rows.Add(AssetProbe.ToRow(asset));
		}

		return Results.Json(new PageDto<AssetRowDto>(rows, total, offset, limit));
	}

	private static IResult GetAssetDetail(HttpRequest request)
	{
		if (!TryGetAsset(request, out IUnityObjectBase? asset))
		{
			return Results.NotFound("找不到资源。");
		}

		List<PropertyDto> properties = new();
		void Add(string name, string? value)
		{
			if (!string.IsNullOrEmpty(value))
			{
				properties.Add(new PropertyDto(name, value));
			}
		}

		Add("类名", asset.ClassName);
		Add("ClassID", asset.ClassID.ToString());
		Add("PathID", asset.PathID.ToString());
		Add("集合", asset.Collection.Name);
		Add("集合路径", asset.Collection.FilePath);
		Add("原始路径", asset.OriginalPath ?? asset.OverridePath);
		Add("原始文件名", asset.OriginalName);
		Add("扩展名", asset.GetBestExtension());
		Add("主资源", asset.MainAsset?.GetBestName());
		Add("AssetBundle", asset.AssetBundleName);

		if (asset is IImageTexture texture)
				{
					try
					{
						Add("尺寸", $"{texture.Width_C189} × {texture.Height_C189}");
					}
			catch (Exception)
			{
				// 有些版本的 Texture 缺少这些字段，跳过即可。
			}
		}

		List<string> previews = new();
		if (AssetProbe.HasImage(asset)) previews.Add("image");
		if (AssetProbe.HasAudio(asset)) previews.Add("audio");
		if (AssetProbe.HasModel(asset)) previews.Add("model");
		if (AssetProbe.HasFont(asset)) previews.Add("font");
		if (AssetProbe.HasVideo(asset)) previews.Add("video");
		if (AssetProbe.HasText(asset)) previews.Add("text");
		if (AssetProbe.GetJson(asset) is not null) previews.Add("json");
		if (AssetProbe.GetYaml(asset) is not null) previews.Add("yaml");

		return Results.Json(
			new AssetDetailDto(
				PathId: asset.PathID,
				ClassId: asset.ClassID,
				ClassName: asset.ClassName,
				Name: asset.GetBestName(),
				Collection: asset.Collection.Name,
				CollectionPath: asset.Collection.GetPath().ToJson(),
				OriginalPath: asset.OriginalPath ?? asset.OverridePath,
				Properties: properties,
								Previews: previews));
	}

	private static IResult GetAssetText(HttpRequest request)
	{
		if (!TryGetAsset(request, out IUnityObjectBase? asset))
		{
			return Results.NotFound("找不到资源。");
		}
		string? text = AssetProbe.GetText(asset);
		return text is null ? Results.NotFound("该资源没有文本内容。") : Results.Text(text, "text/plain; charset=utf-8");
	}

	private static IResult GetAssetJson(HttpRequest request)
	{
		if (!TryGetAsset(request, out IUnityObjectBase? asset))
		{
			return Results.NotFound("找不到资源。");
		}
		string? json = AssetProbe.GetJson(asset);
		return json is null ? Results.NotFound() : Results.Text(json, "application/json; charset=utf-8");
	}

	private static IResult GetAssetYaml(HttpRequest request)
	{
		if (!TryGetAsset(request, out IUnityObjectBase? asset))
		{
			return Results.NotFound("找不到资源。");
		}
		string? yaml = AssetProbe.GetYaml(asset);
		return yaml is null ? Results.NotFound() : Results.Text(yaml, "application/yaml; charset=utf-8");
	}

	#endregion

	#region 二进制预览

	private enum AssetKind
	{
		Image,
		Audio,
		Model,
		Font,
		Video,
		Binary,
	}

	private static IResult ServeAssetBytes(HttpRequest request, AssetKind kind)
	{
		if (!TryGetAsset(request, out IUnityObjectBase? asset))
		{
			return Results.NotFound("找不到资源。");
		}

		switch (kind)
		{
			case AssetKind.Image:
				{
					ImageExportFormat format = ImageExportFormat.Png;
					string? extension = request.Query["ext"];
					if (extension is not null && ImageExportFormat.TryGetFromExtension(extension, out ImageExportFormat parsed))
					{
						format = parsed;
					}
					byte[]? bytes = AssetProbe.GetImageBytes(asset, format);
					return bytes is null
						? Results.NotFound("图片无法解码。")
						: Results.Bytes(bytes, $"image/{format.GetFileExtension()}");
				}

			case AssetKind.Audio:
				{
					(byte[] Data, string Mime)? result = AssetProbe.GetAudioBytes(asset, request.Query["ext"]);
					return result is null ? Results.NotFound("音频无法解码。") : Results.Bytes(result.Value.Data, result.Value.Mime);
				}

			case AssetKind.Model:
				{
					byte[]? bytes = AssetProbe.GetModelBytes(asset);
					return bytes is null
						? Results.NotFound("模型无法解码。")
						: Results.Bytes(bytes, "model/gltf-binary", "model.glb");
				}

			case AssetKind.Font:
				{
					(byte[] Data, string Mime)? result = AssetProbe.GetFontBytes(asset);
					return result is null
						? Results.NotFound("字体无法读取。")
						: Results.Bytes(result.Value.Data, result.Value.Mime, $"{asset.GetBestName()}");
				}

			case AssetKind.Video:
				{
					(byte[] Data, string Mime)? result = AssetProbe.GetVideoBytes(asset);
					return result is null ? Results.NotFound("视频无法读取。") : Results.Bytes(result.Value.Data, result.Value.Mime);
				}

			case AssetKind.Binary:
				{
					(byte[] Data, string Mime)? result = AssetProbe.GetRawBytes(asset);
					return result is null ? Results.NotFound("该资源没有原始数据。") : Results.Bytes(result.Value.Data, result.Value.Mime);
				}

			default:
				return Results.BadRequest();
		}
	}

	#endregion

	#region 设置 / 操作 / 日志

	private static IResult GetUiText()
	{
		UiTextDto dto = new(
			Language: LocalizedText.CurrentLanguage,
			Languages: LocalizedText.Languages.Select(l => new LanguageOptionDto(l.Code, l.Name)).ToList(),
			Strings: LocalizedText.Export());
		return Results.Json(dto);
	}

	private static IResult SetLanguage(LanguageRequest body)
	{
		LocalizedText.SetLanguage(body.Language);
		// 顺便同步给 AssetRipper 自己的本地化，让导出日志也用同一种语言。
		AssetRipper.GUI.Localizations.Localization.LoadLanguage(body.Language);
		return Results.Json(new UiTextDto(
			Language: LocalizedText.CurrentLanguage,
			Languages: LocalizedText.Languages.Select(l => new LanguageOptionDto(l.Code, l.Name)).ToList(),
			Strings: LocalizedText.Export()));
	}

	private static IResult UpdateSettings(SettingsUpdateRequest body)
	{
		string? error = SettingsService.Update(body.Values ?? new Dictionary<string, string?>());
		return error is null
			? Results.Json(SettingsService.Get())
			: Results.BadRequest(new { error });
	}

	private static IResult StartLoad(LoadRequest body)
	{
		if (OperationService.Instance.IsBusy)
		{
			return Results.Conflict(new { error = "已有任务在运行。" });
		}
		if (!OperationService.Instance.StartLoad(body.Paths ?? [], out string? error))
		{
			return Results.BadRequest(new { error });
		}
		return Results.Json(OperationService.Instance.Current);
	}

	private static IResult StartExport(ExportRequest body)
	{
		if (OperationService.Instance.IsBusy)
		{
			return Results.Conflict(new { error = "已有任务在运行。" });
		}
		if (!OperationService.Instance.StartExport(body.OutputPath ?? string.Empty, body.CreateSubfolder, out string? error))
		{
			return Results.BadRequest(new { error });
		}
		return Results.Json(OperationService.Instance.Current);
	}

	private static IResult GetLog(HttpRequest request)
	{
		if (request.Query.ContainsKey("since"))
		{
			long since = ParseLong(request.Query["since"], 0);
			int limit = Math.Clamp(ParseInt(request.Query["limit"], 500), 1, 2000);
			(IReadOnlyList<LogEntryDto> items, long lastSeq) = LogService.Instance.Since(since, limit);
			return Results.Json(new { items, lastSeq });
		}
		else
		{
			int tail = Math.Clamp(ParseInt(request.Query["tail"], 300), 1, 2000);
			IReadOnlyList<LogEntryDto> items = LogService.Instance.Tail(tail);
			return Results.Json(new { items, lastSeq = LogService.Instance.Since(long.MaxValue, 0).LastSeq });
		}
	}

	#endregion

	#region 参数解析

	private static bool TryGetAsset(HttpRequest request, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IUnityObjectBase? asset)
	{
		asset = null;
		string? path = request.Query["path"];
		if (string.IsNullOrEmpty(path) || !long.TryParse(request.Query["pathId"], out long pathId))
		{
			return false;
		}
		asset = AssetProbe.FindAsset(path, pathId);
		return asset is not null;
	}

	private static int ParseInt(string? value, int fallback)
		=> int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;

	private static long ParseLong(string? value, long fallback)
		=> long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long parsed) ? parsed : fallback;

	#endregion
}


