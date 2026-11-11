using System.Reflection;
using HarmonyLib;
using AssetRipper.Import.AssetCreation;
using AssetRipperPatcher.Patches;

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
        IReadOnlyList<PatchReport> reports = DescribeAll(assembly);
        EnsureUnambiguousOrder(reports, static message => new PatchBindingException(message));

        int applied = 0;
        foreach (PatchReport report in reports)
        {
            if (report.Error is not null || report.Target is null)
            {
                throw new PatchBindingException(
                    $"[{report.Patch.DeclaringType!.Name}.{report.Patch.Name}] {report.Error}");
            }

            // 未显式声明优先级的补丁沿用 Harmony 默认值（-1 → Priority.Normal）。
            HarmonyMethod harmonyMethod = new(report.Patch, report.Attr.HasPriority ? report.Attr.Priority : -1);
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
            Console.WriteLine($"[Patch] {DescribeTarget(report.Target)} <- {report.Patch.DeclaringType!.Name}.{report.Patch.Name} " +
                $"({report.Attr.PatchType}, priority={(report.Attr.HasPriority ? report.Attr.Priority.ToString() : "default")})");
        }
        Console.WriteLine($"[PatchRegistrar] {applied} patch(es) applied.");
    }

            /// <summary>
            /// 注册注解表达不了的补丁：目标成员是私有嵌套类型里的重写方法
            /// （<see cref="TypeTreeObject"/> 的 <c>ReadRelease</c>/<c>ReadEditor</c>），
            /// 无法用 <see cref="TypeInfo.GetMethod(string)"/> 在编译期命名，只能按反射签名绑定。
            /// 与注解补丁走同一套"失败即抛"的约定。
            /// </summary>
            public static void RegisterTypeTreeReaders(Harmony harmony)
            {
                Type treeObject = typeof(TypeTreeObject);
                foreach (Type nested in treeObject.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    foreach (string name in new[] { "ReadRelease", "ReadEditor" })
                    {
                        MethodInfo? target = nested.GetMethod(name,
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                        if (target is null)
                        {
                            continue;
                        }
                        bool editor = name is "ReadEditor";
                        MethodInfo patch = editor
                            ? typeof(TypeTreeReadPatch).GetMethod(nameof(TypeTreeReadPatch.LenientReadEditor))!
                            : typeof(TypeTreeReadPatch).GetMethod(nameof(TypeTreeReadPatch.LenientReadRelease))!;
                        harmony.Patch(target, prefix: new HarmonyMethod(patch));
                        Console.WriteLine($"[Patch] {nested.Name}.{name} <- TypeTreeReadPatch.{patch.Name} (Prefix)");
                    }
                }
            }

    /// <summary>
    /// 同一目标 + 同一 <see cref="PatchType"/> 上挂了多个补丁时，执行顺序完全由 Harmony 的
    /// <c>priority</c> 决定（priority 相同时才退回到注册顺序）。而注册顺序来自
    /// <c>assembly.GetTypes()</c>，本就不保证稳定。所以这种组合下**必须**每个补丁都显式声明
    /// <see cref="PatchMethodAttribute.Priority"/>，否则这里直接报错。
    /// </summary>
    /// <param name="reports">待检查的补丁解析结果（解析失败的条目会被忽略）。</param>
    /// <param name="errorFactory">把错误消息包装成目标异常类型（注册期抛绑定异常、测试期抛断言）。</param>
    public static void EnsureUnambiguousOrder(IReadOnlyList<PatchReport> reports, Func<string, Exception> errorFactory)
    {
        IEnumerable<IGrouping<(MethodBase Target, PatchType Kind), PatchReport>> ambiguous = reports
            .Where(static r => r.Error is null && r.Target is not null)
            .GroupBy(static r => (r.Target!, r.Attr.PatchType))
            .Where(static g => g.Count() > 1 && g.Any(static r => !r.Attr.HasPriority));

        foreach (IGrouping<(MethodBase Target, PatchType Kind), PatchReport> group in ambiguous)
        {
            string members = string.Join(", ", group.Select(static r =>
                $"{r.Patch.DeclaringType!.Name}.{r.Patch.Name}" +
                (r.Attr.HasPriority ? $" (Priority={r.Attr.Priority})" : " (Priority 未声明)")));
            throw errorFactory(
                $"{DescribeTarget(group.Key.Target)} 上有 {group.Count()} 个 {group.Key.Kind} 补丁，" +
                $"执行顺序取决于 assembly.GetTypes() 的返回次序——每个补丁都必须显式声明 [PatchMethod(..., Priority = ...)]：" +
                $"\n  {members}");
        }
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
