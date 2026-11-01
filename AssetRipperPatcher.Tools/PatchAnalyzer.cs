using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AssetRipperPatcher.Tools;

/// <summary>
/// 注解化补丁系统分析器。编译期照注解校验：
/// - HRP001 补丁目标不存在
/// - HRP002 OverrideIndex 越界（消息列出全部候选签名）
/// - HRP003 补丁方法签名与 Harmony 注入规则冲突
/// - HRP004 目标为 public → 可用 nameof 绑定（Info）
/// - HRP011 访问器目标不存在 / 种类不符
/// - HRP012 访问器泛型实参与成员实际类型不符
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PatchAnalyzer : DiagnosticAnalyzer
{
    private const string PatchMethodAttributeFullName = "AssetRipperPatcher.Patching.PatchMethodAttribute";
    private const string AccessFieldAttributeFullName = "AssetRipperPatcher.Patching.AccessFieldAttribute";
    private const string AccessPropertyAttributeFullName = "AssetRipperPatcher.Patching.AccessPropertyAttribute";
    private const string AccessMethodAttributeFullName = "AssetRipperPatcher.Patching.AccessMethodAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Diagnostics.HRP001, Diagnostics.HRP002, Diagnostics.HRP003, Diagnostics.HRP004, Diagnostics.HRP011, Diagnostics.HRP012);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAttribute, SyntaxKind.Attribute);

    }

    // 构建编译默认不导入被引用程序集的 private 成员（MetadataImportOptions.Internal），
    // 而本系统的候选集语义要求全量成员。所有符号解析都走 ShadowView 的全量视图。
    private static SemanticModel GetShadowSemanticModel(SyntaxNodeAnalysisContext context, SyntaxTree tree) =>
        ShadowView.Of(context.Compilation).GetSemanticModel(tree);

    private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
    {
        var attributeSyntax = (AttributeSyntax)context.Node;
        SemanticModel semanticModel = GetShadowSemanticModel(context, attributeSyntax.SyntaxTree);
        ITypeSymbol? attrType = semanticModel.GetTypeInfo(attributeSyntax).Type;
        if (attrType is not INamedTypeSymbol namedAttr || namedAttr is IErrorTypeSymbol)
        {
            return;
        }
        string attrFullName = CanonicalSymbol.FullName(namedAttr);

        switch (attrFullName)
        {
            case PatchMethodAttributeFullName:
                AnalyzePatchMethod(context, semanticModel, attributeSyntax);
                break;
            case AccessFieldAttributeFullName:
                AnalyzeAccessor(context, semanticModel, attributeSyntax, AccessorKind.Field);
                break;
            case AccessPropertyAttributeFullName:
                AnalyzeAccessor(context, semanticModel, attributeSyntax, AccessorKind.Property);
                break;
            case AccessMethodAttributeFullName:
                AnalyzeAccessor(context, semanticModel, attributeSyntax, AccessorKind.Method);
                break;
        }
    }

    private enum AccessorKind { Field, Property, Method }

    // ---------- 实参提取 ----------

    private static (ITypeSymbol? TargetType, string? MemberName, int OverrideIndex, int PatchType) GetAttributeArguments(SyntaxNodeAnalysisContext context, SemanticModel semanticModel, AttributeSyntax attrSyntax)
    {
        ITypeSymbol? targetType = null;
        string? memberName = null;
        int overrideIndex = 0;
        int patchType = (int)PatchTypeKind.Postfix;

        SeparatedSyntaxList<AttributeArgumentSyntax> arguments = attrSyntax.ArgumentList?.Arguments ?? default;
        var positional = arguments.Where(a => a.NameEquals is null).ToList();

        if (positional.Count >= 1 && positional[0].Expression is TypeOfExpressionSyntax typeOf)
        {
            targetType = semanticModel.GetTypeInfo(typeOf.Type).Type;
        }
        if (positional.Count >= 2)
        {
            // 第二个位置实参有两种写法：字面量 `"Export"` 与 `nameof(ExportHandler.Export)`。
            // 两者在语义上都是编译期字符串常量，GetConstantValue 都能解析。
            // 早期实现只认 LiteralExpressionSyntax —— 全库绑定改成 nameof 之后分析器
            // 取不到名字就直接 return，一条诊断都不报（看起来像「分析器没装」）。
            Optional<object?> nameConstant = semanticModel.GetConstantValue(positional[1].Expression);
            if (nameConstant.HasValue && nameConstant.Value is string name)
            {
                memberName = name;
            }
        }
        foreach (AttributeArgumentSyntax argument in arguments)
        {
            if (argument.NameEquals?.Name is IdentifierNameSyntax identifier)
            {
                Optional<object?> constant = semanticModel.GetConstantValue(argument.Expression);
                if (constant.HasValue && constant.Value is int value)
                {
                    if (identifier.Identifier.ValueText == "OverrideIndex")
                    {
                        overrideIndex = value;
                    }
                    else if (identifier.Identifier.ValueText == "PatchType")
                    {
                        patchType = value;
                    }
                }
            }
        }
        return (targetType, memberName, overrideIndex, patchType);
    }

    private static Location? GetStringArgumentLocation(AttributeSyntax attrSyntax)
    {
        AttributeArgumentSyntax? argument = (attrSyntax.ArgumentList?.Arguments ?? default)
            .Where(a => a.NameEquals is null)
            .Skip(1)
            .FirstOrDefault();
        return argument?.Expression.GetLocation();
    }

    /// <summary>第二个位置实参是否已写成 <c>nameof(...)</c>（用于抑制 HRP004）。</summary>
    private static bool IsNameOfBinding(AttributeSyntax attrSyntax) =>
        (attrSyntax.ArgumentList?.Arguments ?? default)
            .Where(a => a.NameEquals is null)
            .Skip(1)
            .FirstOrDefault()?.Expression is InvocationExpressionSyntax invocation
        && invocation.Expression is IdentifierNameSyntax identifier
        && identifier.Identifier.ValueText == "nameof";

    private static Location? GetNamedArgumentLocation(AttributeSyntax attrSyntax, string name) =>
        (attrSyntax.ArgumentList?.Arguments ?? default)
            .Where(a => a.NameEquals?.Name is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == name)
            .Select(a => a.Expression.GetLocation())
            .FirstOrDefault();

    // ---------- PatchMethod ----------

    private static void AnalyzePatchMethod(SyntaxNodeAnalysisContext context, SemanticModel semanticModel, AttributeSyntax attrSyntax)
    {
        (ITypeSymbol? targetType, string? methodName, int overrideIndex, int patchTypeValue) =
            GetAttributeArguments(context, semanticModel, attrSyntax);

        if (targetType is null || targetType is IErrorTypeSymbol || methodName is null)
        {
            return; // typeof(不存在的类型) 由编译器原生 CS0246 报
        }
        var patchType = (PatchTypeKind)patchTypeValue;

        Location stringLocation = GetStringArgumentLocation(attrSyntax) ?? attrSyntax.GetLocation();
        Location indexLocation = GetNamedArgumentLocation(attrSyntax, "OverrideIndex") ?? attrSyntax.GetLocation();

        List<IMethodSymbol> candidates = CanonicalSymbol.CollectCandidates(targetType, methodName);
        if (candidates.Count == 0)
        {
            Report(context, Diagnostics.HRP001, stringLocation, targetType.Name, methodName);
            return;
        }

        if (overrideIndex < 0 || overrideIndex >= candidates.Count)
        {
            List<IMethodSymbol> sorted = CanonicalSymbol.Sort(new List<IMethodSymbol>(candidates));
            string candidateList = string.Join("\n", sorted.Select((c, i) => $"[{i}] {CanonicalSymbol.Describe(c)}"));
            Report(context, Diagnostics.HRP002, indexLocation,
                $"OverrideIndex {overrideIndex} 越界: {targetType.Name}.{methodName} 共 {candidates.Count} 个候选:\n{candidateList}");
            return;
        }

        IMethodSymbol target = CanonicalSymbol.Sort(candidates)[overrideIndex];

        // HRP004：目标存在且为 public → 可用 nameof 绑定（构造函数/私有成员 nameof 不了）
        if (methodName != ".ctor" && !IsNameOfBinding(attrSyntax) && target.DeclaredAccessibility == Accessibility.Public)
        {
            Report(context, Diagnostics.HRP004, stringLocation, targetType.Name, methodName);
        }

        // HRP003：补丁方法签名注入检查
        if (GetPatchMethodSymbol(semanticModel, attrSyntax) is { } patchMethod)
        {
            CheckInjection(context, semanticModel.Compilation, patchMethod, target, patchType);
        }
    }

    private static IMethodSymbol? GetPatchMethodSymbol(SemanticModel semanticModel, AttributeSyntax attrSyntax)
    {
        if (attrSyntax.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() is not { } methodDeclaration)
        {
            return null;
        }
        return semanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;
    }

    private static void CheckInjection(SyntaxNodeAnalysisContext context, Compilation compilation, IMethodSymbol patch, IMethodSymbol target, PatchTypeKind patchType)
    {
        Location methodLocation = patch.DeclaringSyntaxReferences.Length > 0
            ? patch.DeclaringSyntaxReferences[0].GetSyntax().GetFirstToken().GetLocation()
            : Location.None;

        if (!patch.IsStatic)
        {
            Report(context, Diagnostics.HRP003, methodLocation,
                "补丁方法必须为 static", DescribeTargetForMessage(target));
            return;
        }

        // 返回值检查。transpiler 的判定见 IsTranspilerReturn：Harmony 只是把返回的
        // object 当 IEnumerable<CodeInstruction> 用，所以任何可隐式转换到它的类型都行
        // （实测 List<CodeInstruction> / CodeInstruction[] / IReadOnlyList<CodeInstruction> 全部可跑）。
        ITypeSymbol ret = patch.ReturnType;
        string? returnError = patchType switch
        {
            PatchTypeKind.Prefix => ret.SpecialType is SpecialType.System_Void or SpecialType.System_Boolean
                ? null
                : $"prefix 必须返回 void 或 bool（当前: {ret.ToDisplayString()}）",
            PatchTypeKind.Postfix => ret.SpecialType == SpecialType.System_Void
                ? null
                : $"postfix 必须返回 void（当前: {ret.ToDisplayString()}）",
            PatchTypeKind.Finalizer => ret.SpecialType == SpecialType.System_Void || IsType(ret, "System.Exception")
                ? null
                : $"finalizer 必须返回 void 或 Exception（当前: {ret.ToDisplayString()}）",
            PatchTypeKind.Transpiler => IsTranspilerReturn(compilation, ret)
                ? null
                : $"transpiler 必须返回可隐式转换为 IEnumerable<CodeInstruction> 的类型"
                    + $"（通常是 IEnumerable<CodeInstruction>，List/数组亦可）——当前: {ret.ToDisplayString()}",
            _ => $"PatchType 值 {(int)patchType} 无效（0=Prefix, 1=Postfix, 2=Finalizer, 3=Transpiler）",
        };
        if (returnError is not null)
        {
            Report(context, Diagnostics.HRP003, methodLocation, returnError, DescribeTargetForMessage(target));
            return;
        }

        if (patchType == PatchTypeKind.Transpiler)
        {
            CheckTranspilerParameters(context, patch, target);
            return;
        }

        foreach (IParameterSymbol parameter in patch.Parameters)
        {
            // Harmony 允许 prefix/postfix 的前置 MethodBase 参数（__originalMethod）
            if (IsType(parameter.Type, "System.Reflection.MethodBase"))
            {
                continue;
            }

            Location parameterLocation = parameter.Locations.FirstOrDefault() ?? methodLocation;
            string name = parameter.Name;

            if (name == "__instance")
            {
                if (target.IsStatic)
                {
                    Report(context, Diagnostics.HRP003, parameterLocation,
                        "__instance 不能用于静态目标", DescribeTargetForMessage(target));
                }
                else if (!IsImplicitFrom(compilation, target.ContainingType!, parameter.Type))
                {
                    Report(context, Diagnostics.HRP003, parameterLocation,
                        $"__instance 类型 {parameter.Type.ToDisplayString()} 不能从 owner {target.ContainingType!.ToDisplayString()} 赋值",
                        DescribeTargetForMessage(target));
                }
            }
            else if (name == "__result")
            {
                if (parameter.RefKind != RefKind.Ref)
                {
                    Report(context, Diagnostics.HRP003, parameterLocation,
                        "__result 必须是 ref 参数", DescribeTargetForMessage(target));
                }
                else if (target.ReturnsVoid)
                {
                    Report(context, Diagnostics.HRP003, parameterLocation,
                        "__result 不能用于 void 目标", DescribeTargetForMessage(target));
                }
                else if (!IsImplicitFrom(compilation, target.ReturnType, parameter.Type))
                {
                    Report(context, Diagnostics.HRP003, parameterLocation,
                        $"目标返回类型 {target.ReturnType.ToDisplayString()} 不能赋给 __result 类型 {parameter.Type.ToDisplayString()}",
                        DescribeTargetForMessage(target));
                }
            }
            else if (name == "__args")
            {
                if (parameter.Type is not IArrayTypeSymbol array
                    || array.ElementType.SpecialType != SpecialType.System_Object)
                {
                    Report(context, Diagnostics.HRP003, parameterLocation,
                        "__args 必须是 object[]", DescribeTargetForMessage(target));
                }
            }
            else if (IsIndexParameterName(name, out int index))
            {
                // __0..__N：按索引引用目标参数
                if (index >= target.Parameters.Length)
                {
                    Report(context, Diagnostics.HRP003, parameterLocation,
                        $"__{index} 超出目标参数数 {target.Parameters.Length}", DescribeTargetForMessage(target));
                }
                else
                {
                    CheckTargetParameterCompatibility(context, compilation, parameter, target.Parameters[index], parameterLocation);
                }
            }
            else if (name.Length > 3 && name.StartsWith("___", StringComparison.Ordinal))
            {
                // ___field：owner 链上的字段注入
                string fieldName = name.Substring(3);
                IFieldSymbol? field = FindField(target.ContainingType!, fieldName);
                if (field is null)
                {
                    Report(context, Diagnostics.HRP003, parameterLocation,
                        $"owner {target.ContainingType!.Name} 链上不存在字段 \"{fieldName}\"", DescribeTargetForMessage(target));
                }
                else if (!IsImplicitFrom(compilation, field.Type, parameter.Type))
                {
                    Report(context, Diagnostics.HRP003, parameterLocation,
                        $"字段 {fieldName} 类型 {field.Type.ToDisplayString()} 不能赋给 {parameter.Type.ToDisplayString()}",
                        DescribeTargetForMessage(target));
                }
            }
            else
            {
                // 同名参数注入——这是抓上游删参/改型的机制
                IParameterSymbol? targetParameter = target.Parameters
                    .FirstOrDefault(tp => string.Equals(tp.Name, name, StringComparison.Ordinal));
                if (targetParameter is null)
                {
                    Report(context, Diagnostics.HRP003, parameterLocation,
                        $"目标方法没有名为 \"{name}\" 的参数（同名参数注入失败）", DescribeTargetForMessage(target));
                }
                else
                {
                    CheckTargetParameterCompatibility(context, compilation, parameter, targetParameter, parameterLocation);
                }
            }
        }

        // 全覆盖检查：挂钩补丁（带 __instance / __result，即读写目标状态/返回值的）必须
        // 接收目标的全部参数——上游加参/删参/改名都会导致静默注入缺失，立即红线。
        // 纯 spy 补丁（如日志限流，只按名绑定个别参数）不做此要求。
        bool hooked = patch.Parameters.Any(p => p.Name is "__instance" or "__result");
        if (hooked)
        {
            HashSet<string> patchNames = new(patch.Parameters.Select(p => p.Name), StringComparer.Ordinal);
            bool hasArgs = patchNames.Contains("__args");
            for (int i = 0; i < target.Parameters.Length; i++)
            {
                IParameterSymbol targetParameter = target.Parameters[i];
                if (patchNames.Contains(targetParameter.Name) || hasArgs || patchNames.Contains("__" + i))
                {
                    continue;
                }
                Report(context, Diagnostics.HRP003, methodLocation,
                    $"目标参数 \"{targetParameter.Name}\" 未被补丁方法接收（挂钩补丁必须覆盖目标全部参数）",
                    DescribeTargetForMessage(target));
            }
        }
    }

    private static bool IsIndexParameterName(string name, out int index)
    {
        index = -1;
        if (name.Length <= 2 || !name.StartsWith("__", StringComparison.Ordinal))
        {
            return false;
        }
        string digits = name.Substring(2);
        if (digits.Length == 0 || digits.Any(c => c < '0' || c > '9'))
        {
            return false;
        }
        index = int.Parse(digits);
        return true;
    }

    private static void CheckTranspilerParameters(SyntaxNodeAnalysisContext context, IMethodSymbol patch, IMethodSymbol target)
    {
        Location methodLocation = patch.DeclaringSyntaxReferences.Length > 0
            ? patch.DeclaringSyntaxReferences[0].GetSyntax().GetFirstToken().GetLocation()
            : Location.None;

        // 首参必须是 IEnumerable<CodeInstruction>：Harmony 按这个类型把原方法指令传进来。
        // 实测首参写成 List<CodeInstruction> 或 object 会直接 NullReferenceException，
        // 所以这里必须严格按形状检查，不能放宽成「可赋值」。
        if (patch.Parameters.Length == 0 || !IsTranspilerShape(patch.Parameters[0].Type))
        {
            Report(context, Diagnostics.HRP003, methodLocation,
                "transpiler 首参必须是 IEnumerable<CodeInstruction>（Harmony 按该类型传入原方法指令）",
                DescribeTargetForMessage(target));
            return;
        }

        // 其余只允许 ILGenerator / MethodBase：Harmony 按类型注入，顺序和个数都无所谓
        // （实测 MethodBase+ILGenerator 反序、两个 ILGenerator 都能跑）。
        // 而 __instance / __result / __args / ___field 一律不支持——实测直接
        // TargetParameterCountException（Harmony 只按 instructions[, ILGenerator][, MethodBase] 传参）。
        foreach (IParameterSymbol parameter in patch.Parameters.Skip(1))
        {
            if (IsType(parameter.Type, "System.Reflection.Emit.ILGenerator")
                || IsType(parameter.Type, "System.Reflection.MethodBase"))
            {
                continue;
            }

            string name = parameter.Name;
            string reason = name switch
            {
                "__instance" => "transpile 发生在打补丁时，没有运行实例",
                "__result" => "transpiler 产出的是指令序列，没有返回值可读",
                "__args" => "参数形态在指令层面未定型，没有参数数组",
                _ when name.StartsWith("___", StringComparison.Ordinal) => "Harmony 不为 transpiler 做字段注入",
                _ => "transpiler 只接受 instructions 之后的 ILGenerator / MethodBase",
            };
            Report(context, Diagnostics.HRP003,
                parameter.Locations.FirstOrDefault() ?? methodLocation,
                $"transpiler 参数 \"{name}\"（{parameter.Type.ToDisplayString()}）不合法：{reason}",
                DescribeTargetForMessage(target));
        }
    }

    private static void CheckTargetParameterCompatibility(
        SyntaxNodeAnalysisContext context,
        Compilation compilation,
        IParameterSymbol patchParameter,
        IParameterSymbol targetParameter,
        Location location)
    {
        if (patchParameter.RefKind == RefKind.Ref)
        {
            // ref 时要求与目标参数类型精确一致
            if (!SymbolEqualityComparer.Default.Equals(patchParameter.Type, targetParameter.Type))
            {
                Report(context, Diagnostics.HRP003, location,
                    $"ref 参数 \"{patchParameter.Name}\" 类型 {patchParameter.Type.ToDisplayString()} 与目标参数类型 {targetParameter.Type.ToDisplayString()} 不精确一致",
                    DescribeTargetForMessage(patchParameter.ContainingSymbol as IMethodSymbol));
            }
        }
        else if (!IsImplicitFrom(compilation, targetParameter.Type, patchParameter.Type))
        {
            Report(context, Diagnostics.HRP003, location,
                $"目标参数 \"{targetParameter.Name}\" 类型 {targetParameter.Type.ToDisplayString()} 不能赋给补丁参数类型 {patchParameter.Type.ToDisplayString()}",
                DescribeTargetForMessage(patchParameter.ContainingSymbol as IMethodSymbol));
        }
    }

    // ---------- Accessor ----------

    private static void AnalyzeAccessor(SyntaxNodeAnalysisContext context, SemanticModel semanticModel, AttributeSyntax attrSyntax, AccessorKind kind)
    {
        (ITypeSymbol? targetType, string? memberName, int overrideIndex, _) =
            GetAttributeArguments(context, semanticModel, attrSyntax);

        if (targetType is null || targetType is IErrorTypeSymbol || memberName is null)
        {
            return;
        }

        // 声明字段（泛型实参检查需要）。
        // 注意：[Access*] 挂在 FieldDeclaration 的 attribute list 上，VariableDeclarator
        // 是它的子节点而非祖先——必须走 FieldDeclaration.Declaration.Variables 取。
        if (attrSyntax.Ancestors().OfType<FieldDeclarationSyntax>().FirstOrDefault() is not { } fieldDeclaration
            || fieldDeclaration.Declaration.Variables.Count == 0)
        {
            return;
        }
        VariableDeclaratorSyntax declarator = fieldDeclaration.Declaration.Variables[0];
        if (semanticModel.GetDeclaredSymbol(declarator) is not IFieldSymbol declarationField)
        {
            return;
        }
        Location fieldIdentifierLocation = declarator.Identifier.GetLocation();
        Location stringLocation = GetStringArgumentLocation(attrSyntax) ?? attrSyntax.GetLocation();

        // HRP011：目标存在性 + 种类
        ISymbol? member;
        if (kind == AccessorKind.Method)
        {
            List<IMethodSymbol> candidates = CanonicalSymbol.CollectCandidates(targetType, memberName);
            if (candidates.Count == 0)
            {
                Report(context, Diagnostics.HRP011, stringLocation,
                    $"{targetType.Name} 链上不存在名为 \"{memberName}\" 的方法");
                return;
            }
            if (overrideIndex < 0 || overrideIndex >= candidates.Count)
            {
                List<IMethodSymbol> sorted = CanonicalSymbol.Sort(new List<IMethodSymbol>(candidates));
                string candidateList = string.Join("\n", sorted.Select((c, i) => $"[{i}] {CanonicalSymbol.Describe(c)}"));
                Report(context, Diagnostics.HRP002,
                    GetNamedArgumentLocation(attrSyntax, "OverrideIndex") ?? attrSyntax.GetLocation(),
                    $"OverrideIndex {overrideIndex} 越界: {targetType.Name}.{memberName} 共 {candidates.Count} 个候选:\n{candidateList}");
                return;
            }
            member = CanonicalSymbol.Sort(candidates)[overrideIndex];
        }
        else
        {
            member = FindInChain(targetType, memberName,
                kind == AccessorKind.Field ? (ISymbol s) => s is IFieldSymbol : (ISymbol s) => s is IPropertySymbol);
            if (member is null)
            {
                // 区分「完全没有」与「种类不符」
                ISymbol? anyMember = FindInChain(targetType, memberName, _ => true);
                string message = anyMember is null
                    ? $"{targetType.Name} 链上不存在名为 \"{memberName}\" 的成员"
                    : $"{targetType.Name}.{memberName} 是 {KindName(anyMember)}，不是{KindNameOf(kind)}";
                Report(context, Diagnostics.HRP011, stringLocation, message);
                return;
            }
        }

        // HRP012：包装类泛型实参 vs 成员实际类型
        if (declarationField.Type is not INamedTypeSymbol wrapper || wrapper.TypeArguments.Length == 0)
        {
            return; // 声明类型不是五个包装类之一（或不带泛型），跳过
        }
        string wrapperName = wrapper.Name;
        Compilation compilation = semanticModel.Compilation;

        bool wantsInstance = wrapperName.StartsWith("Instance", StringComparison.Ordinal);
        if (member.IsStatic == wantsInstance)
        {
            Report(context, Diagnostics.HRP011, stringLocation,
                $"{targetType.Name}.{memberName} 是{(member.IsStatic ? "静态" : "实例")}成员，与包装类 {wrapperName} 不符");
            return;
        }

        ITypeSymbol? ownerArg = wrapper.TypeArguments.Length >= 2 ? wrapper.TypeArguments[0] : null;
        ITypeSymbol valueArg = wrapper.TypeArguments[wrapper.TypeArguments.Length - 1];

        // owner 检查：TOwner 必须是成员声明类型本身或它的派生类型。
        // 反向（TOwner 是声明类型的基类）虽然泛型能编译，但运行时会拿非目标实例
        // 反射调用而抛 TargetException，必须在编译期拦住。
        if (ownerArg is not null && wantsInstance)
        {
            ITypeSymbol declaringType = member.ContainingType!;
            if (!IsImplicitFrom(compilation, ownerArg, declaringType))
            {
                Report(context, Diagnostics.HRP012, fieldIdentifierLocation,
                    $"TOwner 泛型实参 {ownerArg.ToDisplayString()} 必须是成员声明类型 {declaringType.ToDisplayString()} 或其派生类型");
                return;
            }
        }

        // 值类型检查：字段/属性/返回值 → TValue/TResult
        ITypeSymbol valueType = kind switch
        {
            AccessorKind.Field => ((IFieldSymbol)member).Type,
            AccessorKind.Property => ((IPropertySymbol)member).Type,
            AccessorKind.Method => ((IMethodSymbol)member).ReturnType,
            _ => throw new InvalidOperationException(),
        };
        // 要求精确一致，而不是「可赋值」：访问器是某个具体成员的强类型视图，
        // 声明得比成员实际类型更宽（如成员是 FullConfiguration 却写 CoreConfiguration）
        // 属于对类型撒谎——上游一旦收窄/换签名，这里不会有任何编译期反应。
        if (!SymbolEqualityComparer.Default.Equals(valueArg, valueType))
        {
            string hint = IsImplicitFrom(compilation, valueType, valueArg) ? "（比成员实际类型宽）" : string.Empty;
            Report(context, Diagnostics.HRP012, fieldIdentifierLocation,
                $"{wrapperName} 值泛型实参 {valueArg.ToDisplayString()}{hint} 必须与成员实际类型 {valueType.ToDisplayString()} 精确一致");
        }
    }

    // ---------- 工具方法 ----------

    private static void Report(SyntaxNodeAnalysisContext context, DiagnosticDescriptor descriptor, Location location, params object?[] args)
    {
        context.ReportDiagnostic(Diagnostic.Create(descriptor, location, args));
    }

    private static bool IsImplicitFrom(Compilation compilation, ITypeSymbol from, ITypeSymbol to) =>
        compilation.ClassifyCommonConversion(from, to).IsImplicit;

    private static bool IsType(ITypeSymbol type, string fullName) =>
        CanonicalSymbol.FullName(type) == fullName;

    private static bool IsTranspilerShape(ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
        && named.TypeArguments.Length == 1
        && IsType(named.TypeArguments[0], "HarmonyLib.CodeInstruction");

    /// <summary>
    /// transpiler 返回值判定。Harmony 只是把返回的 object 当 <c>IEnumerable&lt;CodeInstruction&gt;</c>
    /// 去枚举，所以判据是「能隐式转换到它」，而不是形状精确相等——实测
    /// <c>List&lt;CodeInstruction&gt;</c>、<c>CodeInstruction[]</c>、<c>IReadOnlyList&lt;CodeInstruction&gt;</c>
    /// 三种返回类型都能正常跑；老实现只认精确形状，会把它们全判死（假报）。
    /// </summary>
    private static bool IsTranspilerReturn(Compilation compilation, ITypeSymbol ret)
    {
        if (IsTranspilerShape(ret))
        {
            return true; // 常见写法的快速路径
        }
        INamedTypeSymbol? codeInstruction = compilation.GetTypeByMetadataName("HarmonyLib.CodeInstruction");
        INamedTypeSymbol enumerable = compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T);
        if (codeInstruction is null || enumerable.TypeKind == TypeKind.Error)
        {
            return false;
        }
        return IsImplicitFrom(compilation, ret, enumerable.Construct(codeInstruction));
    }

    private static IFieldSymbol? FindField(ITypeSymbol owner, string name)
    {
        for (ITypeSymbol? type = owner; type is not null; type = type.BaseType)
        {
            foreach (ISymbol member in type.GetMembers(name))
            {
                if (member is IFieldSymbol field)
                {
                    return field;
                }
            }
        }
        return null;
    }

    private static ISymbol? FindInChain(ITypeSymbol owner, string name, Func<ISymbol, bool> predicate)
    {
        for (ITypeSymbol? type = owner; type is not null; type = type.BaseType)
        {
            ISymbol? found = type.GetMembers(name).FirstOrDefault(predicate);
            if (found is not null)
            {
                return found;
            }
        }
        return null;
    }

    private static string KindName(ISymbol symbol) => symbol switch
    {
        IFieldSymbol => "字段",
        IPropertySymbol => "属性",
        IMethodSymbol => "方法",
        _ => symbol.Kind.ToString(),
    };

    private static string KindNameOf(AccessorKind kind) => kind switch
    {
        AccessorKind.Field => "字段",
        AccessorKind.Property => "属性",
        AccessorKind.Method => "方法",
        _ => "?",
    };

    private static string? DescribeTargetForMessage(IMethodSymbol? target) =>
        target is null ? null : $"目标签名: {CanonicalSymbol.Describe(target)}";

    /// <summary>PatchType 枚举的字面量副本（分析器不引用主工程）。</summary>
    private enum PatchTypeKind
    {
        Prefix = 0,
        Postfix = 1,
        Finalizer = 2,
        Transpiler = 3,
    }
}

/// <summary>诊断描述符。</summary>
internal static class Diagnostics
{
    public static readonly DiagnosticDescriptor HRP001 = new(
        id: "HRP001",
        title: "补丁目标不存在",
        messageFormat: "[PatchMethod(typeof({0}), \"{1}\")] 目标类型链上不存在该名成员（目标未解析，参数注入检查已跳过）",
        category: "AssetRipperPatcher.Patching",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HRP002 = new(
        id: "HRP002",
        title: "OverrideIndex 越界",
        messageFormat: "{0}",
        category: "AssetRipperPatcher.Patching",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HRP003 = new(
        id: "HRP003",
        title: "补丁方法签名与 Harmony 注入规则冲突",
        messageFormat: "{0}{1}",
        category: "AssetRipperPatcher.Patching",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HRP004 = new(
        id: "HRP004",
        title: "可改用 nameof 绑定",
        messageFormat: "[PatchMethod(typeof({0}), \"{1}\")] 目标存在且为 public，可用 nameof({0}.{1}) 绑定",
        category: "AssetRipperPatcher.Patching",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HRP011 = new(
        id: "HRP011",
        title: "访问器目标不存在或种类不符",
        messageFormat: "{0}",
        category: "AssetRipperPatcher.Patching",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HRP012 = new(
        id: "HRP012",
        title: "访问器泛型实参与成员实际类型不符",
        messageFormat: "{0}",
        category: "AssetRipperPatcher.Patching",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
