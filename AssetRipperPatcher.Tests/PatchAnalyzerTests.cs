using System.Collections.Immutable;
using AssetRipperPatcher.Patching;
using AssetRipperPatcher.Tools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace AssetRipperPatcher.Tests;

/// <summary>
/// 分析器回归测试：把设计文档第 7 节的变异验证固化成常驻用例。
///
/// 这些用例存在的理由很具体——访问器诊断（HRP011/HRP012）曾因为把
/// <c>VariableDeclaratorSyntax</c> 当成 attribute 的祖先而整段失效，
/// 手工变异验证发现不了这种「规则永远不会触发」的退化，只有真跑一遍分析器才行。
/// </summary>
public class PatchAnalyzerTests
{
    private readonly ITestOutputHelper _output;

    public PatchAnalyzerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // using 必须整体放在最前面，所以把它们和 Fake 目标类型一起放进 boilerplate；
    // 每个用例只提供补丁类。
    private const string TargetBoilerplate = """
        using System.Collections.Generic;
        using System.Linq;
        using HarmonyLib;
        using AssetRipperPatcher.Patching;
        using Fake;

        namespace Fake
        {
            public class Target
            {
                private void TwoArgs(int a, string b) { }
                private void OneArg(int a) { }
                protected string Name { get; set; } = "";
                private int count;

                // nameof 绑定要求成员对测试源码可见，所以另备一组 public 成员。
                public void PublicTwo(int a, string b) { }
                public int Counter;
                public string Label { get; set; } = "";
            }
        }
        """;

    /// <summary>变异 3：目标名不存在 → HRP001。</summary>
    [Fact]
    public async Task Missing_target_member_reports_HRP001()
    {
        List<string> diagnostics = await AnalyzeAsync("""
            internal static class Patches
            {
                [PatchMethod(typeof(Target), "Exportt")]
                public static void Post(Target __instance) { }
            }
            """);

        Assert.Contains(diagnostics, d => d.StartsWith("HRP001", StringComparison.Ordinal));
    }

    /// <summary>变异 2：OverrideIndex 越界 → HRP002，且消息列出候选。</summary>
    [Fact]
    public async Task OverrideIndex_out_of_range_reports_HRP002()
    {
        List<string> diagnostics = await AnalyzeAsync("""
            internal static class Patches
            {
                [PatchMethod(typeof(Target), "OneArg", OverrideIndex = 99, PatchType = PatchType.Postfix)]
                public static void Post(Target __instance, int a) { }
            }
            """);

        string message = Assert.Single(diagnostics, d => d.StartsWith("HRP002", StringComparison.Ordinal));
        Assert.Contains("[0]", message);
    }

    /// <summary>变异 1：挂钩补丁漏掉目标参数 → HRP003（抓上游加参/删参）。</summary>
    [Fact]
    public async Task Hooked_patch_missing_target_parameter_reports_HRP003()
    {
        List<string> diagnostics = await AnalyzeAsync("""
            internal static class Patches
            {
                [PatchMethod(typeof(Target), "TwoArgs", PatchType = PatchType.Prefix)]
                public static bool Pre(Target __instance) => true;
            }
            """);

        Assert.Contains(diagnostics, d => d.StartsWith("HRP003", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Contains("\"a\"", StringComparison.Ordinal));
    }

    /// <summary>HRP011：访问器目标不存在。</summary>
    [Fact]
    public async Task Missing_accessor_member_reports_HRP011()
    {
        List<string> diagnostics = await AnalyzeAsync("""
            internal static class Patches
            {
                [AccessProperty(typeof(Target), "NoSuchProperty")]
                private static readonly InstanceProperty<Target, string> Name = new();
            }
            """);

        Assert.Contains(diagnostics, d => d.StartsWith("HRP011", StringComparison.Ordinal));
    }

    /// <summary>HRP011：成员存在但种类不符（字段 vs 属性）。</summary>
    [Fact]
    public async Task Wrong_accessor_kind_reports_HRP011()
    {
        List<string> diagnostics = await AnalyzeAsync("""
            internal static class Patches
            {
                [AccessProperty(typeof(Target), "count")]
                private static readonly InstanceProperty<Target, int> Count = new();
            }
            """);

        Assert.Contains(diagnostics, d => d.StartsWith("HRP011", StringComparison.Ordinal));
    }

    /// <summary>变异 4：泛型实参比成员实际类型更宽（继承关系）→ HRP012。</summary>
    [Fact]
    public async Task Wider_accessor_type_argument_reports_HRP012()
    {
        List<string> diagnostics = await AnalyzeAsync("""
            internal static class Patches
            {
                [AccessProperty(typeof(Target), "Name")]
                private static readonly InstanceProperty<Target, object> Name = new();
            }
            """);

        Assert.Contains(diagnostics, d => d.StartsWith("HRP012", StringComparison.Ordinal));
    }

    /// <summary>正向对照：签名正确的注解不得产生任何 HRP 诊断。</summary>
    [Fact]
    public async Task Correct_annotations_produce_no_diagnostics()
    {
        List<string> diagnostics = await AnalyzeAsync("""
            internal static class Patches
            {
                [PatchMethod(typeof(Target), "TwoArgs", PatchType = PatchType.Postfix)]
                public static void Post(Target __instance, int a, string b) { }

                [AccessProperty(typeof(Target), "Name")]
                private static readonly InstanceProperty<Target, string> Name = new();

                [AccessField(typeof(Target), "count")]
                private static readonly InstanceField<Target, int> Count = new();
            }
            """);

        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// transpiler 返回值：Harmony 只把返回的 object 当 IEnumerable&lt;CodeInstruction&gt; 用，
    /// 所以 List/数组/IReadOnlyList 都是合法返回类型（实测可跑）。
    /// 老实现按「形状精确相等」判定，会把它们全判死——这条就是那个假报的回归。
    /// </summary>
    [Fact]
    public async Task Transpiler_returning_concrete_collection_is_accepted()
    {
        List<string> diagnostics = await AnalyzeAsync("""
            internal static class Patches
            {
                [PatchMethod(typeof(Target), "OneArg", PatchType = PatchType.Transpiler)]
                public static List<CodeInstruction> AsList(IEnumerable<CodeInstruction> instructions) => instructions.ToList();

                [PatchMethod(typeof(Target), "OneArg", PatchType = PatchType.Transpiler)]
                public static CodeInstruction[] AsArray(IEnumerable<CodeInstruction> instructions) => instructions.ToArray();
            }
            """);

        Assert.Empty(diagnostics);
    }

    /// <summary>transpiler 返回 void → HRP003，消息点名要求 IEnumerable&lt;CodeInstruction&gt;。</summary>
    [Fact]
    public async Task Transpiler_returning_void_reports_HRP003()
    {
        List<string> diagnostics = await AnalyzeAsync("""
            internal static class Patches
            {
                [PatchMethod(typeof(Target), "OneArg", PatchType = PatchType.Transpiler)]
                public static void Rip(IEnumerable<CodeInstruction> instructions) { }
            }
            """);

        string message = Assert.Single(diagnostics, d => d.StartsWith("HRP003", StringComparison.Ordinal));
        Assert.Contains("IEnumerable<CodeInstruction>", message);
        Assert.Contains("void", message);
    }

    /// <summary>transpiler 首参不是 IEnumerable&lt;CodeInstruction&gt; → HRP003（运行时是 NRE）。</summary>
    [Fact]
    public async Task Transpiler_with_wrong_first_parameter_reports_HRP003()
    {
        List<string> diagnostics = await AnalyzeAsync("""
            internal static class Patches
            {
                [PatchMethod(typeof(Target), "OneArg", PatchType = PatchType.Transpiler)]
                public static IEnumerable<CodeInstruction> Rip(List<CodeInstruction> instructions) => instructions;
            }
            """);

        Assert.Contains(diagnostics, d => d.StartsWith("HRP003", StringComparison.Ordinal) && d.Contains("首参"));
    }

    /// <summary>
    /// transpiler 不接受 __result / __instance / ___field —— Harmony 只按
    /// instructions[, ILGenerator][, MethodBase] 传参，实测多一个参数就 TargetParameterCountException。
    /// </summary>
    [Fact]
    public async Task Transpiler_with_injected_state_parameter_reports_HRP003()
    {
        List<string> diagnostics = await AnalyzeAsync("""
            internal static class Patches
            {
                [PatchMethod(typeof(Target), "OneArg", PatchType = PatchType.Transpiler)]
                public static IEnumerable<CodeInstruction> Rip(IEnumerable<CodeInstruction> instructions, ref bool __result)
                    => instructions;
            }
            """);

        string message = Assert.Single(diagnostics, d => d.StartsWith("HRP003", StringComparison.Ordinal));
        Assert.Contains("__result", message);
        Assert.Contains("没有返回值可读", message);
    }

    /// <summary>PatchType 值超出枚举范围 → HRP003 明确说值无效，而不是笼统的「返回值不合法」。</summary>
    [Fact]
    public async Task Invalid_patch_type_value_reports_HRP003()
    {
        List<string> diagnostics = await AnalyzeAsync("""
            internal static class Patches
            {
                [PatchMethod(typeof(Target), "OneArg", PatchType = (PatchType)7)]
                public static void Weird(Target __instance, int a) { }
            }
            """);

        string message = Assert.Single(diagnostics, d => d.StartsWith("HRP003", StringComparison.Ordinal));
        Assert.Contains("PatchType 值 7 无效", message);
    }

    /// <summary>
    /// 目标名写错时只报 HRP001，不再级联报参数问题 —— 目标都没解析出来，
    /// 谈参数注入没有意义。消息会显式说明检查已跳过，免得被误当成漏检。
    /// （现实里踩过：把 "Export" 打成 "Export2" 后，看到参数名不对却没有红线。）
    /// </summary>
    [Fact]
    public async Task Missing_target_skips_parameter_check_and_says_so()
    {
        List<string> diagnostics = await AnalyzeAsync("""
            internal static class Patches
            {
                [PatchMethod(typeof(Target), "TwoArgz", PatchType = PatchType.Postfix)]
                public static void Post(Target __instance, int alsoWrong) { }
            }
            """);

        string hrp001 = Assert.Single(diagnostics, d => d.StartsWith("HRP001", StringComparison.Ordinal));
        Assert.Contains("参数注入检查已跳过", hrp001);
        Assert.DoesNotContain(diagnostics, d => d.StartsWith("HRP003", StringComparison.Ordinal));
    }

    /// <summary>
    /// 目标存在但补丁参数名对不上 → HRP003，且诊断位置必须精确落在参数标识符上。
    /// 位置很关键：IDE 靠它把红线画在参数处，画错位置等于没有提示。
    /// </summary>
    [Fact]
    public async Task Wrong_parameter_name_reports_HRP003_on_the_parameter()
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeDiagnosticsAsync("""
            internal static class Patches
            {
                [PatchMethod(typeof(Target), "TwoArgs", PatchType = PatchType.Postfix)]
                public static void Post(Target __instance, int a, string renamed) { }
            }
            """);

        // 两条 HRP003：参数名对不上 + 挂钩补丁没覆盖目标参数 b；这里只验证前者的定位。
        Diagnostic hrp003 = Assert.Single(diagnostics, d => d.Id == "HRP003" && d.GetMessage().Contains("renamed"));
        Assert.Contains("同名参数注入失败", hrp003.GetMessage());

        string sourceText = hrp003.Location.SourceTree!.GetText().ToString();
        string slice = sourceText.Substring(hrp003.Location.SourceSpan.Start, hrp003.Location.SourceSpan.Length);
        Assert.Equal("renamed", slice);
    }

    /// <summary>
    /// nameof 写法的绑定必须与字面量写法等价地参与校验。
    /// 现实故障：全库绑定从 "Export" 改成 nameof(ExportHandler.Export) 之后，分析器因为
    /// 只认字面量而集体失明 —— 参数名写成 gameData2 也没有任何红线。
    /// </summary>
    [Fact]
    public async Task NameOf_binding_still_checks_parameter_names()
    {
        List<string> diagnostics = await AnalyzeAsync("""
            internal static class Patches
            {
                [PatchMethod(typeof(Target), nameof(Target.PublicTwo), PatchType = PatchType.Postfix)]
                public static void Post(Target __instance, int a, string renamed) { }
            }
            """);

        string hrp003 = Assert.Single(diagnostics, d => d.StartsWith("HRP003", StringComparison.Ordinal)
            && d.Contains("renamed", StringComparison.Ordinal));
        Assert.Contains("同名参数注入失败", hrp003);
    }

    /// <summary>nameof 写法的访问器同样要跑 HRP011/HRP012。</summary>
    [Fact]
    public async Task NameOf_binding_still_checks_accessor_types()
    {
        List<string> diagnostics = await AnalyzeAsync("""
            internal static class Patches
            {
                [AccessField(typeof(Target), nameof(Target.Counter))]
                internal static readonly InstanceField<Target, long> Counter = null!;

                // nameof 保证成员存在，能触发的 HRP011 只剩「种类不符」这一支。
                [AccessField(typeof(Target), nameof(Target.Label))]
                internal static readonly InstanceField<Target, string> Label = null!;
            }
            """);

        Assert.Contains(diagnostics, d => d.StartsWith("HRP012", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.StartsWith("HRP011", StringComparison.Ordinal)
            && d.Contains("不是字段", StringComparison.Ordinal));
    }

    /// <summary>nameof 绑定、签名完全对得上 → 零诊断（否则等于把好代码判死）。</summary>
    [Fact]
    public async Task NameOf_binding_on_correct_patch_is_silent()
    {
        List<string> diagnostics = await AnalyzeAsync("""
            internal static class Patches
            {
                [PatchMethod(typeof(Target), nameof(Target.PublicTwo))]
                public static void Post(Target __instance, int a, string b) { }

                [AccessField(typeof(Target), nameof(Target.Counter))]
                internal static readonly InstanceField<Target, int> Counter = null!;
            }
            """);

        Assert.Empty(diagnostics);
    }

    private async Task<ImmutableArray<Diagnostic>> AnalyzeDiagnosticsAsync(string body)
    {
        IEnumerable<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(PatchMethodAttribute).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(HarmonyLib.CodeInstruction).Assembly.Location));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "AnalyzerFacts",
            [CSharpSyntaxTree.ParseText(TargetBoilerplate + "\n" + body, new CSharpParseOptions(LanguageVersion.Preview))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Disable));

        // 源码本身必须能编译，否则拿到的是语法/语义错误而不是分析结果。
        ImmutableArray<Diagnostic> compileErrors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        Assert.True(compileErrors.IsEmpty, "测试源码本身不合法:\n" + string.Join("\n", compileErrors));

        ImmutableArray<Diagnostic> diagnostics = await compilation
            .WithAnalyzers([new PatchAnalyzer()])
            .GetAnalyzerDiagnosticsAsync();

        foreach (Diagnostic diagnostic in diagnostics)
        {
            _output.WriteLine($"{diagnostic.Id}: {diagnostic.GetMessage()}");
        }
        return diagnostics;
    }

    private async Task<List<string>> AnalyzeAsync(string body)
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeDiagnosticsAsync(body);
        return diagnostics.Select(d => $"{d.Id}: {d.GetMessage()}").ToList();
    }
}
