using System.Text;
using System.Text.RegularExpressions;

namespace AssetRipperPatcher;

/// <summary>
/// 把工程里指向「已被换成 UPM 包的裸 DLL」的脚本引用，改写成包内真实的 <c>.cs.meta</c> 引用。
///
/// <para>导出阶段 <see cref="Patches.MetaPtrPatch"/> 已经在写 YAML 时顺手改写了引用；
/// 这个类是**兜底**：处理那些由别处（用户自己的工具、旧版本导出、手改过）留下的引用。</para>
///
/// <para><b>怎么知道该改哪条。</b>
/// 先用 <see cref="UnityPackageCatalog.CalculateAssemblyGuid"/> 把「工程里出现的 guid」
/// 反查回程序集名——<c>guid = MD5(assemblyName)</c> 是确定性的，遍历已知程序集算一遍即可。
/// 再用 <c>fileID</c> 反查 (namespace, name)：同样遍历候选类型算 MD4。
/// 两条都对上才改写，避免误伤。</para>
/// </summary>
internal static partial class ScriptReferenceRewriter
{
    /// <summary>Unity 里 <c>m_Script</c> 指向 MonoScript 时 <c>type</c> 恒为 3。</summary>
    private const int MonoScriptAssetType = 3;

    /// <summary>匹配一个脚本引用：<c>{fileID: N, guid: HEX, type: 3}</c>。</summary>
    [GeneratedRegex(@"\{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-fA-F]{32}),\s*type:\s*3\}", RegexOptions.Compiled)]
    private static partial Regex ScriptPointerRegex();

    /// <summary>Unity 工程里可能是文本、且含脚本引用的资产扩展名。</summary>
    private static readonly HashSet<string> TextAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".prefab", ".unity", ".asset", ".mat", ".anim", ".controller", ".overrideController",
        ".physicMaterial", ".physicsMaterial2D", ".preset", ".lighting", ".renderTexture",
        ".mixer", ".guiskin", ".fontsettings", ".terrainlayer", ".signal", ".playable",
    };

    /// <summary>改写结果统计。</summary>
    /// <param name="FilesScanned">扫描的文本资产数。</param>
    /// <param name="FilesChanged">实际改写的文件数。</param>
    /// <param name="ReferencesRewritten">改写的引用条数。</param>
    /// <param name="ReferencesUnresolved">看起来像脚本引用、但查不到包内类型的条数。</param>
    public readonly record struct Result(
        int FilesScanned,
        int FilesChanged,
        int ReferencesRewritten,
        int ReferencesUnresolved);

    /// <summary>
    /// 扫描并改写 <paramref name="assetsDirectory"/> 下所有文本资产里的脚本引用。
    /// </summary>
    /// <param name="assetsDirectory"><c>ExportedProject/Assets</c>。</param>
    /// <param name="replacedAssemblies">
    /// 已经被换成包的**程序集名**（不含扩展名）。只有指向这些程序集的引用才会被改写——
    /// 其它程序集的 DLL 还在原地，引用是有效的，不能动。
    /// </param>
    public static Result Run(string assetsDirectory, ISet<string> replacedAssemblies)
    {
        if (replacedAssemblies.Count == 0 || !Directory.Exists(assetsDirectory))
        {
            return new Result(0, 0, 0, 0);
        }

        // guid → 程序集名。guid 是 MD5(程序集名)，确定性，可以反查。
        Dictionary<string, string> assemblyByGuid = new(StringComparer.OrdinalIgnoreCase);
        foreach (string assembly in replacedAssemblies)
        {
            assemblyByGuid[UnityPackageCatalog.CalculateAssemblyGuid(assembly).ToString()] = assembly;
        }

        // (程序集, fileID) → 包内 GUID。fileID 是 MD4(ns+name) 的前 4 字节。
        Dictionary<(string Assembly, long FileId), UnityPackageCatalog.ScriptEntry> byFileId = new();
        foreach (string assembly in replacedAssemblies)
        {
            foreach (UnityPackageCatalog.ScriptEntry entry in UnityPackageCatalog.ForAssembly(assembly))
            {
                byFileId[(assembly, UnityPackageCatalog.CalculateScriptFileId(entry.Namespace, entry.TypeName))] = entry;
            }
        }

        int scanned = 0;
        int changed = 0;
        int rewritten = 0;
        int unresolved = 0;

        foreach (string file in Directory.EnumerateFiles(assetsDirectory, "*", SearchOption.AllDirectories))
        {
            if (!TextAssetExtensions.Contains(Path.GetExtension(file)))
            {
                continue;
            }
            scanned++;

            string content;
            try
            {
                content = File.ReadAllText(file);
            }
            catch
            {
                continue;
            }

            if (!content.Contains("m_Script", StringComparison.Ordinal)
                && !content.Contains("guid:", StringComparison.Ordinal))
            {
                continue;
            }

            int localRewritten = 0;
            int localUnresolved = 0;
            string updated = ScriptPointerRegex().Replace(content, match =>
            {
                string guid = match.Groups[2].Value;
                if (!assemblyByGuid.TryGetValue(guid, out string? assembly))
                {
                    return match.Value; // 不是被换掉的那些程序集
                }

                long fileId = long.Parse(match.Groups[1].Value);
                if (byFileId.TryGetValue((assembly, fileId), out UnityPackageCatalog.ScriptEntry entry))
                {
                    localRewritten++;
                    return $"{{fileID: {UnityPackageCatalog.MonoScriptFileId}, guid: {entry.Guid}, type: {MonoScriptAssetType}}}";
                }

                // 属于被换掉的程序集，但包内找不到这个类型：说明它是游戏自己的
                // 类型，或者包里没有对应源码。保留原样，让用户能看出是哪个程序集。
                localUnresolved++;
                return match.Value;
            });

            if (localRewritten > 0)
            {
                File.WriteAllText(file, updated, new UTF8Encoding(false));
                changed++;
                rewritten += localRewritten;
            }
            unresolved += localUnresolved;
        }

        return new Result(scanned, changed, rewritten, unresolved);
    }
}
