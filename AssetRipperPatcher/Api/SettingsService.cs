using AssetRipper.Export.Configuration;
using AssetRipper.GUI.Web;
using AssetRipper.Import.Configuration;
using AssetRipper.Processing.Configuration;

namespace AssetRipperPatcher.Api;

/// <summary>
/// 设置页的数据源。
///
/// <para><b>为什么手写清单而不是走源生成器。</b>AssetRipper 的 SettingsPage.g.cs 由
/// 源生成器从 Razor 式页面生成，只产出 HTML 片段。这里用「键 → 反射 setter」的显式清单，
/// 前端拿到的是结构化 schema，可以直接渲染成 Fluent 控件，并且带上每个选项的说明。</para>
///
/// <para><b>说明文案从哪来。</b>与原始 WebUI 一一对应：<c>DropDownSetting&lt;T&gt;</c> 子类
/// 里的 <c>GetDisplayName</c>/<c>GetDescription</c>，以及 <c>Localizations/*.json</c> 的原文。
/// 这里把它们收进 <see cref="LocalizedText"/> 的 <c>opt.*</c> / <c>desc.*</c> 前缀下。</para>
/// </summary>
internal static class SettingsService
{
	private static FullConfiguration Configuration => GameFileLoader.Settings;

	public static SettingsDto Get()
	{
		ImportSettings import = Configuration.ImportSettings;
		ProcessingSettings processing = Configuration.ProcessingSettings;
		ExportSettings export = Configuration.ExportSettings;

		return new SettingsDto(
			// 与上游一致：导入资源后设置锁定，避免导出中途改配置导致不一致。
			ReadOnly: GameFileLoader.IsLoaded,
			Groups:
			[
				new SettingGroupDto("import",
				[
					Enum<ScriptContentLevel>("Import.ScriptContentLevel", import.ScriptContentLevel),
					Enum<StreamingAssetsMode>("Import.StreamingAssetsMode", import.StreamingAssetsMode),
					Text("Import.DefaultVersion", import.DefaultVersion.ToString()),
					Text("Import.TargetVersion", import.TargetVersion.ToString()),
					Bool("Processing.EnablePrefabOutlining", processing.EnablePrefabOutlining),
					Bool("Processing.EnableStaticMeshSeparation", processing.EnableStaticMeshSeparation),
					Enum<BundledAssetsExportMode>("Processing.BundledAssetsExportMode", processing.BundledAssetsExportMode),
				]),
				new SettingGroupDto("export",
				[
					Enum<AudioExportFormat>("Export.AudioExportFormat", export.AudioExportFormat),
					Enum<ImageExportFormat>("Export.ImageExportFormat", export.ImageExportFormat),
					Enum<LightmapTextureExportFormat>("Export.LightmapTextureExportFormat", export.LightmapTextureExportFormat),
					Enum<SpriteExportMode>("Export.SpriteExportMode", export.SpriteExportMode),
					Enum<ShaderExportMode>("Export.ShaderExportMode", export.ShaderExportMode),
					Enum<TextExportMode>("Export.TextExportMode", export.TextExportMode),
										Enum<ScriptExportMode>("Export.ScriptExportMode", export.ScriptExportMode),
					Enum<ScriptLanguageVersion>("Export.ScriptLanguageVersion", export.ScriptLanguageVersion),
					Bool("Export.ScriptTypesFullyQualified", export.ScriptTypesFullyQualified),
					Bool("Export.PreferOriginalTextureExtension", export.PreferOriginalTextureExtension),
					Bool("Export.ExportUnreadableAssets", export.ExportUnreadableAssets),
					Bool("Export.SaveSettingsToDisk", export.SaveSettingsToDisk),
				]),
				new SettingGroupDto("processing",
				[
					Bool("Processing.RemoveNullableAttributes", processing.RemoveNullableAttributes),
					Bool("Processing.PublicizeAssemblies", processing.PublicizeAssemblies),
				]),
				new SettingGroupDto("experimental",
				[
					Bool("Processing.EnableAssetDeduplication", processing.EnableAssetDeduplication),
				]),
			]);
	}

	/// <summary>设置项标题。</summary>
	private static string Label(string key) => LocalizedText.Get($"setting.{key}");

	/// <summary>设置项自身的说明（不是选项说明）。没有则为 null。</summary>
	private static string? Description(string key) => TryGetText($"setting.{key}.desc");

	private static SettingDto Bool(string key, bool value)
		=> new(key, Label(key), Description(key), "bool", value ? "true" : "false", null);

	private static SettingDto Text(string key, string value)
		=> new(key, Label(key), Description(key), "text", value, null);

	/// <summary>
	/// 枚举项。每个选项都带显示名和说明，前端在选择后立即显示对应说明 ——
	/// 这正是原始 WebUI 里 <c>option-description</c> 那段的效果。
	/// </summary>
	private static SettingDto Enum<TEnum>(string key, TEnum value) where TEnum : struct, System.Enum
	{
		string shortName = typeof(TEnum).Name;
		List<SettingOptionDto> options = new();
		foreach (TEnum item in System.Enum.GetValues<TEnum>())
		{
			string name = item.ToString();
			options.Add(new SettingOptionDto(
				Value: name,
				Label: TryGetText($"opt.{shortName}.{name}") ?? name,
				Description: OptionDescription(key, shortName, name)));
		}

		return new SettingDto(key, Label(key), Description(key), "enum", value.ToString(), options);
	}

	/// <summary>缺键时返回 null，这样前端可以区分「没有说明」和「说明是空串」。</summary>
	private static string? TryGetText(string key)
	{
		string text = LocalizedText.Get(key);
		// LocalizedText.Get 在两种语言都缺键时返回键本身，据此判断。
		return text.StartsWith("opt.", StringComparison.Ordinal) || text.StartsWith("desc.", StringComparison.Ordinal)
			|| text.StartsWith("setting.", StringComparison.Ordinal)
			? null
			: text;
	}

	private static string? OptionDescription(string settingKey, string shortName, string valueName)
	{
		// 优先按「设置项」的键找（图片/光照贴图这类整组共用一段说明），
		// 再退回按「枚举类型 + 值」的键找。
		return TryGetText($"desc.{settingKey}") ?? TryGetText($"desc.{shortName}.{valueName}");
	}

	/// <summary>应用一批键值改动。返回 null 表示成功，否则是错误说明。</summary>
	public static string? Update(IReadOnlyDictionary<string, string?> values)
	{
		if (GameFileLoader.IsLoaded)
		{
			return LocalizedText.Get("settings.locked");
		}

		foreach ((string key, string? value) in values)
		{
			string? error = Apply(key, value);
			if (error is not null)
			{
				return error;
			}
		}

		Configuration.MaybeSaveToDefaultPath();
		return null;
	}

	private static string? Apply(string key, string? value)
	{
		ImportSettings import = Configuration.ImportSettings;
		ProcessingSettings processing = Configuration.ProcessingSettings;
		ExportSettings export = Configuration.ExportSettings;

		try
		{
			switch (key)
			{
				case "Import.ScriptContentLevel": import.ScriptContentLevel = Parse<ScriptContentLevel>(value); break;
				case "Import.StreamingAssetsMode": import.StreamingAssetsMode = Parse<StreamingAssetsMode>(value); break;
				case "Import.DefaultVersion": import.DefaultVersion = AssetRipper.Primitives.UnityVersion.Parse(value ?? ""); break;
				case "Import.TargetVersion": import.TargetVersion = AssetRipper.Primitives.UnityVersion.Parse(value ?? ""); break;

				case "Processing.EnablePrefabOutlining": processing.EnablePrefabOutlining = ParseBool(value); break;
				case "Processing.EnableStaticMeshSeparation": processing.EnableStaticMeshSeparation = ParseBool(value); break;
				case "Processing.EnableAssetDeduplication": processing.EnableAssetDeduplication = ParseBool(value); break;
				case "Processing.RemoveNullableAttributes": processing.RemoveNullableAttributes = ParseBool(value); break;
				case "Processing.PublicizeAssemblies": processing.PublicizeAssemblies = ParseBool(value); break;
				case "Processing.BundledAssetsExportMode": processing.BundledAssetsExportMode = Parse<BundledAssetsExportMode>(value); break;

				case "Export.AudioExportFormat": export.AudioExportFormat = Parse<AudioExportFormat>(value); break;
				case "Export.ImageExportFormat": export.ImageExportFormat = Parse<ImageExportFormat>(value); break;
				case "Export.LightmapTextureExportFormat": export.LightmapTextureExportFormat = Parse<LightmapTextureExportFormat>(value); break;
				case "Export.ScriptExportMode": export.ScriptExportMode = Parse<ScriptExportMode>(value); break;
				case "Export.ScriptLanguageVersion": export.ScriptLanguageVersion = Parse<ScriptLanguageVersion>(value); break;
				case "Export.ScriptTypesFullyQualified": export.ScriptTypesFullyQualified = ParseBool(value); break;
				case "Export.ShaderExportMode": export.ShaderExportMode = Parse<ShaderExportMode>(value); break;
				case "Export.SpriteExportMode": export.SpriteExportMode = Parse<SpriteExportMode>(value); break;
				case "Export.TextExportMode": export.TextExportMode = Parse<TextExportMode>(value); break;
								case "Export.ExportUnreadableAssets": export.ExportUnreadableAssets = ParseBool(value); break;
				case "Export.PreferOriginalTextureExtension": export.PreferOriginalTextureExtension = ParseBool(value); break;
				case "Export.SaveSettingsToDisk": export.SaveSettingsToDisk = ParseBool(value); break;

				default:
					return $"Unknown setting: {key}";
			}
		}
		catch (Exception ex)
		{
			return $"{key}: '{value}' → {ex.Message}";
		}

		return null;

		static TEnum Parse<TEnum>(string? value) where TEnum : struct, System.Enum
			=> System.Enum.TryParse(value, ignoreCase: true, out TEnum parsed)
				? parsed
				: throw new FormatException($"not a valid {typeof(TEnum).Name}");

		static bool ParseBool(string? value)
			=> bool.TryParse(value, out bool parsed) ? parsed : throw new FormatException("not a valid boolean");
	}
}