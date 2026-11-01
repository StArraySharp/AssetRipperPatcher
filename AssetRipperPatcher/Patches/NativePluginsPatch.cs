using AssetRipper.Export.UnityProjects;
using AssetRipper.IO.Files;
using AssetRipper.Processing;
using AssetRipperPatcher.Patching;

namespace AssetRipperPatcher.Patches;

/// <summary>
/// 导出后处理补丁：导出 Unity 项目完成后，把游戏数据目录下的原生库复制到导出项目的
/// Assets/Plugins，并修复 TMP 字体源引用"分家"问题。
/// </summary>
internal static class NativePluginsPatch
{
    /// <summary>
    /// Harmony Postfix: 导出 Unity 项目完成后，把游戏数据目录 (GameName_Data) 下
    /// Plugins 中的所有子目录（原生库）复制到导出项目的 Assets/Plugins 下。
    /// 源目录为 {GameName_Data}/Plugins，目标为 {输出目录}/ExportedProject/Assets/Plugins。
    /// </summary>
    [PatchMethod(typeof(ExportHandler), "Export", PatchType = PatchType.Postfix)]
    public static void CopyNativePluginsAfterExport(GameData gameData, string outputPath, FileSystem fileSystem)
    {
        try
        {
            // 修复 TMP 字体源引用分家（先于原生库复制，无论如何都执行）
            FixTmpFontSourceGuids(outputPath);

            string? gameDataPath = gameData.PlatformStructure?.GameDataPath;
            if (string.IsNullOrEmpty(gameDataPath))
            {
                Console.WriteLine("Skip native plugins copy: GameDataPath not found");
                return;
            }

            string sourcePlugins = System.IO.Path.Combine(gameDataPath, "Plugins");
            if (!System.IO.Directory.Exists(sourcePlugins))
            {
                Console.WriteLine($"Skip native plugins copy: '{sourcePlugins}' not found");
                return;
            }

            string targetPlugins = System.IO.Path.Combine(outputPath, "ExportedProject", "Assets", "Plugins");
            System.IO.Directory.CreateDirectory(targetPlugins);

            int count = 0;
            foreach (string subDirectory in System.IO.Directory.EnumerateDirectories(sourcePlugins))
            {
                string name = System.IO.Path.GetFileName(subDirectory);
                CopyDirectory(subDirectory, System.IO.Path.Combine(targetPlugins, name));
                count++;
            }
            Console.WriteLine($"Copied {count} native plugin folder(s) from '{sourcePlugins}' to '{targetPlugins}'");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to copy native plugins: {ex}");
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        System.IO.Directory.CreateDirectory(target);
        foreach (string file in System.IO.Directory.EnumerateFiles(source))
        {
            string fileName = System.IO.Path.GetFileName(file);
            System.IO.File.Copy(file, System.IO.Path.Combine(target, fileName), true);
        }
        foreach (string subDirectory in System.IO.Directory.EnumerateDirectories(source))
        {
            string name = System.IO.Path.GetFileName(subDirectory);
            CopyDirectory(subDirectory, System.IO.Path.Combine(target, name));
        }
    }

    /// <summary>
    /// 修复 TMP 字体"分家"：把 TMP FontAsset 里的 m_SourceFontFileGUID / sourceFontFileGUID
    /// 同步为 PPtr 引用的实际字体 GUID（AssetRipper 导出字体时生成的新 GUID）。
    /// TMP 运行时用 m_SourceFontFileGUID 字符串查找源字体，不同步会导致字体本体和字体资源分家。
    /// </summary>
    private static void FixTmpFontSourceGuids(string outputPath)
    {
        try
        {
            string assetsPath = System.IO.Path.Combine(outputPath, "ExportedProject", "Assets");
            if (!System.IO.Directory.Exists(assetsPath))
            {
                return;
            }

            int fixedCount = 0;
            foreach (string file in System.IO.Directory.EnumerateFiles(assetsPath, "*.asset", SearchOption.AllDirectories))
            {
                try
                {
                    string content = System.IO.File.ReadAllText(file);
                    // 匹配 m_SourceFontFile: {fileID: 12800000, guid: X, type: 3}（fileID 0 表示未绑定源字体，跳过）
                    System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(content,
                        @"m_SourceFontFile:\s*\{fileID:\s*12800000,\s*guid:\s*([a-f0-9]+),\s*type:\s*3\}");
                    if (!m.Success)
                    {
                        continue;
                    }
                    string ppGuid = m.Groups[1].Value;
                    // 匹配 m_SourceFontFileGUID: Y
                    System.Text.RegularExpressions.Match m2 = System.Text.RegularExpressions.Regex.Match(content,
                        @"m_SourceFontFileGUID:\s*([a-f0-9]+)");
                    if (!m2.Success || m2.Groups[1].Value == ppGuid)
                    {
                        continue;
                    }
                    string oldGuid = m2.Groups[1].Value;
                    string newContent = content
                        .Replace("m_SourceFontFileGUID: " + oldGuid, "m_SourceFontFileGUID: " + ppGuid)
                        .Replace("sourceFontFileGUID: " + oldGuid, "sourceFontFileGUID: " + ppGuid);
                    if (newContent != content)
                    {
                        System.IO.File.WriteAllText(file, newContent);
                        fixedCount++;
                    }
                }
                catch
                {
                    // 忽略单个文件的错误
                }
            }
            if (fixedCount > 0)
            {
                Console.WriteLine($"Fixed TMP font source GUIDs in {fixedCount} asset(s)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FixTmpFontSourceGuids failed: {ex}");
        }
    }
}
