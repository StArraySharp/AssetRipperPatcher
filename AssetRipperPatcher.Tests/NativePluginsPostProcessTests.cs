using AssetRipperPatcher;
using NUnit.Framework;

namespace AssetRipperPatcher.Tests;

/// <summary>
/// 导出后处理（<see cref="NativePluginsPostProcess"/>）的回归测试：
/// 插件 <c>.meta</c> 的平台判定，以及「裸 DLL 换包」时**是否安全删除**的判断。
/// </summary>
/// <remarks>
/// 每个用例必须拿到独立的临时目录：NUnit 默认复用同一个 fixture 实例，
/// 会让上一个用例留下的插件文件污染下一个用例的计数。
/// </remarks>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class NativePluginsPostProcessTests : IDisposable
{
    private readonly string _root;

    public NativePluginsPostProcessTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "arp-plugins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "Assets"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响测试结论
        }
    }

    private string PluginFile(string relativePath)
    {
        string full = Path.Combine(_root, "Assets", "Plugins", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, [0x4D, 0x5A]); // "MZ" 头，内容无所谓
        return full;
    }

    private string AssetsDir => Path.Combine(_root, "Assets");

    private static string Read(string path) => File.ReadAllText(path);

    private static HashSet<string> Referenced(params string[] names) =>
        new(names, StringComparer.OrdinalIgnoreCase);

    // ---------- .meta 生成 ----------

    [Test]
    public void Writes_meta_for_native_library_and_platform_is_from_folder()
    {
        PluginFile(Path.Combine("x86_64", "foo.dll"));
        Dictionary<string, string> packages = new();

        (int metas, int removed, int kept) = NativePluginsPostProcess.Run(AssetsDir, packages);

        Assert.That(metas, Is.EqualTo(1));
        Assert.That(removed, Is.EqualTo(0));
        Assert.That(kept, Is.EqualTo(0));
        Assert.That(packages, Is.Empty);

        string meta = Read(Path.Combine(_root, "Assets", "Plugins", "x86_64", "foo.dll.meta"));
        TestContext.Out.WriteLine(meta);
        Assert.That(meta, Does.Contain("PluginImporter:"));
        Assert.That(meta, Does.Contain("Standalone: Win64"));
        Assert.That(meta, Does.Contain("CPU: x86_64"));
        // 「Any」必须先关掉，否则等于全平台可用。
        Assert.That(meta, Does.Contain(": Any"));
        Assert.That(meta, Does.Contain("enabled: 0"));
    }

    [Test]
    public void Android_folder_produces_android_platform_block()
    {
        PluginFile(Path.Combine("Android", "arm64-v8a", "libfoo.so"));
        NativePluginsPostProcess.Run(AssetsDir, new Dictionary<string, string>());

        string meta = Read(Path.Combine(_root, "Assets", "Plugins", "Android", "arm64-v8a", "libfoo.so.meta"));
        TestContext.Out.WriteLine(meta);
        Assert.That(meta, Does.Contain("Android"));
    }

    /// <summary>AssetRipper 已经写了 .meta 的插件不能被覆盖。</summary>
    [Test]
    public void Existing_meta_is_left_alone()
    {
        string dll = PluginFile(Path.Combine("x86_64", "bar.dll"));
        string metaPath = dll + ".meta";
        const string original = "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n";
        File.WriteAllText(metaPath, original);

        (int metas, _, _) = NativePluginsPostProcess.Run(AssetsDir, new Dictionary<string, string>());

        Assert.That(metas, Is.EqualTo(0));
        Assert.That(Read(metaPath), Is.EqualTo(original));
    }

    /// <summary>同一路径重复跑必须得到同样的 GUID，否则每次导出都在版本控制里产生伪改动。</summary>
    [Test]
    public void Meta_guid_is_deterministic_across_runs()
    {
        PluginFile(Path.Combine("x86_64", "baz.dll"));
        NativePluginsPostProcess.Run(AssetsDir, new Dictionary<string, string>());
        string first = Read(Path.Combine(_root, "Assets", "Plugins", "x86_64", "baz.dll.meta"));

        File.Delete(Path.Combine(_root, "Assets", "Plugins", "x86_64", "baz.dll.meta"));
        NativePluginsPostProcess.Run(AssetsDir, new Dictionary<string, string>());
        string second = Read(Path.Combine(_root, "Assets", "Plugins", "x86_64", "baz.dll.meta"));

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void Missing_plugins_directory_is_not_an_error()
    {
        (int metas, int removed, int kept) = NativePluginsPostProcess.Run(AssetsDir, new Dictionary<string, string>());
        Assert.That(metas, Is.EqualTo(0));
        Assert.That(removed, Is.EqualTo(0));
        Assert.That(kept, Is.EqualTo(0));
    }

    // ---------- 删 DLL 的安全性判断 ----------

    /// <summary>
    /// 核心用例：命中映射表但**没有脚本引用** → 安全删除。
    /// 纯类库（Newtonsoft.Json）就是这种情况。
    /// </summary>
    [Test]
    public void Unreferenced_packaged_dll_is_removed()
    {
        string dll = PluginFile(Path.Combine("x86_64", "Newtonsoft.Json.dll"));
        Dictionary<string, string> packages = new();

        (int metas, int removed, int kept) = NativePluginsPostProcess.Run(
            AssetsDir, packages, referencedAssemblies: Referenced());

        Assert.That(File.Exists(dll), Is.False, "无脚本引用的 DLL 应该被删除");
        Assert.That(removed, Is.EqualTo(1));
        Assert.That(kept, Is.EqualTo(0));
        Assert.That(metas, Is.EqualTo(0));
        Assert.That(packages.ContainsKey("com.unity.nuget.newtonsoft-json"), Is.True);
        Assert.That(packages["com.unity.nuget.newtonsoft-json"], Is.EqualTo("3.2.1"));
    }

    /// <summary>
    /// 核心用例：命中映射表但**有脚本引用** → 必须保留。
    /// 删掉它会让工程里所有指向它的 m_Script 变成 Missing (Mono Script)，
    /// 而包内程序集的 GUID 是 Unity 导入时随机生成的，导出阶段无法回填。
    /// </summary>
    [Test]
    public void Referenced_packaged_dll_is_kept()
    {
        string dll = PluginFile(Path.Combine("x86_64", "Newtonsoft.Json.dll"));
        Dictionary<string, string> packages = new();

        (int metas, int removed, int kept) = NativePluginsPostProcess.Run(
            AssetsDir, packages, referencedAssemblies: Referenced("Newtonsoft.Json"));

        Assert.That(File.Exists(dll), Is.True, "有脚本引用的 DLL 必须保留");
        Assert.That(removed, Is.EqualTo(0));
        Assert.That(kept, Is.EqualTo(1));
        // 包依赖仍然要登记：包能提供 Editor 程序集与资源。
        Assert.That(packages.ContainsKey("com.unity.nuget.newtonsoft-json"), Is.True);
        // 保留的 DLL 也要补 .meta，否则默认全平台可用。
        Assert.That(metas, Is.EqualTo(1));
        Assert.That(File.Exists(dll + ".meta"), Is.True);
    }

    /// <summary>
    /// 调用方没提供引用信息（null）时必须保守：一律不删。
    /// </summary>
    [Test]
    public void Missing_reference_info_keeps_the_dll()
    {
        string dll = PluginFile("Newtonsoft.Json.dll");

        (_, int removed, int kept) = NativePluginsPostProcess.Run(
            AssetsDir, new Dictionary<string, string>(), referencedAssemblies: null);

        Assert.That(File.Exists(dll), Is.True);
        Assert.That(removed, Is.EqualTo(0));
        Assert.That(kept, Is.EqualTo(1));
    }

    /// <summary>强制删除：即使有脚本引用也删（用户显式要求）。</summary>
    [Test]
    public void Force_flag_removes_even_when_referenced()
    {
        string dll = PluginFile("Newtonsoft.Json.dll");

        (_, int removed, int kept) = NativePluginsPostProcess.Run(
            AssetsDir, new Dictionary<string, string>(),
            referencedAssemblies: Referenced("Newtonsoft.Json"),
            removePackagedDlls: true);

        Assert.That(File.Exists(dll), Is.False);
        Assert.That(removed, Is.EqualTo(1));
        Assert.That(kept, Is.EqualTo(0));
    }

    /// <summary>删 DLL 时同名的 .pdb / .xml 也要清掉，否则留下孤儿文件。</summary>
    [Test]
    public void Removed_dll_takes_its_pdb_and_xml_with_it()
    {
        string dll = PluginFile("Newtonsoft.Json.dll");
        File.WriteAllText(Path.ChangeExtension(dll, ".pdb"), "x");
        File.WriteAllText(Path.ChangeExtension(dll, ".xml"), "x");

        NativePluginsPostProcess.Run(
            AssetsDir, new Dictionary<string, string>(), referencedAssemblies: Referenced());

        Assert.That(File.Exists(Path.ChangeExtension(dll, ".pdb")), Is.False);
        Assert.That(File.Exists(Path.ChangeExtension(dll, ".xml")), Is.False);
    }

    /// <summary>不在映射表里的 DLL 永远不该被删——那可能是游戏自己的程序集。</summary>
    [Test]
    public void Unmapped_dll_is_never_removed()
    {
        string dll = PluginFile(Path.Combine("x86_64", "MyGame.Custom.dll"));

        (_, int removed, int kept) = NativePluginsPostProcess.Run(
            AssetsDir, new Dictionary<string, string>(), referencedAssemblies: Referenced());

        Assert.That(File.Exists(dll), Is.True);
        Assert.That(removed, Is.EqualTo(0));
        Assert.That(kept, Is.EqualTo(0));
    }
}
