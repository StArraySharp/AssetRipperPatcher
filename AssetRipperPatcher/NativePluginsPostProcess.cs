using System.Text;
using System.Text.RegularExpressions;
using AssetRipper.Import.Structure.Assembly;
using AssetRipper.Processing;
using AssetRipper.SourceGenerated.Classes.ClassID_115;

namespace AssetRipperPatcher;

/// <summary>
/// 导出后处理：给 <c>Assets/Plugins/</c> 下的原生库补 <c>.meta</c>（PluginImporter），
/// 并把「其实是 UPM 包产物」的 DLL 换成清单里的包依赖。
///
/// <para><b>为什么需要。</b>
/// 直接把 <c>Game_Data/Plugins/</c> 拷进 <c>Assets/Plugins/</c> 有两个后果：</para>
/// <list type="number">
/// <item>没有 <c>.meta</c>，Unity 会用 PluginImporter 默认值——<b>全平台全架构都勾上</b>。
/// 一个 Windows x86_64 的 <c>.dll</c> 因此被当成「任何平台可用」，
/// Android/iOS 构建时就会链接错架构甚至直接失败。</item>
/// <item>裸 DLL 抢占了本该由包提供的程序集，一旦装上同名包就报 CS0433。</item>
/// </list>
/// </summary>
internal static partial class NativePluginsPostProcess
{
    /// <summary>按源目录名推断 Unity 平台设置。键忽略大小写。</summary>
    private static readonly Dictionary<string, PlatformRule> PlatformByFolder =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Standalone
            ["x86"] = new("Standalone", "Win", "x86"),
            ["x86_64"] = new("Standalone", "Win64", "x86_64"),
            ["x64"] = new("Standalone", "Win64", "x86_64"),
            ["Win"] = new("Standalone", "Win", "AnyCPU"),
            ["Win64"] = new("Standalone", "Win64", "AnyCPU"),
            ["Mac"] = new("Standalone", "OSXUniversal", "AnyCPU"),
            ["MacOS"] = new("Standalone", "OSXUniversal", "AnyCPU"),
            ["Linux"] = new("Standalone", "Linux64", "x86_64"),
            ["Linux64"] = new("Standalone", "Linux64", "x86_64"),
            // Mobile
            ["Android"] = new("Android", "", "AnyCPU"),
            ["iOS"] = new("iPhone", "", "AnyCPU"),
            // 编辑器专用
            ["Editor"] = new("Editor", "Editor", "AnyCPU"),
        };

    private readonly record struct PlatformRule(string Platform, string PlatformSub, string Cpu);

    /// <summary>Unity 认为「这是一个插件」的扩展名。</summary>
    private static readonly HashSet<string> PluginExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".dll", ".so", ".bundle", ".dylib", ".a", ".aar", ".jar" };

    /// <summary>
    /// 对 <paramref name="assetsDirectory"/> 下的 Plugins 目录做后处理。
    /// </summary>
    /// <param name="assetsDirectory"><c>ExportedProject/Assets</c>。</param>
    /// <param name="packages">收集到的「需要的包」，由调用方写进 manifest。</param>
    /// <param name="referencedAssemblies">
    /// 被工程真实引用的程序集名（由 <see cref="CollectReferencedAssemblies"/> 得到）。
    ///
    /// <para><b>这是删 DLL 的安全前提。</b>
    /// 命中映射表只说明「这个 DLL 是包的产物」，不代表「删掉它安全」。
    /// 只有当该程序集**没有被任何脚本引用**时才删——纯类库（Newtonsoft.Json、
    /// UniTask、DOTween 运行时）删了无事，带自定义 <c>MonoBehaviour</c> 的删了就是满屏
    /// <c>Missing (Mono Script)</c>。</para>
    ///
    /// <para>为 null 表示调用方没提供信息，此时一律**不删**（保守）。</para>
    /// </param>
    /// <param name="removePackagedDlls">
    /// 强制删除命中映射表的 DLL，**即使它被脚本引用**。
    /// 只在你确认工程不依赖那些脚本时才用。
    /// </param>
    /// <returns>补的 .meta 数量、删掉的 DLL 数量、因被引用而保留的 DLL 数量。</returns>
    public static (int MetasWritten, int DllsRemoved, int DllsKept) Run(
        string assetsDirectory,
        IDictionary<string, string> packages,
        ISet<string>? referencedAssemblies = null,
        bool removePackagedDlls = false)
    {
        string pluginsRoot = Path.Combine(assetsDirectory, "Plugins");
        if (!Directory.Exists(pluginsRoot))
        {
            return (0, 0, 0);
        }

        int metas = 0;
        int removed = 0;
        int kept = 0;

        // 目录 → 平台规则。Plugins 根下的散文件用「Any」，子目录按名字查表。
        foreach (string file in Directory.EnumerateFiles(pluginsRoot, "*", SearchOption.AllDirectories))
        {
            string extension = Path.GetExtension(file);
            if (!PluginExtensions.Contains(extension))
            {
                continue;
            }

            // 裸 DLL 若其实是某个包的产物：登记包依赖；是否删 DLL 要看有没有脚本引用它。
            if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                            && UnityPackageCatalog.TryResolveAssembly(file, out AsmDef? asmdef)
                            && UnityPackageCatalog.TryResolvePackage(file, out UnityPackage? firstProvider))
            {
                // 同一个程序集可能由多个包提供（Unity.TextMeshPro：独立包 or UGUI 2.0+ 内置）。
                // 挑错就会同时装上两个包 → CS0433。优先复用本流程已选中的包，
                // 其次挑「内置了别的包」的聚合包。
                UnityPackage package = UnityPackageCatalog.ChooseProvider(
                        file, new HashSet<string>(packages.Keys, StringComparer.OrdinalIgnoreCase), default)
                    ?? firstProvider!;

                if (!packages.ContainsKey(package.name))
                {
                    // 注册表里只有实验版时换成可用的稳定版（UGUI 3.0.0-exp.4 → 2.0.0），
                    // 否则清单里写的版本根本装不上。
                    packages[package.name] = UnityPackageCatalog.GetManifestVersion(package.version);
                }

                bool isReferenced = referencedAssemblies is null
                    || referencedAssemblies.Contains(asmdef.name);

                if (isReferenced && !removePackagedDlls)
                {
                    // 保留：删了会让指向它的 m_Script 悬空。
                    Console.WriteLine(
                        $"[Plugins] 保留 '{Path.GetFileName(file)}'（包 {package.name}）：" +
                        $"工程里有脚本引用它，删除会导致 Missing (Mono Script)");
                    kept++;
                }
                else
                {
                    Console.WriteLine($"[Plugins] 删除 '{Path.GetFileName(file)}' -> 包 {package.name}" +
                        (isReferenced ? "（强制：--remove-packaged-dlls）" : "（无脚本引用，安全）"));
                    File.Delete(file);
                    // 同目录的 .pdb / .xml 也一并清掉，否则留下孤儿文件。
                    foreach (string sibling in new[] { ".pdb", ".xml" })
                    {
                        string path = Path.ChangeExtension(file, sibling);
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                    }
                    removed++;
                    continue;
                }
            }

            // 其余：补 .meta（已有就跳过，尊重 AssetRipper 自己写的）。
            string metaPath = file + ".meta";
            if (File.Exists(metaPath))
            {
                continue;
            }
            PlatformRule rule = ResolvePlatform(pluginsRoot, file);
            File.WriteAllText(metaPath, BuildPluginMeta(file, rule), new UTF8Encoding(false));
            metas++;
        }

        // 收尾：剔除「被清单里其它包内置」的条目。
        // 典型场景是 UGUI 2.0+ 已自带 TextMeshPro，再写 com.unity.textmeshpro
        // 会让两个包各带一份 Unity.TextMeshPro.dll → CS0433。
        foreach (string name in UnityPackageCatalog.RemoveSuperseded(packages))
        {
            Console.WriteLine($"[Plugins] {name} 已被清单里的其它包内置，跳过");
        }

        return (metas, removed, kept);
    }

    /// <summary>
    /// 判断某个程序集里是否有脚本**真的被工程引用**。
    ///
    /// <para><b>为什么这是删 DLL 的正确前提。</b>
    /// AssetRipper 给裸 DLL 里的脚本写引用
    /// <c>{fileID: MD4('s'+0+0+0+ns+name)[0..4], guid: MD5(assemblyName)}</c>。
    /// 删掉 DLL 后这些引用就悬空了——但**只有真的存在这种引用时才会悬空**。</para>
    ///
    /// <para>纯类库（Newtonsoft.Json、UniTask、DOTween 的运行时部分）里的类型
    /// 不是 <c>MonoBehaviour</c>，不会被场景/prefab 以 <c>m_Script</c> 引用，
    /// 删掉完全安全。反过来，带自定义 <c>MonoBehaviour</c> 的程序集删掉就会满屏
    /// <c>Missing (Mono Script)</c>。</para>
    ///
    /// <para>判断依据是 <see cref="IMonoScript"/> 是否存在：AssetRipper 只为
    /// 「被引用到的脚本」创建 <c>MonoScript</c> 资产（见 <c>ScriptExporter</c> 的
    /// <c>TryCreateCollection</c>），所以资产集合里有没有某个程序集的
    /// <c>IMonoScript</c>，就是「有没有脚本引用它」的权威答案。</para>
    /// </summary>
    /// <param name="gameData">已加载的游戏数据。</param>
    /// <returns>被引用的程序集名集合（不含扩展名，忽略大小写）。</returns>
    public static HashSet<string> CollectReferencedAssemblies(GameData gameData)
    {
        HashSet<string> referenced = new(StringComparer.OrdinalIgnoreCase);
        foreach (IMonoScript script in gameData.GameBundle.FetchAssets().OfType<IMonoScript>())
        {
            string assembly = script.GetAssemblyNameFixed();
            if (!string.IsNullOrEmpty(assembly))
            {
                referenced.Add(assembly);
            }
        }
        return referenced;
    }

    /// <summary>
    /// 由「文件相对 Plugins 的目录」推断平台规则。
    /// <c>Plugins/x86_64/foo.dll</c> → Standalone/Win64/x86_64；
    /// <c>Plugins/Android/</c> 或 <c>Plugins/Android/libs/arm64-v8a/</c> → Android。
    /// 直接放在 <c>Plugins/</c> 根下的按 Any 处理（与 Unity 默认一致，但只勾当前平台）。
    /// </summary>
    private static PlatformRule ResolvePlatform(string pluginsRoot, string file)
    {
        string relative = Path.GetRelativePath(pluginsRoot, file);
        string[] parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (PlatformByFolder.TryGetValue(parts[i], out PlatformRule rule))
            {
                return rule;
            }
        }
        // 兜底：按当前操作系统对应的 Standalone 目标。比「全平台」安全得多。
        return OperatingSystem.IsWindows()
            ? new PlatformRule("Standalone", "Win64", "x86_64")
            : OperatingSystem.IsMacOS()
                ? new PlatformRule("Standalone", "OSXUniversal", "AnyCPU")
                : new PlatformRule("Standalone", "Linux64", "x86_64");
    }

    /// <summary>
    /// 生成 PluginImporter 的 .meta。
    /// 结构照 Unity 自己写的（<c>fileFormatVersion: 2</c> + <c>PluginImporter</c> 段），
    /// GUID 由文件相对路径确定性生成，保证重复导出得到同一份工程。
    /// </summary>
    private static string BuildPluginMeta(string file, PlatformRule rule)
    {
        string guid = DeterministicGuid(file);
        StringBuilder sb = new();
        sb.AppendLine("fileFormatVersion: 2");
        sb.AppendLine($"guid: {guid}");
        sb.AppendLine("PluginImporter:");
        sb.AppendLine("  externalObjects: {}");
        sb.AppendLine("  serializedVersion: 2");
        sb.AppendLine("  iconMap: {}");
        sb.AppendLine("  executionOrder: {}");
        sb.AppendLine("  defineConstraints: []");
        sb.AppendLine("  isPreloaded: 0");
        sb.AppendLine("  isOverridable: 0");
        sb.AppendLine("  isExplicitlyReferenced: 0");
        sb.AppendLine("  validateReferences: 1");
        sb.AppendLine("  platformData:");
        // 先关掉「Any」，再打开具体平台——Unity 的 .meta 就是这个顺序。
        sb.AppendLine("  - first:");
        sb.AppendLine("      : Any");
        sb.AppendLine("    second:");
        sb.AppendLine("      enabled: 0");
        sb.AppendLine("      settings: {}");
        if (rule.PlatformSub.Length > 0)
        {
            // 形如 `- first: {Standalone: Win64}`：一个键值对，不是两行。
            sb.AppendLine("  - first:");
            sb.AppendLine($"      {rule.Platform}: {rule.PlatformSub}");
            sb.AppendLine("    second:");
            sb.AppendLine("      enabled: 1");
            sb.AppendLine("      settings:");
            if (rule.Cpu.Length > 0 && !rule.Cpu.Equals("AnyCPU", StringComparison.Ordinal))
            {
                sb.AppendLine($"        CPU: {rule.Cpu}");
            }
        }
        else
        {
            sb.AppendLine("  - first:");
            sb.AppendLine($"      {rule.Platform}: ");
            sb.AppendLine("    second:");
            sb.AppendLine("      enabled: 1");
            sb.AppendLine("      settings: {}");
        }
        sb.AppendLine("  userData:");
        sb.AppendLine("  assetBundleName:");
        sb.AppendLine("  assetBundleVariant:");
        return sb.ToString();
    }

    /// <summary>
    /// 由路径确定性生成 GUID（Unity GUID 是 32 位小写十六进制）。
    /// 用 MD5 而不是 <see cref="Guid.NewGuid"/>：同一路径重复导出必须得到同一份工程，
    /// 否则每次导出都会在版本控制里产生大量「meta 变了」的伪改动。
    /// </summary>
    private static string DeterministicGuid(string path)
    {
        byte[] hash = System.Security.Cryptography.MD5.HashData(
            Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
