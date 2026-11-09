using System.Reflection;
using AssetRipperPatcher.Patching;
using HarmonyLib;
using Xunit;
using Xunit.Abstractions;

namespace AssetRipperPatcher.Tests;

/// <summary>
/// 注解化补丁系统自检（设计文档第 6 节）。
/// </summary>
public class PatchBindingTests
{
    private readonly ITestOutputHelper _output;

    public PatchBindingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static Assembly PatcherAssembly => typeof(AssetRipperPatcher.Patching.PatchRegistrar).Assembly;

    /// <summary>
    /// 测试 1：全部补丁注解可解析，且输出目标完整签名（人工核对 OverrideIndex 是否指对成员）。
    /// </summary>
    [Fact]
    public void All_patch_methods_resolve()
    {
        IReadOnlyList<PatchReport> reports = PatchRegistrar.DescribeAll(PatcherAssembly);
        Assert.NotEmpty(reports);

        foreach (PatchReport report in reports)
        {
            _output.WriteLine($"{report.Patch.DeclaringType!.Name}.{report.Patch.Name} ({report.Attr.PatchType}, OverrideIndex={report.Attr.OverrideIndex})");
            if (report.Error is not null)
            {
                _output.WriteLine($"    ERROR: {report.Error}");
            }
            else
            {
                MethodBase target = report.Target!;
                string parameters = string.Join(", ", target.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
                _output.WriteLine($"    -> {target.DeclaringType!.FullName}.{target.Name}({parameters})");
            }
            Assert.Null(report.Error);
        }
    }

    /// <summary>
    /// 测试 2：用 Harmony 本尊全量绑定——「分析器排序与运行时排序漂移」的兜底。
    /// </summary>
    [Fact]
    public void All_patches_bind_with_real_harmony()
    {
        Harmony harmony = new("test.patchbinding." + Guid.NewGuid().ToString("N"));
        PatchRegistrar.RegisterAll(harmony, PatcherAssembly);

        // 验证每个目标确实挂上了补丁
        foreach (PatchReport report in PatchRegistrar.DescribeAll(PatcherAssembly))
        {
            MethodBase target = report.Target!;
            HarmonyLib.Patches patches = Harmony.GetPatchInfo(target);
            Assert.Contains(harmony.Id, patches.Owners);
            var applied = patches.Prefixes.Concat(patches.Postfixes)
                .Concat(patches.Finalizers).Concat(patches.Transpilers);
            Assert.Contains(applied, p => p.PatchMethod == report.Patch);
        }
    }

    /// <summary>
    /// 运行期与编译期必须一致：注册器也用同一规则拒绝「同目标同 kind 未排序」。
    /// 这条是编译期 HRP005 的运行时兜底——分析器万一没跑，启动时也要炸。
    /// </summary>
    [Fact]
    public void Ambiguous_order_is_rejected_at_registration()
    {
        IReadOnlyList<PatchReport> reports = PatchRegistrar.DescribeAll(PatcherAssembly);
        // 真实补丁集已经全部声明了 Priority，所以这里不该抛。
        PatchRegistrar.EnsureUnambiguousOrder(reports, message => new InvalidOperationException(message));

        PatchReport sample = reports.First(r => r.Attr.PatchType == PatchType.Postfix);
        // 同目标同 kind、未声明 Priority 的两条 → 必须拒绝
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => PatchRegistrar.EnsureUnambiguousOrder([sample, sample],
                message => new InvalidOperationException(message)));
        Assert.Contains("Priority", ex.Message);

        // 两个都显式声明 Priority 就放行
        PatchReport high = sample with { Attr = CloneWithPriority(sample.Attr, 500) };
        PatchReport low = sample with { Attr = CloneWithPriority(sample.Attr, 300) };
        PatchRegistrar.EnsureUnambiguousOrder([high, low], message => new InvalidOperationException(message));

        // 只有一个补丁时，不声明 Priority 也不该报
        PatchRegistrar.EnsureUnambiguousOrder([sample], message => new InvalidOperationException(message));
    }

    private static PatchMethodAttribute CloneWithPriority(PatchMethodAttribute source, int priority)
    {
        return new PatchMethodAttribute(source.TargetType, source.MethodName)
        {
            PatchType = source.PatchType,
            OverrideIndex = source.OverrideIndex,
            Priority = priority,
        };
    }

    /// <summary>
    /// 测试 3：全部访问器在 ModuleInitializer 阶段已绑定成功。
    /// </summary>
    [Fact]
    public void All_accessors_are_bound()
    {
        List<(string Field, Accessor Accessor)> accessors = new();
        foreach (Type type in PatcherAssembly.GetTypes())
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (typeof(Accessor).IsAssignableFrom(field.FieldType))
                {
                    accessors.Add(($"{type.Name}.{field.Name}", (Accessor)field.GetValue(null)!));
                }
            }
        }

        Assert.NotEmpty(accessors);
        foreach ((string field, Accessor accessor) in accessors)
        {
            _output.WriteLine($"{field} ({accessor.GetType().Name}): Bound={accessor.Bound} Error={accessor.BindingError}");
            Assert.True(accessor.Bound, $"{field} 未绑定: {accessor.BindingError}");
        }
    }
}
