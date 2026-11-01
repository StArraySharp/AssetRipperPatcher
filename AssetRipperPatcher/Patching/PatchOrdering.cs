using System.Reflection;
using System.Text;

namespace AssetRipperPatcher.Patching;

/// <summary>
/// 确定性重载排序（反射侧）。
///
/// .NET 的 GetMethods() 顺序不保证稳定，而编译期分析器（AssetRipperPatcher.Tools，
/// Roslyn 符号侧）数出来的「重载 N」和运行时（本文件）的「重载 N」必须是同一个成员。
/// 两侧必须实现逐字相同的排序键——修改本文件的规则时，必须同步修改分析器侧
/// CanonicalSymbol.cs，并用 AssetRipperPatcher.Tests 的 Harmony 本尊绑定测试兜底。
/// </summary>
internal static class PatchOrdering
{
    /// <summary>
    /// 收集候选集：owner 类型 + 基类链上全部同名方法（".ctor" 时为全部实例构造函数）。
    /// 含 owner 自己声明的私有成员；继承链上只收非私有（与反射 GetMethods 语义一致，
    /// 符号侧同样主动排除「声明类型 ≠ owner 且 Private」的成员）。
    /// override 与基类 virtual 同时在候选集（两侧语义一致）。
    /// </summary>
    public static List<MethodBase> CollectCandidates(Type ownerType, string methodName)
    {
        List<MethodBase> candidates = new();
        HashSet<MethodBase> seen = new();

        if (methodName == ".ctor")
        {
            // 构造函数不继承，只看 owner 自身
            foreach (ConstructorInfo ctor in ownerType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (seen.Add(ctor))
                {
                    candidates.Add(ctor);
                }
            }
            return candidates;
        }

        for (Type? type = ownerType; type is not null; type = type.BaseType)
        {
            const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (MethodInfo method in type.GetMethods(All))
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                {
                    continue;
                }
                // 继承链上的私有成员不可见（反射 GetMethods 天然不含基类私有）
                if (type != ownerType && method.IsPrivate)
                {
                    continue;
                }
                if (seen.Add(method))
                {
                    candidates.Add(method);
                }
            }
        }
        return candidates;
    }

    /// <summary>按排序键排序候选集；返回排序后的列表。</summary>
    public static List<MethodBase> Sort(List<MethodBase> candidates)
    {
        candidates.Sort((a, b) => string.CompareOrdinal(SortKey(a), SortKey(b)));
        return candidates;
    }

    /// <summary>
    /// 排序键：实例/静态（实例在前）+ paramCount + 参数 canonical（";" 连接）+ 返回值 canonical
    /// + 声明类型 canonical。与分析器侧 CanonicalSymbol.SortKey 逐字一致。
    /// join 字符参与比较是有意为之（见设计文档第 2 节 Logger.Log 实测案例）。
    /// OverrideIndex 默认 0：候选唯一时即它自己；多个候选时取确定性排序后的第一个。
    /// </summary>
    public static string SortKey(MethodBase method)
    {
        var parameters = method.GetParameters();
        StringBuilder sb = new();
        // 实例/静态区分：实例方法（"0"）排在静态方法（"1"）之前
        sb.Append(method.IsStatic ? "1" : "0");
        sb.Append('|');
        sb.Append(parameters.Length);
        sb.Append('|');
        for (int i = 0; i < parameters.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(';');
            }
            sb.Append(Canonical(parameters[i].ParameterType));
        }
        sb.Append('|');
        sb.Append(method is ConstructorInfo ? "~ctor" : Canonical((method as MethodInfo)!.ReturnType));
        sb.Append('|');
        sb.Append(Canonical(method.DeclaringType!));
        return sb.ToString();
    }

    /// <summary>
    /// 类型的 canonical 形式。与分析器侧 CanonicalSymbol.Canonical 逐字一致：
    /// byref → element + "&amp;"；array → element + "[" + rank-1 个逗号 + "]"；
    /// pointer → element + "*"；泛型参数 → "!" + position；
    /// named type → (namespace + "." + 嵌套链) + Name(去 `N) + "`" + arity + 构造泛型实参递归。
    /// 可空注解（string? vs string）两侧都不编码。
    /// </summary>
    public static string Canonical(Type type)
    {
        if (type.IsByRef)
        {
            return Canonical(type.GetElementType()!) + "&";
        }
        if (type.IsArray)
        {
            return Canonical(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        }
        if (type.IsPointer)
        {
            return Canonical(type.GetElementType()!) + "*";
        }
        if (type.IsGenericParameter)
        {
            return "!" + type.GenericParameterPosition;
        }

        StringBuilder sb = new();
        if (!string.IsNullOrEmpty(type.Namespace))
        {
            sb.Append(type.Namespace);
            sb.Append('.');
        }
        // 嵌套类型：外层到内层逐级 DeclaringType.Name（同样做 `N 规整）
        Stack<Type> nest = new();
        for (Type? outer = type.DeclaringType; outer is not null; outer = outer.DeclaringType)
        {
            nest.Push(outer);
        }
        while (nest.Count > 0)
        {
            AppendName(sb, nest.Pop());
            sb.Append('.');
        }

        AppendName(sb, type);

        if (type.IsConstructedGenericType)
        {
            Type[] args = type.GetGenericArguments();
            sb.Append('(');
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append(Canonical(args[i]));
            }
            sb.Append(')');
        }
        return sb.ToString();

        static void AppendName(StringBuilder sb, Type t)
        {
            string name = t.Name;
            int backtick = name.IndexOf('`');
            if (backtick >= 0)
            {
                name = name[..backtick];
            }
            // arity 用 GetGenericArguments().Length（未构造泛型为 0）；与符号侧 TypeArguments.Length 对应
            int arity = t.IsGenericType ? t.GetGenericArguments().Length : 0;
            sb.Append(name);
            sb.Append('`');
            sb.Append(arity);
        }
    }

    /// <summary>成员的人类可读签名（错误消息用）。</summary>
    public static string Describe(MethodBase method)
    {
        var parameters = method.GetParameters();
        string parameterList = string.Join(", ", parameters.Select(p =>
            (p.ParameterType.IsByRef ? "ref " : "") + p.ParameterType.Name + " " + p.Name));
        string returnType = method is ConstructorInfo ? "" : DescribeType((method as MethodInfo)!.ReturnType) + " ";
        return $"{returnType}{method.DeclaringType!.Name}.{method.Name}({parameterList})";
    }

    private static string DescribeType(Type type)
    {
        if (type.IsByRef)
        {
            return "ref " + DescribeType(type.GetElementType()!);
        }
        if (type.IsArray)
        {
            return DescribeType(type.GetElementType()!) + "[]";
        }
        return type.Name;
    }
}
