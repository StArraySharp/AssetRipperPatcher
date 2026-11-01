using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace AssetRipperPatcher.Tools;

/// <summary>
/// 确定性重载排序（Roslyn 符号侧）。
///
/// 必须与主工程 Patching/PatchOrdering.cs（反射侧）实现逐字相同的排序键——
/// 修改本文件的规则时，必须同步修改 PatchOrdering.cs，并用
/// AssetRipperPatcher.Tests 的 Harmony 本尊绑定测试兜底。
/// </summary>
internal static class CanonicalSymbol
{
    /// <summary>
    /// 收集候选集（符号侧）：owner 类型 + 基类链上全部同名 Ordinary 方法
    /// （".ctor" 时为全部实例构造函数）。含 owner 自己声明的私有成员；
    /// 继承链上只收非私有（排除「声明类型 ≠ owner 且 Private」，与反射语义一致）。
    /// </summary>
    public static List<IMethodSymbol> CollectCandidates(ITypeSymbol ownerType, string methodName)
    {
        List<IMethodSymbol> candidates = new();

        if (methodName == ".ctor")
        {
            // 构造函数不继承，只看 owner 自身
            foreach (IMethodSymbol ctor in ownerType.GetMembers().OfType<IMethodSymbol>())
            {
                if (ctor.MethodKind == MethodKind.Constructor && !ctor.IsStatic)
                {
                    candidates.Add(ctor);
                }
            }
            return candidates;
        }

        for (ITypeSymbol? type = ownerType; type is not null; type = type.BaseType)
        {
            foreach (ISymbol member in type.GetMembers(methodName))
            {
                if (member is not IMethodSymbol method || method.MethodKind != MethodKind.Ordinary)
                {
                    continue;
                }
                if (!SymbolEqualityComparer.Default.Equals(type, ownerType)
                    && method.DeclaredAccessibility == Accessibility.Private)
                {
                    continue;
                }
                candidates.Add(method);
            }
        }
        return candidates;
    }

    /// <summary>按排序键排序；返回新列表。</summary>
    public static List<IMethodSymbol> Sort(List<IMethodSymbol> candidates)
    {
        candidates.Sort((a, b) => string.CompareOrdinal(SortKey(a), SortKey(b)));
        return candidates;
    }

    /// <summary>
    /// 排序键：实例/静态（实例在前）+ paramCount + 参数 canonical（";" 连接，byref 加 "&amp;"）
    /// + 返回值 canonical + 声明类型 canonical。与 PatchOrdering.SortKey 逐字一致。
    /// OverrideIndex 默认 0：候选唯一时即它自己。
    /// </summary>
    public static string SortKey(IMethodSymbol method)
    {
        StringBuilder sb = new();
        // 实例/静态区分：实例方法（"0"）排在静态方法（"1"）之前
        sb.Append(method.IsStatic ? "1" : "0");
        sb.Append('|');
        sb.Append(method.Parameters.Length);
        sb.Append('|');
        for (int i = 0; i < method.Parameters.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(';');
            }
            sb.Append(Canonical(method.Parameters[i].Type));
            if (method.Parameters[i].RefKind != RefKind.None)
            {
                sb.Append('&');
            }
        }
        sb.Append('|');
        sb.Append(method.MethodKind == MethodKind.Constructor ? "~ctor" : Canonical(method.ReturnType));
        sb.Append('|');
        sb.Append(Canonical(method.ContainingType!));
        return sb.ToString();
    }

    /// <summary>
    /// 类型的 canonical 形式，与 PatchOrdering.Canonical 逐字一致：
    /// 指针 → "*"；数组 → "[]"（rank-1 个逗号）；泛型参数 → "!" + Ordinal；
    /// named type → (namespace + "." + 嵌套链) + Name + "`" + arity + 构造泛型实参递归。
    /// 可空注解（string? vs string）两侧都不编码。
    /// 注：byref 在符号侧没有 ITypeSymbol 形态，由参数级 RefKind 编码（见 SortKey）。
    /// </summary>
    public static string Canonical(ITypeSymbol type)
    {
        switch (type)
        {
            case IPointerTypeSymbol pointer:
                return Canonical(pointer.PointedAtType) + "*";
            case IArrayTypeSymbol array:
                return Canonical(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]";
            case ITypeParameterSymbol typeParameter:
                return "!" + typeParameter.Ordinal;
        }

        if (type is not INamedTypeSymbol named)
        {
            return type.ToDisplayString();
        }

        StringBuilder sb = new();
        string ns = NamespaceOf(named);
        if (ns.Length > 0)
        {
            sb.Append(ns);
            sb.Append('.');
        }
        Stack<INamedTypeSymbol> nest = new();
        for (INamedTypeSymbol? outer = named.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            nest.Push(outer);
        }
        while (nest.Count > 0)
        {
            AppendName(sb, nest.Pop());
            sb.Append('.');
        }

        AppendName(sb, named);

        if (named.TypeArguments.Length > 0)
        {
            sb.Append('(');
            for (int i = 0; i < named.TypeArguments.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append(Canonical(named.TypeArguments[i]));
            }
            sb.Append(')');
        }
        return sb.ToString();
    }

    private static string NamespaceOf(INamedTypeSymbol named)
    {
        StringBuilder sb = new();
        for (INamespaceSymbol? ns = named.ContainingNamespace; ns is { IsGlobalNamespace: false }; ns = ns.ContainingNamespace)
        {
            if (sb.Length > 0)
            {
                sb.Insert(0, '.');
            }
            sb.Insert(0, ns.Name);
        }
        return sb.ToString();
    }

    private static void AppendName(StringBuilder sb, INamedTypeSymbol t)
    {
        // 符号侧 Name 无 `N 后缀；arity 用 TypeArguments.Length（未构造时 0），与反射侧对应
        sb.Append(t.Name);
        sb.Append('`');
        sb.Append(t.TypeArguments.Length);
    }

    /// <summary>类型的全名（namespace + 嵌套链 + Name），无 `N、无实参。</summary>
    public static string FullName(ITypeSymbol type)
    {
        StringBuilder sb = new();
        string ns = NamespaceOf((INamedTypeSymbol)type);
        if (ns.Length > 0)
        {
            sb.Append(ns);
            sb.Append('.');
        }
        Stack<ITypeSymbol> nest = new();
        for (ITypeSymbol? outer = type.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            nest.Push(outer);
        }
        while (nest.Count > 0)
        {
            sb.Append(nest.Pop().Name);
            sb.Append('.');
        }
        sb.Append(type.Name);
        return sb.ToString();
    }

    /// <summary>成员的人类可读签名（错误消息用，供 HRP002 列出全部候选）。</summary>
    public static string Describe(IMethodSymbol method)
    {
        string parameterList = string.Join(", ", method.Parameters.Select(p =>
            (p.RefKind == RefKind.Ref ? "ref " : p.RefKind == RefKind.Out ? "out " : "") + p.Type.Name + " " + p.Name));
        string returnType = method.MethodKind == MethodKind.Constructor ? "" : method.ReturnType.Name + " ";
        return $"{returnType}{method.ContainingType.Name}.{method.Name}({parameterList})";
    }
}
