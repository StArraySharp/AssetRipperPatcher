using System.Reflection;
using AssetRipper.Export.UnityProjects.Project;
using AssetRipper.Primitives;
using HarmonyLib;

namespace AssetRipperPatcher.Patches;

/// <summary>
/// PackageManifest 补丁：导出 manifest.json 时注入 IDE 与 UGUI 包依赖。
/// </summary>
internal static class PackageManifestPatch
{
    public static void Register(Harmony harmony)
    {
        Type manifestExporterType = typeof(PackageManifestPostExporter);
        MethodInfo? createManifest = manifestExporterType.GetMethod("CreateManifest",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null,
            [typeof(UnityVersion)],
            null);
        if (createManifest != null)
        {
            harmony.Patch(createManifest,
                prefix: new HarmonyMethod(typeof(PackageManifestPatch).GetMethod(nameof(PatchCreateManifest))));
            Console.WriteLine("PackageManifestPostExporter.CreateManifest Patched");
        }
        else
        {
            Console.WriteLine("PackageManifestPostExporter.CreateManifest not found");
        }
    }

    /// <summary>
    /// Harmony Prefix: 在导出 manifest.json 时注入 IDE 和 UGUI 包依赖
    /// </summary>
    public static bool PatchCreateManifest(UnityVersion version, ref PackageManifest __result)
    {
        // 先调用原始逻辑获取默认清单
        __result = PackageManifest.CreateDefault(version);

        // 注入 IDE 支持
        __result.Dependencies.TryAdd("com.unity.ide.rider", "3.0.31");
        __result.Dependencies.TryAdd("com.unity.ide.visualstudio", "2.0.22");

        // 仅 Unity 6+ 注入 UGUI（UGUI 自带 TMP，无需单独声明）
        if (AppState.IsUnity6)
        {
            __result.Dependencies.TryAdd("com.unity.ugui", GuidMapping.UGUIVersion);
        }

        return false; // 跳过原始方法
    }
}
