using System.Diagnostics.CodeAnalysis;
using System.Text;
using AssetRipper.Export.UnityProjects.Scripts;
using AssetRipper.Primitives;

namespace AssetRipperPatcher;

/// <summary>
/// Unity 官方包目录的查询层；数据在 <c>UnityPackageCatalog.Generated.cs</c>。
///
/// <para><b>它取代了什么。</b>
/// 老实现是 <c>GuidMapping</c>：两张硬编码的 <c>fileID → GUID</c> 表
/// （TMP 13 条、UGUI 约 80 条）。那张表有三个硬伤：
/// 只覆盖两个包、fileID 要人工算、GUID 抄错一个就静默失效。</para>
///
/// <para><b>现在怎么查。</b>
/// 包里每个 <c>.cs.meta</c> 的 GUID 都是打包时固定下来的，所以
/// 「程序集 + 命名空间 + 类型名 → 包内 GUID + 所属包」可以由工具直接采集。
/// fileID 不用存——它是 Unity 的确定性算法，查的时候现算，
/// 直接调用 AssetRipper 自己的 <see cref="ScriptHashing.CalculateScriptFileID(string, string)"/>。</para>
/// </summary>
public static partial class UnityPackageCatalog
{
    /// <summary>Unity 里 MonoScript 资产的固定 fileID（<c>ExportIdHandler.GetMainExportID(115)</c>）。</summary>
    public const long MonoScriptFileId = 11500000L;

    /// <summary>
    /// 「独立包被并入另一个包」的已知事实。
    ///
    /// <para><b>为什么要硬编码。</b>
    /// 采集数据看得见「两个包提供同一个程序集」，但看不见「装哪个就够」——
    /// UGUI 3.x 的包体里确实有 <c>Unity.TextMeshPro</c>，但那只是 TMP 的一部分；
    /// 真实工程里 Unity 2023.2+ 靠 UGUI <b>2.0+</b> 自带完整的 TextMeshPro。
    /// 光靠「程序集重叠」去猜会选错，所以明确写下来，并带上生效的版本门槛。</para>
    ///
    /// <para>装 UGUI 2.0+ 时**不应该**再往清单里写 <c>com.unity.textmeshpro</c>，
    /// 否则两个包各带一份 <c>Unity.TextMeshPro.dll</c>，报 CS0433
    /// 「类型在多个程序集中存在」。</para>
    /// </summary>
    private static readonly MergeRule[] MergeRules =
    [
        // 被并入的包,      宿主包,           宿主从哪个版本开始自带
        new("com.unity.textmeshpro", "com.unity.ugui", "2.0.0"),
        ];

    private readonly record struct MergeRule(
        string SupersededPackage,
        string HostPackage,
        string HostMinimumVersion);

    /// <summary>
    /// 装了 <paramref name="hostName"/>@<paramref name="hostVersion"/> 之后，
    /// 是否就不该再装 <paramref name="supersededPackage"/>。
    /// </summary>
    /// <remarks>
    /// 版本门槛只在宿主版本解析得出来时生效；解析不出就保守地**不**合并
    /// （多装一个包是编译错误，少装一个包是功能缺失，前者更容易发现）。
    /// </remarks>
    public static bool Supersedes(string hostName, string hostVersion, string supersededPackage)
    {
        if (string.Equals(hostName, supersededPackage, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (!TryParsePackageVersion(hostVersion, out Version version))
        {
            return false;
        }

        foreach (MergeRule rule in MergeRules)
        {
            if (string.Equals(rule.SupersededPackage, supersededPackage, StringComparison.OrdinalIgnoreCase)
                && string.Equals(rule.HostPackage, hostName, StringComparison.OrdinalIgnoreCase)
                && version >= ParsePackageVersionOrZero(rule.HostMinimumVersion))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>两个已知包之间的吞并判断（版本取自目录数据）。</summary>
    public static bool Supersedes(UnityPackage host, UnityPackage superseded) =>
        Supersedes(host.name, host.version, superseded.name);

    /// <summary>
    /// 从 <paramref name="packages"/> 里剔除被其它成员吞并的包。
    ///
    /// <para>写 manifest 之前过一遍：装了 <c>com.unity.ugui</c> 2.0+ 就不要再写
    /// <c>com.unity.textmeshpro</c>。</para>
    /// </summary>
    /// <returns>被剔除的包名（已从 <paramref name="packages"/> 移除）。</returns>
    public static IReadOnlyList<string> RemoveSuperseded(IDictionary<string, string> packages)
    {
        List<string> removed = new();
        foreach ((string name, string _) in packages.ToList())
        {
            foreach ((string hostName, string hostVersion) in packages.ToList())
            {
                if (Supersedes(hostName, hostVersion, name))
                {
                    packages.Remove(name);
                    removed.Add(name);
                    break;
                }
            }
        }
        return removed;
    }

    /// <summary>解析 UPM 版本号（可能带 <c>-exp.4</c> 之类的预发布后缀）。</summary>
    private static bool TryParsePackageVersion(string text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }
        int cut = text.IndexOfAny(['-', '+']);
        string core = cut >= 0 ? text[..cut] : text;
        return Version.TryParse(core, out version!);
    }

    /// <summary>
    /// 把「注册表里只有实验版」的包版本换成可用的稳定版。
    ///
    /// <para><b>为什么需要。</b>
    /// Unity 注册表有时只公开某个包的预发布版本——<c>com.unity.ugui</c> 就只列了
    /// <c>3.0.0-exp.4</c>，而它在真实工程里是**编辑器内置**模块，从注册表根本下载不到
    /// （实测 <c>2.0.0</c> / <c>1.0.0</c> 在公共源上都是 404）。
    /// 直接把 <c>3.0.0-exp.4</c> 写进 <c>manifest.json</c> 会让工程装不上这个包。</para>
    ///
    /// <para><b>规则。</b>
    /// 版本形如 <c>x.0.0-预发布后缀</c> 且 <c>x ≥ 2</c> 时，降到 <c>(x-1).0.0</c>；
    /// 结果是 <c>1.0.0</c> 时不动——<c>1.0.0</c> 是 Unity 内置包的通用占位版本。
    /// 非预发布版本原样返回。</para>
    ///
    /// <para>例：<c>3.0.0-exp.4</c> → <c>2.0.0</c>；<c>2.0.0-pre.1</c> → <c>1.0.0</c>（不动）；
    /// <c>3.0.9</c> → <c>3.0.9</c>（不动）。</para>
    /// </summary>
    /// <param name="versionText"><c>package.json</c> 里的版本号。</param>
    /// <param name="stable">替换后的稳定版；没有替换时等于 <paramref name="versionText"/>。</param>
    /// <returns>是否发生了替换。</returns>
    public static bool TryGetStableVersion(string versionText, out string stable)
    {
        stable = versionText;
        if (string.IsNullOrEmpty(versionText))
        {
            return false;
        }

        int dash = versionText.IndexOf('-', StringComparison.Ordinal);
        if (dash <= 0)
        {
            return false; // 没有预发布后缀
        }

        // 只处理 x.0.0-xxx：主版本 ≥ 2 才有「上一个稳定主版本」可用。
        string core = versionText[..dash];
        if (!Version.TryParse(core, out Version? parsed)
            || parsed.Major < 2
            || parsed.Minor != 0
            || parsed.Build != 0)
        {
            return false;
        }

        stable = $"{parsed.Major - 1}.0.0";
        return true;
    }

    /// <summary>
    /// 把包版本规整成「能写进 <c>manifest.json</c> 的版本」：
    /// 预发布版换成稳定版，其余原样。
    /// </summary>
    public static string GetManifestVersion(string packageVersion) =>
        TryGetStableVersion(packageVersion, out string stable) ? stable : packageVersion;

    private static Version ParsePackageVersionOrZero(string text) =>
        TryParsePackageVersion(text, out Version version) ? version : new Version(0, 0);

    /// <summary>包内一个可序列化类型与它所在程序集/包。</summary>
    /// <param name="AssemblyName">程序集名（不含 <c>.dll</c>）。</param>
    /// <param name="Namespace">命名空间；空串表示全局命名空间。</param>
    /// <param name="TypeName">类型名；嵌套类型用 <c>Outer+Inner</c>。</param>
    /// <param name="Guid">包内该 <c>.cs.meta</c> 的 GUID。</param>
    /// <param name="Package">来源 UPM 包。</param>
    public readonly record struct ScriptEntry(
        string AssemblyName,
        string Namespace,
        string TypeName,
        UnityGuid Guid,
        UnityPackage Package);

    /// <summary>按 <see cref="AsmDef.name"/> 建的索引（忽略大小写）。</summary>
    private static readonly Lazy<Dictionary<string, (AsmDef AsmDef, UnityPackage Package)>> Assemblies = new(BuildAssemblies);

    /// <summary>按 (程序集, 命名空间, 类型名) 建的类型索引（忽略大小写）。</summary>
    private static readonly Lazy<Dictionary<string, ScriptEntry>> Types = new(BuildTypes);

    private static Dictionary<string, (AsmDef, UnityPackage)> BuildAssemblies()
    {
        Dictionary<string, (AsmDef, UnityPackage)> index = new(StringComparer.OrdinalIgnoreCase);
        foreach (UnityPackage package in Packages)
        {
            foreach (AsmDef asmdef in package.asmdefs)
            {
                if (index.TryGetValue(asmdef.name, out (AsmDef AsmDef, UnityPackage Package) existing))
                {
                    // 同一个程序集可能出现在多个包里（com.unity.ugui 3.x 内置了
                    // Unity.TextMeshPro，与 com.unity.textmeshpro 并存）。
                    // 两边的类型列表可能互补，合并而不是丢弃。
                    //
                    // 合并结果放在**新的** AsmDef 实例里，不动 Packages 里的原对象：
                    // 包自己的 guids / asmdefs 必须保持自洽（见 Package_guids_are_the_flattened_type_guids）。
                    AsmDef merged = new()
                    {
                        name = existing.AsmDef.name,
                        guid = existing.AsmDef.guid.Length > 0 ? existing.AsmDef.guid : asmdef.guid,
                    };
                    merged.types.AddRange(existing.AsmDef.types);
                    foreach (ScriptType type in asmdef.types)
                    {
                        if (!merged.types.Any(t =>
                            string.Equals(t.name, type.name, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(t.@namespace, type.@namespace, StringComparison.OrdinalIgnoreCase)))
                        {
                            merged.types.Add(type);
                        }
                    }
                    index[asmdef.name] = (merged, existing.Package);
                    continue;
                }
                index.Add(asmdef.name, (asmdef, package));
            }
        }
        return index;
    }

    private static Dictionary<string, ScriptEntry> BuildTypes()
    {
        Dictionary<string, ScriptEntry> index = new(StringComparer.OrdinalIgnoreCase);
        // 基于合并后的视图（Assemblies.Value），而不是直接遍历 Packages：
        // 同一程序集在多个包里出现时，类型列表已被合并过。
        foreach ((string name, (AsmDef asmdef, UnityPackage package)) in Assemblies.Value)
        {
            foreach (ScriptType type in asmdef.types)
            {
                index.TryAdd(Key(name, type.@namespace, type.name), new ScriptEntry(
                    name, type.@namespace, type.name, type.guid, package));
            }
        }
        return index;
    }

    private static string Key(string assemblyName, string @namespace, string typeName) =>
        $"{assemblyName}\u0000{@namespace}\u0000{typeName}";

    /// <summary>去掉 <c>.dll</c> / 目录前缀。</summary>
    internal static string NormalizeAssemblyName(string value)
    {
        string name = Path.GetFileName(value);
        return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    /// <summary>包提供的程序集总数（诊断/测试用）。</summary>
    public static int AssemblyCount => Assemblies.Value.Count;

    /// <summary>可序列化类型总数（诊断/测试用）。</summary>
    public static int TypeCount => Types.Value.Count;

    /// <summary>
    /// 某个程序集由**哪些**包提供（通常是 1 个，但可能多个）。
    ///
    /// <para><b>为什么是复数。</b>
    /// 程序集会在包之间搬家：<c>Unity.TextMeshPro</c> 同时由
    /// <c>com.unity.textmeshpro</c>（独立包）和 <c>com.unity.ugui</c>（2.0 起内置）提供。
    /// 决定「该往清单里加哪个包」时必须看到全部候选，否则会同时装上两个包，
    /// 报 CS0433「类型在多个程序集中存在」。</para>
    /// </summary>
    public static IReadOnlyList<UnityPackage> GetProviders(string assemblyNameOrPath)
    {
        if (string.IsNullOrEmpty(assemblyNameOrPath))
        {
            return [];
        }
        string name = NormalizeAssemblyName(assemblyNameOrPath);
        List<UnityPackage> providers = new();
        foreach (UnityPackage package in Packages)
        {
            if (package.asmdefs.Any(a => string.Equals(a.name, name, StringComparison.OrdinalIgnoreCase))
                && !providers.Contains(package))
            {
                providers.Add(package);
            }
        }
        return providers;
    }

    /// <summary>
    /// 从若干候选包里挑出「该写进 manifest」的那一个。
    ///
    /// <para><b>规则（按优先级）。</b></para>
    /// <list type="number">
    /// <item>偏好调用方已选中的包（同一个导出流程里多次调用要保持一致）。</item>
    /// <item>偏好「吞并了另一个候选」的包——UGUI 2.0+ 自带 TextMeshPro，
    /// 装它就够了；再装 <c>com.unity.textmeshpro</c> 会重复定义同一个程序集。</item>
    /// <item>剩下的按最低 Unity 版本升序：既保证装得上，又挑最通用（最老）的那个。</item>
    /// </list>
    /// </summary>
    /// <param name="assemblyNameOrPath">程序集名或 DLL 路径。</param>
    /// <param name="preferred">已选中的包名集合；命中者优先。</param>
    /// <param name="version">工程 Unity 版本；用于优先选「装得上」的包。</param>
    public static UnityPackage? ChooseProvider(
        string assemblyNameOrPath,
        IReadOnlySet<string>? preferred,
        UnityVersion version)
    {
        IReadOnlyList<UnityPackage> providers = GetProviders(assemblyNameOrPath);
        if (providers.Count == 0)
        {
            return null;
        }
        if (providers.Count == 1)
        {
            return providers[0];
        }

        return providers
            .OrderByDescending(p => preferred is not null && preferred.Contains(p.name))
            // 聚合包优先：它按 MergeRules 吞并了候选里的另一个包，装它就少一份冲突风险。
            .ThenByDescending(p => providers.Any(other => Supersedes(p, other)))
            // 装得上的优先。
            .ThenByDescending(p => IsCompatible(p, version))
            // 最低 Unity 版本更老 = 覆盖范围更广。
            .ThenBy(ParseUnityOrMax)
            .First();
    }

    private static UnityVersion ParseUnityOrMax(UnityPackage package) =>
        UnityVersion.TryParse(package.unity, out UnityVersion parsed, out _) ? parsed : UnityVersion.MaxVersion;

    /// <summary>某个程序集由哪个包提供。参数可以带 <c>.dll</c> 与目录。</summary>
    public static bool TryResolvePackage(string assemblyNameOrPath, [NotNullWhen(true)] out UnityPackage? package)
    {
        package = null;
        if (string.IsNullOrEmpty(assemblyNameOrPath))
        {
            return false;
        }
        if (Assemblies.Value.TryGetValue(NormalizeAssemblyName(assemblyNameOrPath), out (AsmDef AsmDef, UnityPackage Package) found))
        {
            package = found.Package;
            return true;
        }
        return false;
    }

    /// <summary>某个程序集对应的 asmdef（含它带来的所有类型 GUID）。</summary>
    public static bool TryResolveAssembly(string assemblyNameOrPath, [NotNullWhen(true)] out AsmDef? asmdef)
    {
        asmdef = null;
        if (string.IsNullOrEmpty(assemblyNameOrPath))
        {
            return false;
        }
        if (Assemblies.Value.TryGetValue(NormalizeAssemblyName(assemblyNameOrPath), out (AsmDef AsmDef, UnityPackage Package) found))
        {
            asmdef = found.AsmDef;
            return true;
        }
        return false;
    }

    /// <summary>某个程序集里全部可序列化类型。</summary>
    public static IEnumerable<ScriptEntry> ForAssembly(string assemblyNameOrPath)
    {
        string assembly = NormalizeAssemblyName(assemblyNameOrPath);
        if (!Assemblies.Value.TryGetValue(assembly, out (AsmDef AsmDef, UnityPackage Package) found))
        {
            yield break;
        }
        foreach (ScriptType type in found.AsmDef.types)
        {
            yield return new ScriptEntry(assembly, type.@namespace, type.name, type.guid, found.Package);
        }
    }

    /// <summary>按 (程序集, 命名空间, 类型名) 查包内 GUID。</summary>
    /// <remarks><paramref name="typeName"/> 里的 <c>.</c> 会当成 <c>+</c> 再试一次，兼容两种嵌套写法。</remarks>
    public static bool TryResolveType(
        string assemblyName,
        string? @namespace,
        string? typeName,
        out ScriptEntry entry)
    {
        entry = default;
        if (string.IsNullOrEmpty(assemblyName) || string.IsNullOrEmpty(typeName))
        {
            return false;
        }

        string assembly = NormalizeAssemblyName(assemblyName);
        string ns = @namespace ?? string.Empty;

        if (Types.Value.TryGetValue(Key(assembly, ns, typeName), out entry))
        {
            return true;
        }
        if (typeName.Contains('.', StringComparison.Ordinal)
            && Types.Value.TryGetValue(Key(assembly, ns, typeName.Replace('.', '+')), out entry))
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// 由「程序集名 + 命名空间 + 类型名」算出 Unity 的脚本 fileID。
    ///
    /// <para>直接委托给 AssetRipper 的 <see cref="ScriptHashing.CalculateScriptFileID(string, string)"/>：
    /// 导出时写引用用它，这里反查用它，两边永远一致。自己再写一遍 MD4 只会引入偏差。</para>
    /// </summary>
    public static long CalculateScriptFileId(string @namespace, string typeName) =>
        ScriptHashing.CalculateScriptFileID(@namespace, typeName);

    /// <summary>
    /// <c>MD5(程序集名)</c>——AssetRipper 给裸 DLL 脚本写的 GUID，
    /// 也是反查「这条引用属于哪个程序集」的钥匙。
    /// </summary>
    public static UnityGuid CalculateAssemblyGuid(string assemblyName) =>
        ScriptHashing.CalculateAssemblyGuid(assemblyName);

    /// <summary>
    /// 老 <c>GuidMapping.TryGet</c> 的替代品：由「脚本 fileID + 当前 Unity 版本」找包内 GUID。
    ///
    /// <para><b>为什么要版本。</b>
    /// 同一个类型名在不同包里的 GUID 可能不同（UGUI 2.x 与 3.x 是两个包）。
    /// fileID 只由命名空间 + 类型名决定，不含程序集，
    /// 所以匹配候选时要按 Unity 版本过滤：只接受「最低 Unity 版本 ≤ 当前版本」的包。</para>
    ///
    /// <para><b>为什么要回退。</b>
    /// 有些包只在注册表里留了 exp 版（<c>com.unity.ugui</c> 就是这样，且它的
    /// <c>unity</c> 字段写着 6000.0）。可是它在 Unity 2019~2022 是编辑器内置模块、
    /// 根本不在注册表里，而脚本 GUID 与 3.x 完全一致。所以第一轮按版本筛，
    /// 筛不到就退回「任意版本」——总比改不了引用、留一堆 Missing (Mono Script) 好。</para>
    ///
    /// <para>候选集按 <see cref="UnityPackage.unity"/> 降序尝试——优先匹配到与工程版本
    /// 最接近的包，而不是随便命中一个老版本。</para>
    /// </summary>
    public static bool TryGetScriptGuid(long fileId, UnityVersion version, out UnityGuid guid, out UnityPackage? package)
    {
        IReadOnlyList<ScriptEntry> candidates = CandidatesForFileId(fileId);

        foreach (ScriptEntry entry in candidates)
        {
            if (IsCompatible(entry.Package, version))
            {
                guid = entry.Guid;
                package = entry.Package;
                return true;
            }
        }

        // 回退：没有版本兼容的候选，就用版本号最高的那个。
        foreach (ScriptEntry entry in candidates)
        {
            guid = entry.Guid;
            package = entry.Package;
            return true;
        }

        guid = UnityGuid.Zero;
        package = null;
        return false;
    }

    /// <summary>不带版本约束的查询（诊断/测试用）。</summary>
    public static bool TryGetScriptGuid(long fileId, out UnityGuid guid, out UnityPackage? package)
    {
        foreach (ScriptEntry entry in CandidatesForFileId(fileId))
        {
            guid = entry.Guid;
            package = entry.Package;
            return true;
        }
        guid = UnityGuid.Zero;
        package = null;
        return false;
    }

    /// <summary>不带版本约束的查询（诊断/测试用）。</summary>
    public static bool TryGetScriptGuid(long fileId, out UnityGuid guid) =>
        TryGetScriptGuid(fileId, out guid, out _);

    /// <summary>fileID → 全部候选类型（可能跨多个包/版本），版本高的在前。</summary>
    private static IReadOnlyList<ScriptEntry> CandidatesForFileId(long fileId) =>
        ByFileId.Value.TryGetValue(fileId, out List<ScriptEntry>? list) ? list : Array.Empty<ScriptEntry>();

    /// <summary>fileID → 候选列表。fileID 是确定性算法，建一次表即可。</summary>
    private static readonly Lazy<Dictionary<long, List<ScriptEntry>>> ByFileId = new(BuildByFileId);

    private static Dictionary<long, List<ScriptEntry>> BuildByFileId()
    {
        Dictionary<long, List<ScriptEntry>> index = new();
        foreach (ScriptEntry entry in Types.Value.Values)
        {
            long fileId = CalculateScriptFileId(entry.Namespace, entry.TypeName);
            if (!index.TryGetValue(fileId, out List<ScriptEntry>? list))
            {
                index[fileId] = list = new List<ScriptEntry>(1);
            }
            if (!list.Contains(entry))
            {
                list.Add(entry);
            }
        }
        // 版本高的排前面：TryGetScriptGuid 取第一个匹配项。
        foreach (List<ScriptEntry> list in index.Values)
        {
            list.Sort(static (a, b) => ComparePackageUnity(ParseUnity(b.Package.unity), ParseUnity(a.Package.unity)));
        }
        return index;
    }

    /// <summary>最低 Unity 版本是否 ≤ 工程版本；包没写 unity 字段时视作任意版本。</summary>
    private static bool IsCompatible(UnityPackage package, UnityVersion version) =>
        !UnityVersion.TryParse(package.unity, out UnityVersion minimum, out _) || version >= minimum;

    private static UnityVersion ParseUnity(string text) =>
        UnityVersion.TryParse(text, out UnityVersion parsed, out _) ? parsed : default;

    private static int ComparePackageUnity(UnityVersion a, UnityVersion b)
    {
        bool aEmpty = a == default;
        bool bEmpty = b == default;
        if (aEmpty || bEmpty)
        {
            return (aEmpty ? 0 : 1) - (bEmpty ? 0 : 1);
        }
        return a < b ? -1 : a > b ? 1 : 0;
    }

    /// <summary>全部包（诊断/测试/清单用）。</summary>
    public static IReadOnlyList<UnityPackage> AllPackages => Packages;
}
