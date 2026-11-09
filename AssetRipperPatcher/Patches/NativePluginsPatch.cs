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
    /// 被「裸 DLL 换成包」这条规则剔除的程序集所对应的 UPM 包。
    ///
    /// 清单文件（<c>Packages/manifest.json</c>）由另一个更早的 Postfix
    /// （<see cref="PackageManifestPatch"/>）写出，那时插件还没处理完，
    /// 所以这里先攒起来，导出结束后由 <see cref="Program"/> 一次性补写。
    /// 键是包名，值是版本（空串表示交给 Unity 解析）。
    /// </summary>
    public static readonly Dictionary<string, string> PendingPackages = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 把 <see cref="PendingPackages"/> 汇总打印一行（导出结束时调用）。
    /// 实际写清单由 <see cref="PackageManifestPatch.AddDependencies"/> 在插件处理完时完成。
    /// </summary>
    public static void ReportPackageSummary()
    {
        if (PendingPackages.Count == 0)
        {
            return;
        }
        string list = string.Join(", ", PendingPackages.Select(static p =>
            string.IsNullOrEmpty(p.Value) ? p.Key : $"{p.Key}@{p.Value}"));
        Console.WriteLine($"[Plugins] {PendingPackages.Count} packaged DLL(s) replaced with UPM dependencies: {list}");
    }
    /// <summary>
    /// Harmony Postfix: 导出 Unity 项目完成后，把游戏数据目录 (GameName_Data) 下
    /// Plugins 中的所有子目录（原生库）复制到导出项目的 Assets/Plugins 下。
    /// 源目录为 {GameName_Data}/Plugins，目标为 {输出目录}/ExportedProject/Assets/Plugins。
    ///
    /// <para><b>Priority。</b>
    /// <see cref="LegacyFontPatch.FixLegacyFontsAfterExport"/> 也是
    /// <c>ExportHandler.Export</c> 的 Postfix，两者互不依赖；显式声明
    /// <see cref="HarmonyLib.Priority.LowerThanNormal"/> 让它排在字体修复之后，
    /// 避免执行顺序取决于 <c>assembly.GetTypes()</c>。</para>
    /// </summary>
    [PatchMethod(typeof(ExportHandler), "Export",
        PatchType = PatchType.Postfix, Priority = HarmonyLib.Priority.LowerThanNormal)]
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

            // 拷贝只是把字节搬过去。Unity 还需要两件事才能正确导入：
            //   1. 每个插件一个 PluginImporter .meta，否则默认「全平台可用」；
            //   2. 裸 DLL 若其实是 UPM 包产物，换成清单依赖。
            //
            // 第 2 步必须先知道「哪些程序集真的被脚本引用」：删掉被引用的 DLL 会让
            // 工程里所有 m_Script 变成 Missing (Mono Script)，而包内程序集的 GUID
            // 是 Unity 导入时随机生成的，导出阶段无从得知、无法回填。
            string assetsDirectory = System.IO.Path.Combine(outputPath, "ExportedProject", "Assets");
            HashSet<string> referenced = NativePluginsPostProcess.CollectReferencedAssemblies(gameData);
            Console.WriteLine($"[Plugins] {referenced.Count} assembly(ies) are referenced by scripts");

            (int metas, int removed, int kept) = NativePluginsPostProcess.Run(
                assetsDirectory, PendingPackages, referenced, AppState.RemovePackagedDlls);
            Console.WriteLine(
                $"[Plugins] wrote {metas} importer .meta file(s), " +
                $"replaced {removed} packaged DLL(s), kept {kept} referenced DLL(s)");

            // 兜底改写：导出阶段 MetaPtrPatch 已经在写 YAML 时改过引用，
            // 但版本筛选失败、或用户手改过工程时可能留下漏网的裸 DLL 引用。
            // 只扫描真的被删掉的程序集，不碰其它 DLL。
            RewriteScriptReferences(assetsDirectory, referenced);

            if (PendingPackages.Count > 0)
            {
                Console.WriteLine($"[Plugins] packages required by removed DLLs: {string.Join(", ", PendingPackages.Keys)}");
                PackageManifestPatch.AddDependencies(outputPath, PendingPackages);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to copy native plugins: {ex}");
        }
    }

    /// <summary>
    /// 兜底：把工程里指向**已删除**的裸 DLL 的脚本引用，改写成包内 <c>.cs.meta</c> 引用。
    ///
    /// <para>正常路径上，<see cref="MetaPtrPatch"/> 在写 YAML 时就改好了。这里处理
    /// 「已经写进磁盘、但引用的 GUID 指向不存在资产」的漏网条目——只扫被删掉的程序集，
    /// 所以不会误伤还在原地的 DLL 引用。</para>
    /// </summary>
    private static void RewriteScriptReferences(string assetsDirectory, HashSet<string> referencedAssemblies)
    {
        HashSet<string> removed = new(StringComparer.OrdinalIgnoreCase);
        foreach (string packageName in PendingPackages.Keys)
        {
            UnityPackage? package = UnityPackageCatalog.AllPackages
                .FirstOrDefault(p => string.Equals(p.name, packageName, StringComparison.OrdinalIgnoreCase));
            if (package is null)
            {
                continue;
            }
            foreach (AsmDef asmdef in package.asmdefs)
            {
                // 只改写「包里确实有源码」的程序集——没有类型的程序集改不了任何东西。
                if (referencedAssemblies.Contains(asmdef.name) && asmdef.types.Count > 0)
                {
                    removed.Add(asmdef.name);
                }
            }
        }

        if (removed.Count == 0)
        {
            return;
        }

        ScriptReferenceRewriter.Result result = ScriptReferenceRewriter.Run(assetsDirectory, removed);
        Console.WriteLine(
            $"[Plugins] script reference scan: {result.ReferencesRewritten} rewritten, " +
            $"{result.ReferencesUnresolved} unresolved, {result.FilesChanged}/{result.FilesScanned} file(s) touched");
    }

    private static void CopyDirectory(string source, string target)
    {
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
