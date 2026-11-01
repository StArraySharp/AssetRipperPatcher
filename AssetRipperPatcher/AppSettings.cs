using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.IO.Files;

namespace AssetRipperPatcher;

/// <summary>
/// 统一的配置入口：首次运行时生成默认设置文件，之后每次加载。
///
/// <para><b>为什么需要这个。</b>
/// AssetRipper 的 <see cref="FullConfiguration"/> 默认值并不适合「导出可编辑 Unity 工程」：
/// <c>SpriteExportMode</c> 默认是 <c>Yaml</c>（Sprite 只导出成 .asset，纹理 importer 里
/// outline/mesh 全空），<c>TextExportMode</c> 默认是 <c>Bytes</c>（文本资源导出成 .bytes）。
/// 而 <c>Program.cs</c> 之前完全不读设置文件，等于一直用这套默认值。</para>
///
/// <para><b>行为。</b>
/// <list type="number">
/// <item>设置文件存在 → 直接加载（尊重用户改动）。</item>
/// <item>不存在 → 用下面的「推荐默认值」新建并写盘，让 GUI 和命令行看到同一份配置。</item>
/// </list>
/// 写盘由 <c>SaveSettingsToDisk</c> 控制（推荐默认值里打开）。</para>
/// </summary>
internal static class AppSettings
{
    /// <summary>
    /// 首次运行生成的推荐配置。命令行与 Web UI 共用，所以两边导出的结果一致。
    /// </summary>
    public static FullConfiguration LoadOrCreate()
    {
        FullConfiguration settings = new();

        bool existed = File.Exists(GetSettingsFilePath());
        settings.LoadFromDefaultPath();

        if (existed)
        {
            Logger.Info(LogCategory.General, $"Settings loaded from '{GetSettingsFilePath()}'");
            return settings;
        }

        ApplyRecommendedDefaults(settings);
        TrySave(settings);
        return settings;
    }

    /// <summary>
    /// 推荐默认值：目标是「导出后能直接在 Unity 里打开并正确显示」。
    /// </summary>
    private static void ApplyRecommendedDefaults(FullConfiguration settings)
    {
        // Sprite 用 Unity 原生 importer 格式（把网格/轮廓写进 .meta），
        // 而不是 Yaml（只产出 .asset，importer 里 outline 为空）。
        settings.ExportSettings.SpriteExportMode = SpriteExportMode.Native;

        // 文本资源导出成纯文本文件，方便直接阅读/编辑。
        settings.ExportSettings.TextExportMode = TextExportMode.Txt;

        // 让上面的选择落盘，下次启动仍是同一份配置。
        settings.ExportSettings.SaveSettingsToDisk = true;
    }

    private static void TrySave(FullConfiguration settings)
    {
        try
        {
            settings.SaveToDefaultPath();
            Logger.Info(LogCategory.General,
                $"Settings file created at '{GetSettingsFilePath()}' " +
                $"(SpriteExportMode={settings.ExportSettings.SpriteExportMode}, " +
                $"TextExportMode={settings.ExportSettings.TextExportMode})");
        }
        catch (Exception ex)
        {
            Logger.Warning(LogCategory.General, $"Failed to write settings file: {ex.Message}");
        }
    }

    private static string GetSettingsFilePath()
    {
        return Path.Join(LocalFileSystem.ExecutingDirectory, SerializedSettings.DefaultFileName);
    }
}
