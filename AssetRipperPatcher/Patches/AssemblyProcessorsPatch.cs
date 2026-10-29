using System.Reflection;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Import.Configuration;
using AssetRipper.Processing;
using HarmonyLib;

namespace AssetRipperPatcher.Patches;

/// <summary>
/// 处理器补丁：ScriptContentLevel == Level2 时跳过所有 Assembly processors，
/// 避免它们用 AsmResolver 修改 DLL 元数据，从而间接导致资源导出时类型/字段缺失。
/// </summary>
internal static class AssemblyProcessorsPatch
{
    /// <summary>
    /// Assembly processors 所在的命名空间。
    /// 这些处理器会用 AsmResolver 修改 DLL 元数据（重命名字段、删/加特性、补属性等）。
    /// </summary>
    private const string AssemblyProcessorsNamespace = "AssetRipper.Processing.Assemblies";

    public static void Register(Harmony harmony)
    {
        MethodInfo? getProcessors = typeof(ExportHandler).GetMethod("GetProcessors",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (getProcessors != null)
        {
            harmony.Patch(getProcessors,
                postfix: new HarmonyMethod(typeof(AssemblyProcessorsPatch).GetMethod(nameof(SkipAssemblyProcessors))));
            Console.WriteLine("ExportHandler.GetProcessors Patched (skip assembly processors on Level2)");
        }
        else
        {
            Console.WriteLine("ExportHandler.GetProcessors not found");
        }
    }

    /// <summary>
    /// Harmony Postfix: 在 ScriptContentLevel == Level2 时跳过所有 Assembly processors。
    /// 使用 Postfix 而非 Prefix：原始 GetProcessors 已正常返回完整列表（惰性），
    /// 这里只需过滤并替换 __result，避免在 Prefix 里反射调用原方法造成递归（栈溢出）。
    /// </summary>
    public static void SkipAssemblyProcessors(ExportHandler __instance, ref IEnumerable<IAssetProcessor> __result)
    {
        if (GetScriptContentLevel(__instance) != ScriptContentLevel.Level2)
        {
            return; // 非 Level2 保持原样
        }

        IAssetProcessor[] all = __result.ToArray();
        IAssetProcessor[] filtered = all
            .Where(p => p.GetType().Namespace != AssemblyProcessorsNamespace)
            .ToArray();

        __result = filtered;
        Console.WriteLine($"Skipped {all.Length - filtered.Length} assembly processor(s) on ScriptContentLevel.Level2");
    }

    private static ScriptContentLevel GetScriptContentLevel(ExportHandler instance)
    {
        try
        {
            PropertyInfo? settingsProperty = typeof(ExportHandler).GetProperty("Settings",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (settingsProperty?.GetValue(instance) is CoreConfiguration configuration)
            {
                return configuration.ImportSettings.ScriptContentLevel;
            }
        }
        catch
        {
            // 忽略反射异常，按默认 Level2 处理
        }
        return ScriptContentLevel.Level2;
    }
}
