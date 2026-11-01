using System.Text;
using AsmResolver.DotNet;
using AssetRipper.Import.AssetCreation;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.Import.Structure.Assembly.Serializable;
using AssetRipper.IO.Endian;
using AssetRipper.SerializationLogic;
using AssetRipper.SourceGenerated.Classes.ClassID_114;
using AssetRipper.SourceGenerated.Classes.ClassID_115;
using AssetRipperPatcher.Patching;

namespace AssetRipperPatcher.Patches;

/// <summary>
/// MonoBehaviour 读取补丁：标准读取失败后用容错模式重试（缺失字段给默认值），
/// 容错也失败时写诊断文件，并对读取失败日志按脚本类型限流。
/// </summary>
internal static class MonoBehaviourReadPatch
{
    /// <summary>
    /// MonoBehaviour 加载失败诊断文件路径（写入到程序运行目录）。
    /// </summary>
    private static readonly string DiagnosticFilePath = System.IO.Path.Combine(AppContext.BaseDirectory, $"MonoBehaviourDiagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
    private static readonly object DiagnosticFileLock = new();

    /// <summary>
    /// 容错读取开关：开启时 SerializableStructure.TryRead 不再因数据不匹配而失败（缺失字段保持默认值）。
    /// </summary>
    public static bool LenientReadMode;
    private static bool LenientRetryInProgress;
    private static readonly HashSet<string> LoggedFailures = new();
    private static readonly object LoggedFailuresLock = new();

    /// <summary>
    /// Harmony Prefix: 容错模式下，SerializableStructure.TryRead 直接调用 Read 并捕获异常
    /// （接受部分读取，缺失字段保持默认值），并跳过原方法的位置检查。
    /// 2.0.0 起 Read/TryRead 多了一个必填的 <c>ITypeResolver</c> 参数，
    /// 由 Harmony 按名匹配透传进来，再原样传给 Read。
    /// </summary>
    [PatchMethod(typeof(SerializableStructure), "TryRead", PatchType = PatchType.Prefix)]
    public static bool TryRead_LenientPrefix(SerializableStructure __instance, ref EndianSpanReader reader,
        IMonoBehaviour monoBehaviour, ITypeResolver resolver, ref bool __result)
    {
        if (!LenientReadMode)
        {
            return true; // 正常模式，走原方法
        }
        try
        {
            __instance.Read(ref reader, monoBehaviour.Collection.Version, monoBehaviour.Collection.Flags, resolver);
        }
        catch
        {
            // 部分读取：缺失字段保持默认值
        }
        __result = true; // 容错：不检查 Position != Length
        return false;    // 跳过原方法
    }

    /// <summary>
    /// Harmony Postfix: 标准读取失败后，用容错模式重新读取（尽量读，缺失字段给默认值）。
    /// </summary>
    [PatchMethod(typeof(UnloadedStructure), "LoadStructure", PatchType = PatchType.Postfix)]
    public static void LenientRetryPostfix(UnloadedStructure __instance, ref SerializableStructure? __result)
    {
        if (__result is not null || LenientRetryInProgress)
        {
            return;
        }
        LenientRetryInProgress = true;
        try
        {
            __result = TryLenientRead(__instance);
        }
        finally
        {
            LenientRetryInProgress = false;
        }
        // 容错也失败：此时才写诊断文件（避免诊断文件刷屏）
        if (__result is null)
        {
            DiagnoseMonoBehaviourLoadFailure(__instance, ref __result);
        }
    }

    private static SerializableStructure? TryLenientRead(UnloadedStructure instance)
    {
        try
        {
            IMonoBehaviour mb = instance.MonoBehaviour;
            IMonoScript? script = mb.ScriptP;
            if (script is null)
            {
                return null;
            }
            string? failureReason = null;
            SerializableStructure? structure = script.GetBehaviourType(instance.AssemblyManager, out failureReason)?.CreateSerializableStructure();
            if (structure is null)
            {
                return null;
            }

            EndianSpanReader reader = new EndianSpanReader(instance.StructureData, mb.Collection.EndianType);
            LenientReadMode = true;
            try
            {
                // 与上游 UnloadedStructure.LoadStructure 相同：IAssemblyManager
                // 实现 ITypeResolver，用它解析脚本类型。
                if (structure.TryRead(ref reader, mb, instance.AssemblyManager))
                {
                    mb.Structure = structure;
                    return structure;
                }
                return null;
            }
            finally
            {
                LenientReadMode = false;
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 限流：相同 key 只打印一次日志。
    /// </summary>
    private static bool ShouldLog(string key)
    {
        lock (LoggedFailuresLock)
        {
            return LoggedFailures.Add(key);
        }
    }

    [PatchMethod(typeof(SerializableStructure), "LogMonoBehaviourMismatch", PatchType = PatchType.Prefix)]
    public static bool LimitMismatchLog(SerializableStructure structure) => ShouldLog("mm:" + structure);

    [PatchMethod(typeof(SerializableStructure), "LogMonoBehaviorReadException", PatchType = PatchType.Prefix)]
    public static bool LimitReadExceptionLog(SerializableStructure structure) => ShouldLog("re:" + structure);

    [PatchMethod(typeof(GameAssetFactory), "LogMonoBehaviorReadException", PatchType = PatchType.Prefix)]
    public static bool LimitReadExceptionLog2(IMonoBehaviour monoBehaviour) => ShouldLog("re2:" + (monoBehaviour.ScriptP?.GetFullName() ?? "?"));

    /// <summary>
    /// Harmony Prefix: 对 Logger 里的 MonoBehaviour 读取失败信息按脚本名限流（每类型只打印一次）。
    /// Logger.Log 有 (LogType,LogCategory,string) 与 (LogType,LogCategory,string[]) 两个重载，
    /// 参数个数相同；确定性排序下 string[] 版本是 index 0，string 版本是 index 1。
    /// </summary>
    [PatchMethod(typeof(Logger), "Log", OverrideIndex = 1, PatchType = PatchType.Prefix)]
    public static bool LimitLog(LogType type, LogCategory category, string message)
    {
        if (message.StartsWith("Could not read MonoBehaviour structure", StringComparison.Ordinal)
            || message.StartsWith("Unable to read MonoBehaviour Structure", StringComparison.Ordinal))
        {
            string key = ExtractScriptKey(message) ?? message;
            return ShouldLog("log:" + key);
        }
        return true;
    }

    private static string? ExtractScriptKey(string message)
    {
        int backtickStart = message.IndexOf('`');
        if (backtickStart >= 0)
        {
            int backtickEnd = message.IndexOf('`', backtickStart + 1);
            if (backtickEnd > backtickStart)
            {
                return message.Substring(backtickStart + 1, backtickEnd - backtickStart - 1);
            }
        }
        int scriptIdx = message.IndexOf("script ", StringComparison.Ordinal);
        if (scriptIdx >= 0)
        {
            int layoutIdx = message.IndexOf(" layout", scriptIdx, StringComparison.Ordinal);
            if (layoutIdx > scriptIdx)
            {
                return message.Substring(scriptIdx + 7, layoutIdx - scriptIdx - 7);
            }
        }
        return null;
    }

    /// <summary>
    /// Harmony Postfix: UnloadedStructure.LoadStructure 失败（返回 null）时，
    /// 把 MonoBehaviour 加载失败的详细信息写入独立诊断文件，便于分析根因。
    /// 诊断文件：{DiagnosticFilePath}
    /// </summary>
    public static void DiagnoseMonoBehaviourLoadFailure(UnloadedStructure __instance, ref SerializableStructure? __result)
    {
        if (__result is not null)
        {
            return; // 成功加载，不诊断
        }

        // 接口/抽象/静态类等（Script ID invalid）没有可序列化数据，跳过诊断以减少刷屏
        IMonoScript? preScript = __instance.MonoBehaviour.ScriptP;
        if (preScript is not null)
        {
            ScriptIdentifier preId = __instance.AssemblyManager.GetScriptID(
                preScript.GetAssemblyNameFixed(), preScript.Namespace.String, preScript.ClassName_R.String);
            if (!__instance.AssemblyManager.IsValid(preId))
            {
                return; // 类型无效（接口/抽象/静态类），无数据可诊断
            }
        }

        StringBuilder sb = new StringBuilder();
        try
        {
            IMonoBehaviour monoBehaviour = __instance.MonoBehaviour;
            IAssemblyManager manager = __instance.AssemblyManager;
            sb.AppendLine("===================== MonoBehaviour Load Failure =====================");

            IMonoScript? script = monoBehaviour.ScriptP;
            if (script is null)
            {
                sb.AppendLine("  m_Script: <null>");
            }
            else
            {
                string assembly = script.GetAssemblyNameFixed();
                string ns = script.Namespace.String;
                string cls = script.ClassName_R.String;
                sb.AppendLine($"  Script: {assembly} / {ns} / {cls}");

                ScriptIdentifier id = manager.GetScriptID(assembly, ns, cls);
                sb.AppendLine($"  AssemblyPresent : {manager.IsPresent(id)}");
                sb.AppendLine($"  ScriptIDValid   : {manager.IsValid(id)}");

                if (manager.TryGetSerializableType(id, monoBehaviour.Collection.Version, out SerializableType? scriptType, out string? failureReason))
                {
                    sb.AppendLine($"  SerializableType: ok (fields: {scriptType!.Fields.Count})");
                    foreach (SerializableType.Field f in scriptType.Fields)
                    {
                        sb.AppendLine($"      {f.Name} : {f.Type.FullName} (ArrayDepth={f.ArrayDepth})");
                    }
                }
                else
                {
                    sb.AppendLine($"  SerializableType: FAILED  Reason: {failureReason ?? "-"}");
                }

                AppendTypeDefinitionDiagnostics(manager, id, sb);
            }

            sb.AppendLine($"  StructureDataLength: {__instance.StructureData.Count} bytes");
            sb.AppendLine("======================================================================");
            sb.AppendLine();
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  [Diagnostic error] {ex}");
        }
        finally
        {
            lock (DiagnosticFileLock)
            {
                System.IO.File.AppendAllText(DiagnosticFilePath, sb.ToString());
            }
        }
    }

    private static void AppendTypeDefinitionDiagnostics(IAssemblyManager manager, ScriptIdentifier id, StringBuilder sb)
    {
        try
        {
            TypeDefinition type = manager.GetTypeDefinition(id);
            sb.AppendLine($"  TypeDefinition: {type.FullName}");
            sb.AppendLine($"    BaseType   : {type.BaseType?.FullName ?? "<null>"}");
            sb.AppendLine($"    IsAbstract : {type.IsAbstract}  IsInterface: {type.IsInterface}  IsSealed: {type.IsSealed}");
            sb.AppendLine($"    Fields ({type.Fields.Count}):");
            foreach (FieldDefinition field in type.Fields)
            {
                sb.AppendLine($"      {field.Name} : {field.Signature?.FieldType?.FullName ?? "<unknown>"} " +
                    $"[public={field.IsPublic}, static={field.IsStatic}, initOnly={field.IsInitOnly}, " +
                    $"notSerialized={HasAttribute(field, "NonSerializedAttribute")}, serializeField={HasAttribute(field, "SerializeField")}]");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  TypeDefinition diagnostic failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool HasAttribute(FieldDefinition field, string simpleName)
    {
        foreach (CustomAttribute attr in field.CustomAttributes)
        {
            if (attr.Constructor?.DeclaringType?.FullName?.Contains(simpleName, StringComparison.Ordinal) == true)
            {
                return true;
            }
        }
        return false;
    }
}
