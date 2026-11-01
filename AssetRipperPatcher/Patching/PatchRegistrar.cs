using System.Reflection;
using HarmonyLib;

namespace AssetRipperPatcher.Patching;

/// <summary>
/// 启动时照 [PatchMethod] 注解自动注册全部补丁。fail fast：
/// 任何绑定失败抛 <see cref="PatchBindingException"/>，永不静默跳过。
/// </summary>
public static class PatchRegistrar
{
    public static void RegisterAll(Harmony harmony) => RegisterAll(harmony, Assembly.GetExecutingAssembly());

    public static void RegisterAll(Harmony harmony, Assembly assembly)
    {
        int applied = 0;
        foreach (PatchReport report in DescribeAll(assembly))
        {
            if (report.Error is not null || report.Target is null)
            {
                throw new PatchBindingException(
                    $"[{report.Patch.DeclaringType!.Name}.{report.Patch.Name}] {report.Error}");
            }

            HarmonyMethod harmonyMethod = new(report.Patch);
            switch (report.Attr.PatchType)
            {
                case PatchType.Prefix:
                    harmony.Patch(report.Target, prefix: harmonyMethod);
                    break;
                case PatchType.Postfix:
                    harmony.Patch(report.Target, postfix: harmonyMethod);
                    break;
                case PatchType.Finalizer:
                    harmony.Patch(report.Target, finalizer: harmonyMethod);
                    break;
                case PatchType.Transpiler:
                    harmony.Patch(report.Target, transpiler: harmonyMethod);
                    break;
                default:
                    throw new PatchBindingException($"Unknown PatchType: {report.Attr.PatchType}");
            }
            applied++;
            Console.WriteLine($"[Patch] {DescribeTarget(report.Target)} <- {report.Patch.DeclaringType!.Name}.{report.Patch.Name} ({report.Attr.PatchType})");
        }
        Console.WriteLine($"[PatchRegistrar] {applied} patch(es) applied.");
    }

    /// <summary>
    /// 内省 API：返回每个补丁的解析结果（供测试与诊断输出，不抛异常）。
    /// </summary>
    public static IReadOnlyList<PatchReport> DescribeAll(Assembly? assembly = null)
    {
        assembly ??= Assembly.GetExecutingAssembly();
        List<PatchReport> reports = new();

        foreach (Type type in assembly.GetTypes())
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                PatchMethodAttribute? attr = method.GetCustomAttribute<PatchMethodAttribute>();
                if (attr is null)
                {
                    continue;
                }
                reports.Add(Describe(method, attr));
            }
        }
        return reports;
    }

    private static PatchReport Describe(MethodInfo patch, PatchMethodAttribute attr)
    {
        if (!patch.IsStatic)
        {
            return new PatchReport(patch, attr, null, "补丁方法必须为 static");
        }

        List<MethodBase> candidates;
        try
        {
            candidates = PatchOrdering.Sort(PatchOrdering.CollectCandidates(attr.TargetType, attr.MethodName));
        }
        catch (Exception ex)
        {
            return new PatchReport(patch, attr, null, $"枚举候选失败: {ex.Message}");
        }

        if (candidates.Count == 0)
        {
            return new PatchReport(patch, attr, null,
                $"目标不存在: {attr.TargetType.FullName}.{attr.MethodName}（类型链上无该名成员）");
        }

        if (attr.OverrideIndex < 0 || attr.OverrideIndex >= candidates.Count)
        {
            string candidateList = string.Join("\n  ",
                candidates.Select((c, i) => $"  [{i}] {PatchOrdering.Describe(c)}"));
            return new PatchReport(patch, attr, null,
                $"OverrideIndex {attr.OverrideIndex} 越界: {attr.TargetType.Name}.{attr.MethodName} 共 {candidates.Count} 个候选:\n{candidateList}");
        }

        return new PatchReport(patch, attr, candidates[attr.OverrideIndex], null);
    }

    private static string DescribeTarget(MethodBase target)
    {
        string declaring = target.DeclaringType?.Name ?? "?";
        string parameters = string.Join(", ", target.GetParameters().Select(p => p.ParameterType.Name));
        return target is ConstructorInfo
            ? $"{declaring}..ctor({parameters})"
            : $"{declaring}.{target.Name}({parameters})";
    }
}

/// <summary>单个补丁的解析结果。</summary>
public sealed record PatchReport(MethodInfo Patch, PatchMethodAttribute Attr, MethodBase? Target, string? Error);
