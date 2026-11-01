using System.Text.RegularExpressions;
using AssetRipper.Export.UnityProjects;
using AssetRipper.IO.Files;
using AssetRipper.Processing;
using AssetRipperPatcher.Patching;

namespace AssetRipperPatcher.Patches;

/// <summary>
/// 旧版动态字体修复补丁。
/// AssetRipper 导出的 Font 资源（ClassID 128）若 m_FontData 为空（游戏运行时用系统字体动态渲染），
/// 其 m_Texture / m_DefaultMaterial 引用的是运行时才生成的字体图集，在导出工程中并不存在，
/// Unity 加载时报 "Font texture for dynamic font X is missing. Please reimport the Font."
/// 修复：把这两个悬空引用置为 {fileID: 0}，让 Unity 按 m_FontNames（如 Times New Roman）动态渲染。
/// </summary>
internal static partial class LegacyFontPatch
{
    /// <summary>m_FontData 为空（行尾直接结束，无内嵌二进制字体数据）。</summary>
    [GeneratedRegex(@"(?m)^  m_FontData:[ \t]*$")]
    private static partial Regex EmptyFontDataRegex();

    /// <summary>m_Texture 对已导出纹理的引用（fileID 2800000 + guid）。</summary>
    [GeneratedRegex(@"(?m)^  m_Texture: \{fileID: 2800000, guid: [0-9a-f]+, type: 2\}")]
    private static partial Regex TextureRefRegex();

    /// <summary>m_DefaultMaterial 对已导出材质的引用（fileID 2100000 + guid）。</summary>
    [GeneratedRegex(@"(?m)^  m_DefaultMaterial: \{fileID: 2100000, guid: [0-9a-f]+, type: 2\}")]
    private static partial Regex MaterialRefRegex();

    /// <summary>
    /// Harmony Postfix: 导出完成后扫描所有 Font 资源，把动态字体的悬空图集引用置空。
    /// </summary>
    [PatchMethod(typeof(ExportHandler), nameof(ExportHandler.Export), PatchType = PatchType.Postfix)]
    public static void FixLegacyFontsAfterExport(GameData gameData, string outputPath, FileSystem fileSystem)
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
                if (FixFontFile(file))
                {
                    fixedCount++;
                }
            }
            if (fixedCount > 0)
            {
                Console.WriteLine($"Fixed {fixedCount} dynamic font asset(s) (cleared missing font atlas references)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FixLegacyFonts failed: {ex}");
        }
    }

    /// <summary>
    /// 修复单个 Font 资源：仅当它是"无内嵌字体数据"的动态字体时，
    /// 把 m_Texture / m_DefaultMaterial 的悬空引用替换为 {fileID: 0}。
    /// 有内嵌 ttf 数据（m_FontData 后跟二进制）或已有图集的字体不受影响。
    /// </summary>
    private static bool FixFontFile(string file)
    {
        try
        {
            string content = System.IO.File.ReadAllText(file);
            if (!content.Contains("--- !u!128 &"))
            {
                return false; // 不是 Font 资源
            }
            // 只处理动态字体：m_FontData 为空说明字体数据在运行时来自系统字体
            if (!EmptyFontDataRegex().IsMatch(content))
            {
                return false;
            }

            string newContent = TextureRefRegex().Replace(content, "  m_Texture: {fileID: 0}");
            newContent = MaterialRefRegex().Replace(newContent, "  m_DefaultMaterial: {fileID: 0}");
            if (newContent == content)
            {
                return false;
            }

            System.IO.File.WriteAllText(file, newContent);
            return true;
        }
        catch
        {
            // 忽略单个文件的错误
            return false;
        }
    }
}
