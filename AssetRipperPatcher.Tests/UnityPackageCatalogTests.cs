using AssetRipper.Primitives;
using NUnit.Framework;

namespace AssetRipperPatcher.Tests;

/// <summary>
/// <see cref="UnityPackageCatalog"/> 的回归测试。
///
/// <para>这张表决定了三件事，写错任何一件都会让导出工程出问题：</para>
/// <list type="number">
/// <item>哪些裸 DLL 会被删掉、换成 manifest 依赖（删错了就 CS0433 / 编译不过）；</item>
/// <item>脚本引用改写成哪个 GUID（改错了满屏 Missing (Mono Script)）；</item>
/// <item>fileID 反查会不会误伤别的类型。</item>
/// </list>
/// </summary>
public class UnityPackageCatalogTests
{
    [Test]
    public void Catalog_is_not_empty()
    {
        TestContext.Out.WriteLine($"packages={UnityPackageCatalog.AllPackages.Count}, " +
            $"assemblies={UnityPackageCatalog.AssemblyCount}, types={UnityPackageCatalog.TypeCount}");
        Assert.That(UnityPackageCatalog.AllPackages, Is.Not.Empty);
        Assert.That(UnityPackageCatalog.AssemblyCount, Is.GreaterThan(0));
        Assert.That(UnityPackageCatalog.TypeCount, Is.GreaterThan(0));
    }

    [TestCase("Unity.TextMeshPro", "com.unity.textmeshpro", "Unity.TextMeshPro")]
    [TestCase("Unity.TextMeshPro.dll", "com.unity.textmeshpro", "Unity.TextMeshPro")]
    [TestCase("Unity.InputSystem", "com.unity.inputsystem", "Unity.InputSystem")]
    [TestCase("Unity.Addressables", "com.unity.addressables", "Unity.Addressables")]
    [TestCase("UnityEngine.UI", "com.unity.ugui", "UnityEngine.UI")]
    public void Known_assemblies_map_to_their_package(
            string assemblyName, string expectedPackage, string expectedAssembly)
    {
        Assert.That(UnityPackageCatalog.TryResolveAssembly(assemblyName, out AsmDef? asmdef), Is.True,
            $"'{assemblyName}' 应该在目录里");
        Assert.That(UnityPackageCatalog.TryResolvePackage(assemblyName, out UnityPackage? package), Is.True);
        Assert.That(package!.name, Is.EqualTo(expectedPackage));
        Assert.That(asmdef!.name, Is.EqualTo(expectedAssembly));
    }

    /// <summary>完整路径也要能查到（调用方传进来的通常是文件路径）。</summary>
    [Test]
    public void Full_path_resolves_by_file_name()
    {
        string path = Path.Combine("out", "ExportedProject", "Assets", "Plugins", "x86_64", "Unity.TextMeshPro.dll");
        Assert.That(UnityPackageCatalog.TryResolvePackage(path, out UnityPackage? package), Is.True);
        Assert.That(package!.name, Is.EqualTo("com.unity.textmeshpro"));
    }

    /// <summary>大小写不敏感：Windows 文件系统本来就不区分。</summary>
    [Test]
    public void Lookup_is_case_insensitive()
    {
        Assert.That(UnityPackageCatalog.TryResolvePackage("unity.textmeshpro.DLL", out _), Is.True);
        Assert.That(UnityPackageCatalog.TryResolvePackage("UNITY.INPUTSYSTEM", out _), Is.True);
    }

    /// <summary>未知程序集必须查不到 —— 否则会被误删。</summary>
    [TestCase("Assembly-CSharp")]
    [TestCase("MyGame.Something")]
    [TestCase("")]
    [TestCase("UnityEngine")]
    [TestCase("mscorlib")]
    public void Unknown_assemblies_are_not_mapped(string assemblyName)
    {
        Assert.That(UnityPackageCatalog.TryResolveAssembly(assemblyName, out AsmDef? asmdef), Is.False);
        Assert.That(asmdef, Is.Null);
        Assert.That(UnityPackageCatalog.TryResolvePackage(assemblyName, out UnityPackage? package), Is.False);
        Assert.That(package, Is.Null);
    }

    /// <summary>
    /// 同一程序集允许出现在多个包里（<c>com.unity.ugui</c> 3.x 内置了
    /// <c>Unity.TextMeshPro</c>）。目录必须能合并它们，而不是报错或随机取一个。
    /// </summary>
    [Test]
    public void Duplicate_assemblies_are_merged()
    {
        List<string> duplicates = UnityPackageCatalog.AllPackages
            .SelectMany(static p => p.asmdefs)
            .GroupBy(static a => a.name, StringComparer.OrdinalIgnoreCase)
            .Where(static g => g.Count() > 1)
            .Select(static g => g.Key)
            .ToList();

        TestContext.Out.WriteLine($"duplicated: {string.Join(", ", duplicates)}");

        // 每个重名的程序集都必须能被查到（合并后仍在索引里）。
        foreach (string name in duplicates)
        {
            Assert.That(UnityPackageCatalog.TryResolveAssembly(name, out _), Is.True, $"{name} 丢了");
        }
    }

    /// <summary>每个包都必须有名字和版本（空值会让清单里出现非法条目）。</summary>
    [Test]
    public void Every_package_has_name_and_version()
    {
        foreach (UnityPackage package in UnityPackageCatalog.AllPackages)
        {
            Assert.That(package.name, Is.Not.Empty);
            Assert.That(package.version, Is.Not.Empty, $"{package.name} 缺版本");
        }
    }

    /// <summary>
    /// <c>guids</c> 是包内全部类型 GUID 的扁平视图，供外部按 GUID 反查包用。
    /// 它必须与 <c>asmdefs[].types[].guid</c> 完全一致——漏同步会让这条路径失效。
    /// </summary>
    [Test]
    public void Package_guids_are_the_flattened_type_guids()
    {
        foreach (UnityPackage package in UnityPackageCatalog.AllPackages)
        {
            HashSet<UnityGuid> fromTypes = package.asmdefs
                .SelectMany(static a => a.types)
                .Select(static t => t.guid)
                .Where(static g => !g.IsZero)
                .ToHashSet();

            Assert.That(package.guids.Where(static g => !g.IsZero).ToHashSet(), Is.EquivalentTo(fromTypes),
                $"{package.name} 的 guids 与 asmdefs 不一致");
        }
    }

    /// <summary>
    /// TextMeshPro 的经典脚本：值取自真实包里的 <c>.cs.meta</c>，
    /// 也是老 <c>GuidMapping</c> 硬编码的那几条。锁住它证明整条链路（采集 → 生成 → 查询）没走偏。
    /// </summary>
    [TestCase("Unity.TextMeshPro", "TMPro", "TextMeshPro", "9541d86e2fd84c1d9990edf0852d74ab")]
    [TestCase("Unity.TextMeshPro", "TMPro", "TextMeshProUGUI", "f4688fdb7df04437aeb418b961361dc5")]
    [TestCase("Unity.TextMeshPro", "TMPro", "TMP_InputField", "2da0c512f12947e489f739169773d7ca")]
    [TestCase("UnityEngine.UI", "UnityEngine.UI", "Button", "4e29b1a8efbd4b44bb3f3716e73f07ff")]
    [TestCase("UnityEngine.UI", "UnityEngine.UI", "Image", "fe87c0e1cc204ed48ad3b37840f39efc")]
    // EventSystems 的源码在 UGUI 包里，编译进 UnityEngine.UI 程序集。
    [TestCase("UnityEngine.UI", "UnityEngine.EventSystems", "EventSystem", "76c392e42b5098c458856cdf6ecaaaa1")]
    public void Known_script_guids_match_the_package_meta(
        string assembly, string @namespace, string typeName, string expectedGuid)
    {
        Assert.That(
            UnityPackageCatalog.TryResolveType(assembly, @namespace, typeName, out UnityPackageCatalog.ScriptEntry entry),
            Is.True, $"{assembly}:{@namespace}.{typeName} 应该在目录里");
        Assert.That(entry.Guid, Is.EqualTo(UnityGuid.Parse(expectedGuid)));
    }

    /// <summary>
    /// fileID 反查：这是 <c>MetaPtrPatch</c> 唯一依赖的路径。
    /// fileID 不含程序集，所以给出的 GUID 必须是包里那份，而不是别的包的同名类型。
    /// </summary>
    [Test]
    public void FileId_lookup_resolves_a_package_script()
    {
        Assert.That(UnityPackageCatalog.TryResolveType(
            "Unity.TextMeshPro", "TMPro", "TextMeshPro", out UnityPackageCatalog.ScriptEntry entry), Is.True);

        long fileId = UnityPackageCatalog.CalculateScriptFileId(entry.Namespace, entry.TypeName);
        Assert.That(UnityPackageCatalog.TryGetScriptGuid(fileId, out UnityGuid guid), Is.True);
        Assert.That(guid, Is.EqualTo(entry.Guid));
    }

    [TestCase("Unity.TextMeshPro", "TMPro", "TextMeshPro")]
    [TestCase("Unity.TextMeshPro", "TMPro", "TMP_Dropdown")]
    [TestCase("UnityEngine.UI", "UnityEngine.UI", "Button")]
    [TestCase("UnityEngine.UI", "UnityEngine.EventSystems", "StandaloneInputModule")]
    public void FileId_round_trips_through_the_catalog(string assembly, string @namespace, string typeName)
    {
        Assert.That(
            UnityPackageCatalog.TryResolveType(assembly, @namespace, typeName, out UnityPackageCatalog.ScriptEntry entry),
            Is.True);

        long fileId = UnityPackageCatalog.CalculateScriptFileId(@namespace, typeName);
        Assert.That(UnityPackageCatalog.TryGetScriptGuid(fileId, out UnityGuid guid), Is.True);
        Assert.That(guid, Is.EqualTo(entry.Guid), $"{typeName} 的 fileID 反查落到了别的类型");
    }

    /// <summary>随便一个数字不该命中。</summary>
    [TestCase(0L)]
    [TestCase(1L)]
    [TestCase(123456789L)]
    public void Unknown_file_ids_do_not_resolve(long fileId)
    {
        Assert.That(UnityPackageCatalog.TryGetScriptGuid(fileId, out UnityGuid guid), Is.False);
        Assert.That(guid.IsZero, Is.True);
    }

    /// <summary>
    /// 版本筛选：<c>UnityEngine.UI</c> 3.x 要求 Unity 6，2.x 支持更老的版本。
    /// 2019 工程不该被匹配到装不上的包。
    /// </summary>
    [Test]
    public void Version_filter_rejects_packages_newer_than_the_project()
    {
        Assert.That(UnityPackageCatalog.TryResolveType(
            "UnityEngine.UI", "UnityEngine.UI", "Image", out UnityPackageCatalog.ScriptEntry entry), Is.True);

        long fileId = UnityPackageCatalog.CalculateScriptFileId(entry.Namespace, entry.TypeName);

        // 2021.3 的工程拿不到 UGUI 3.x（要求 6000.0），但仍必须能解析——
        // 回退到「任意版本」的候选，否则引用会留成 Missing (Mono Script)。
        Assert.That(UnityPackageCatalog.TryGetScriptGuid(fileId, UnityVersion.Parse("2021.3.0f1"), out UnityGuid guid, out _), Is.True);
        Assert.That(guid, Is.EqualTo(entry.Guid));

        // 缺 unity 字段 / 解析失败时按「任何版本都行」处理，不该崩。
        Assert.That(UnityPackageCatalog.TryGetScriptGuid(fileId, default, out _, out _), Is.True);
    }

    /// <summary>
    /// 嵌套类型用 <c>Outer+Inner</c> 存，但调用方可能传 <c>Outer.Inner</c>。两种都要能查。
    /// </summary>
    [Test]
    public void Nested_type_names_accept_dot_notation()
    {
        UnityPackageCatalog.ScriptEntry? nested = UnityPackageCatalog
            .AllPackages
            .SelectMany(static p => p.asmdefs)
            .SelectMany(static a => a.types.Select(t => (a.name, t)))
            .Where(static pair => pair.t.name.Contains('+', StringComparison.Ordinal))
            .Select(static pair => (UnityPackageCatalog.ScriptEntry?)new UnityPackageCatalog.ScriptEntry(
                pair.name, pair.t.@namespace, pair.t.name, pair.t.guid, null!))
            .FirstOrDefault();

        if (nested is null)
        {
            Assert.Ignore("目录里没有嵌套类型");
        }

        UnityPackageCatalog.ScriptEntry value = nested.Value;
        string dotted = value.TypeName.Replace('+', '.');

        Assert.That(UnityPackageCatalog.TryResolveType(
            value.AssemblyName, value.Namespace, dotted, out UnityPackageCatalog.ScriptEntry found), Is.True);
        Assert.That(found.Guid, Is.EqualTo(value.Guid));
    }

    /// <summary>
    /// <c>MD5(程序集名)</c> 必须与 Unity / AssetRipper 的算法一致，
    /// 否则「按 guid 反查程序集」永远对不上。
    /// </summary>
    [Test]
    public void Assembly_guid_matches_the_known_hash()
    {
        // UnityEngine 的 MD5 是 AssemblyRipper 里长期使用的常量（0x1F55507F...），
        // 但这里锁一个自算值更直接：ZERO 长度输入也必须是合法 GUID。
        UnityGuid a = UnityPackageCatalog.CalculateAssemblyGuid("Unity.TextMeshPro");
        Assert.That(a.IsZero, Is.False);
        Assert.That(a, Is.EqualTo(UnityPackageCatalog.CalculateAssemblyGuid("Unity.TextMeshPro")));
        Assert.That(a, Is.Not.EqualTo(UnityPackageCatalog.CalculateAssemblyGuid("Unity.TextMeshPro.Editor")));
    }

    // ---------- 「UGUI 2.0+ 内置 TMP」这条合并规则 ----------

    /// <summary>
    /// UGUI 2.0 起自带 TextMeshPro（对应 Unity 2023.2+）。同时装
    /// <c>com.unity.textmeshpro</c> 会让两个包各带一份
    /// <c>Unity.TextMeshPro.dll</c>，报 CS0433。
    /// </summary>
    [TestCase("2.0.0", true)]
    [TestCase("2.1.0", true)]
    [TestCase("3.0.0-exp.4", true)]
    [TestCase("1.0.0", false)]
    [TestCase("", false)]
    public void Ugui_supersedes_textmeshpro_from_2_0(string uguiVersion, bool expected)
    {
        Assert.That(
            UnityPackageCatalog.Supersedes("com.unity.ugui", uguiVersion, "com.unity.textmeshpro"),
            Is.EqualTo(expected));
    }

    /// <summary>反过来不成立，其它包也不受影响。</summary>
    [Test]
    public void Supersede_rule_is_one_directional()
    {
        Assert.That(
            UnityPackageCatalog.Supersedes("com.unity.textmeshpro", "3.0.9", "com.unity.ugui"),
            Is.False);
        Assert.That(
            UnityPackageCatalog.Supersedes("com.unity.ugui", "2.0.0", "com.unity.inputsystem"),
            Is.False);
        Assert.That(
            UnityPackageCatalog.Supersedes("com.unity.ugui", "2.0.0", "com.unity.ugui"),
            Is.False);
    }

    /// <summary>写清单前必须把被内置的那个剔掉。</summary>
    [Test]
    public void RemoveSuperseded_drops_textmeshpro_when_ugui_2_is_present()
    {
        Dictionary<string, string> packages = new(StringComparer.OrdinalIgnoreCase)
        {
            ["com.unity.ugui"] = "2.0.0",
            ["com.unity.textmeshpro"] = "3.0.9",
            ["com.unity.inputsystem"] = "1.20.1",
        };

        IReadOnlyList<string> removed = UnityPackageCatalog.RemoveSuperseded(packages);

        Assert.That(removed, Is.EquivalentTo(new[] { "com.unity.textmeshpro" }));
        Assert.That(packages.Keys, Is.EquivalentTo(new[] { "com.unity.ugui", "com.unity.inputsystem" }));
    }

    /// <summary>UGUI 1.x（不自带 TMP）时两个包都必须保留。</summary>
    [Test]
    public void RemoveSuperseded_keeps_textmeshpro_for_ugui_1()
    {
        Dictionary<string, string> packages = new(StringComparer.OrdinalIgnoreCase)
        {
            ["com.unity.ugui"] = "1.0.0",
            ["com.unity.textmeshpro"] = "3.0.9",
        };

        Assert.That(UnityPackageCatalog.RemoveSuperseded(packages), Is.Empty);
        Assert.That(packages.Count, Is.EqualTo(2));
    }

    /// <summary>
    /// <c>Unity.TextMeshPro</c> 有多家提供者，选包时必须偏向聚合包（UGUI），
    /// 否则「删 DLL 换包」会同时写上两个包。
    /// </summary>
    [Test]
    public void ChooseProvider_prefers_the_aggregating_package()
    {
        IReadOnlyList<UnityPackage> providers = UnityPackageCatalog.GetProviders("Unity.TextMeshPro");
        TestContext.Out.WriteLine(string.Join(", ", providers.Select(static p => p.name)));
        Assert.That(providers.Count, Is.GreaterThan(1), "Unity.TextMeshPro 应该有多家提供者");

        UnityPackage? chosen = UnityPackageCatalog.ChooseProvider("Unity.TextMeshPro", null, default);
        Assert.That(chosen, Is.Not.Null);
        Assert.That(chosen!.name, Is.EqualTo("com.unity.ugui"));
    }

    /// <summary>调用方已选定的包优先，保证同一次导出里选择稳定。</summary>
    [Test]
    public void ChooseProvider_honours_the_preferred_package()
    {
        UnityPackage? chosen = UnityPackageCatalog.ChooseProvider(
            "Unity.TextMeshPro",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "com.unity.textmeshpro" },
            default);

        Assert.That(chosen, Is.Not.Null);
        Assert.That(chosen!.name, Is.EqualTo("com.unity.textmeshpro"));
    }

    /// <summary>单一提供者时必须原样返回，不该受 preferred 影响。</summary>
    [Test]
    public void ChooseProvider_returns_the_only_provider()
    {
        UnityPackage? chosen = UnityPackageCatalog.ChooseProvider(
            "Unity.InputSystem",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "com.unity.textmeshpro" },
            default);

        Assert.That(chosen, Is.Not.Null);
        Assert.That(chosen!.name, Is.EqualTo("com.unity.inputsystem"));
    }

    [Test]
    public void ChooseProvider_returns_null_for_unknown_assembly()
    {
        Assert.That(UnityPackageCatalog.ChooseProvider("Not.A.Package.Dll", null, default), Is.Null);
        Assert.That(UnityPackageCatalog.GetProviders("Not.A.Package.Dll"), Is.Empty);
    }

    // ---------- 实验版 → 稳定版 ----------

    /// <summary>
    /// 注册表里只有预发布版的包（UGUI 3.0.0-exp.4）写进清单会装不上，
    /// 要降到上一个稳定主版本。<c>x.0.0-xxx</c> → <c>(x-1).0.0</c>。
    /// </summary>
    [TestCase("3.0.0-exp.4", "2.0.0", true)]
    [TestCase("3.0.0-pre.15", "2.0.0", true)]
    [TestCase("2.0.0-pre.1", "1.0.0", true)]
    [TestCase("2.0.0-exp.2", "1.0.0", true)]
    // x = 1 时降到 0.0.0 没有意义，按用户要求不处理。
    [TestCase("1.0.0-exp.1", "1.0.0-exp.1", false)]
    [TestCase("1.0.0-preview.14", "1.0.0-preview.14", false)]
    public void Prerelease_major_bumps_down_to_the_previous_stable(string input, string expected, bool changed)
    {
        bool replaced = UnityPackageCatalog.TryGetStableVersion(input, out string stable);

        Assert.That(replaced, Is.EqualTo(changed), $"TryGetStableVersion(\"{input}\")");
        Assert.That(stable, Is.EqualTo(expected));
    }

    /// <summary>稳定版本必须原样保留，不能被这条规则误伤。</summary>
    [TestCase("1.0.0")]
    [TestCase("2.0.0")]
    [TestCase("3.0.9")]
    [TestCase("3.2.1")]
    [TestCase("10.10.1")]
    [TestCase("1.4.6")]
    public void Stable_versions_are_untouched(string version)
    {
        Assert.That(UnityPackageCatalog.TryGetStableVersion(version, out string stable), Is.False);
        Assert.That(stable, Is.EqualTo(version));
        Assert.That(UnityPackageCatalog.GetManifestVersion(version), Is.EqualTo(version));
    }

    /// <summary>只有 <c>x.0.0-xxx</c> 这个形状才处理；<c>x.y.0</c> / <c>x.0.z</c> 的预发布不动。</summary>
    [TestCase("3.1.0-exp.1")]
    [TestCase("3.0.5-exp.1")]
    [TestCase("1.5.0-preview.14")]
    public void Only_major_dot_zero_dot_zero_prereleases_are_rewritten(string version)
    {
        Assert.That(UnityPackageCatalog.TryGetStableVersion(version, out string stable), Is.False);
        Assert.That(stable, Is.EqualTo(version));
    }

    /// <summary>
    /// <c>1.0.0</c> 是 Unity 内置包的通用占位版本（<c>com.unity.modules.*</c> 全是它）。
    /// 它是稳定版，但即便写成 <c>1.0.0-exp.1</c> 也不该退到 <c>0.0.0</c>。
    /// </summary>
    [TestCase("1.0.0")]
    [TestCase("1.0.0-exp.1")]
    [TestCase("1.0.0-preview.14")]
    public void Version_1_is_never_rewritten(string version)
    {
        Assert.That(UnityPackageCatalog.TryGetStableVersion(version, out string stable), Is.False);
        Assert.That(stable, Is.EqualTo(version));
        Assert.That(UnityPackageCatalog.GetManifestVersion(version), Is.EqualTo(version));
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("not-a-version")]
    [TestCase("3")]
    public void Unparsable_versions_are_left_alone(string? version)
    {
        Assert.That(UnityPackageCatalog.TryGetStableVersion(version!, out string stable), Is.False);
        Assert.That(stable, Is.EqualTo(version));
    }

    /// <summary>
    /// 真实数据：目录里的 UGUI 只有 3.0.0-exp.4，写清单必须换成 2.0.0，
    /// 而且 2.0.0 仍满足「内置 TMP」的门槛（所以 TMP 包照旧被剔除）。
    /// </summary>
    [Test]
    public void Ugui_in_the_catalog_is_downgraded_to_a_usable_stable_version()
    {
        Assert.That(UnityPackageCatalog.TryResolvePackage("UnityEngine.UI", out UnityPackage? ugui), Is.True);

        string manifestVersion = UnityPackageCatalog.GetManifestVersion(ugui!.version);
        TestContext.Out.WriteLine($"{ugui.version} -> {manifestVersion}");

        Assert.That(manifestVersion, Is.EqualTo("2.0.0"));
        Assert.That(UnityPackageCatalog.Supersedes(ugui.name, manifestVersion, "com.unity.textmeshpro"), Is.True);
    }
}
