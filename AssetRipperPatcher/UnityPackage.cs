using AssetRipper.Primitives;

namespace AssetRipperPatcher;

/// <summary>
/// 一个 Unity 官方 UPM 包，以及它带进来的程序集与脚本 GUID。
///
/// <para>数据由 <c>tools/UnityPackageHarvester</c> 从 <c>https://packages.unity.com</c>
/// 的 tarball 里解出来（见 <c>UnityPackageCatalog.Generated.cs</c>）。</para>
/// </summary>
public class UnityPackage
{
    /// <summary>UPM 包名，例如 <c>com.unity.textmeshpro</c>。</summary>
    public string name { get; set; } = string.Empty;

    public string displayName { get; set; } = string.Empty;

    /// <summary>用于写 <c>Packages/manifest.json</c> 的版本号。</summary>
    public string version { get; set; } = string.Empty;

    /// <summary>最低 Unity 版本（<c>package.json</c> 的 <c>unity</c> 字段）。</summary>
    public string unity { get; set; } = string.Empty;

    public string description { get; set; } = string.Empty;
    public string category { get; set; } = string.Empty;

    /// <summary>包名 → 版本，来自 <c>package.json</c> 的 <c>dependencies</c>。</summary>
    public Dictionary<string, string> dependencies { get; set; } = new();

    /// <summary>包内所有可序列化脚本的 GUID（<see cref="AsmDef.types"/> 的扁平汇总）。</summary>
    public List<UnityGuid> guids { get; set; } = new();

    /// <summary>包里所有 asmdef（= 所有 DLL）。</summary>
    public List<AsmDef> asmdefs { get; set; } = new();
}
