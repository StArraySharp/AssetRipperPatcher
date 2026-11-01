using System.Reflection;
using System.Runtime.CompilerServices;

namespace AssetRipperPatcher.Patching;

/// <summary>声明在静态只读字段上：类型安全的实例字段反射访问器。</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class AccessFieldAttribute(Type type, string fieldName) : Attribute
{
    public Type TargetType { get; } = type;
    public string MemberName { get; } = fieldName;
}

/// <summary>声明在静态只读字段上：类型安全的实例属性反射访问器。</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class AccessPropertyAttribute(Type type, string propertyName) : Attribute
{
    public Type TargetType { get; } = type;
    public string MemberName { get; } = propertyName;
}

/// <summary>声明在静态只读字段上：类型安全的（私有）方法反射访问器。OverrideIndex 与 PatchMethod 同一排序规则。</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class AccessMethodAttribute(Type type, string methodName) : Attribute
{
    public Type TargetType { get; } = type;
    public string MemberName { get; } = methodName;

    /// <summary>重载消歧索引，与 PatchMethodAttribute.OverrideIndex 同一排序规则。</summary>
    public int OverrideIndex { get; set; }
}

/// <summary>访问器基类。绑定状态供测试断言；绑定失败原因供错误消息。</summary>
public abstract class Accessor
{
    public bool Bound { get; internal set; }

    internal string? BindingError { get; set; }

    internal abstract void Bind(MemberInfo member);
}

/// <summary>实例字段访问器。</summary>
public sealed class InstanceField<TOwner, TValue> : Accessor
{
    private Func<object?, object?>? _getter;
    private Action<object, object?>? _setter;

    public TValue Get(TOwner owner) => (TValue)_getter!(owner)!;

    public void Set(TOwner owner, TValue value) => _setter!(owner!, value);

    internal override void Bind(MemberInfo member)
    {
        FieldInfo field = (FieldInfo)member;
        _getter = field.GetValue;
        _setter = field.SetValue;
        Bound = true;
    }
}

/// <summary>静态字段访问器。</summary>
public sealed class StaticField<TValue> : Accessor
{
    private Func<object?>? _getter;
    private Action<object?>? _setter;

    public TValue Get() => (TValue)_getter()!;

    public void Set(TValue value) => _setter(value);

    internal override void Bind(MemberInfo member)
    {
        FieldInfo field = (FieldInfo)member;
        _getter = () => field.GetValue(null);
        _setter = value => field.SetValue(null, value);
        Bound = true;
    }
}

/// <summary>实例属性访问器。</summary>
public sealed class InstanceProperty<TOwner, TValue> : Accessor
{
    private Func<object, object?>? _getter;
    private Action<object, object?>? _setter;

    public TValue Get(TOwner owner) => (TValue)_getter(owner!)!;

    public void Set(TOwner owner, TValue value) => _setter(owner!, value);

    internal override void Bind(MemberInfo member)
    {
        PropertyInfo property = (PropertyInfo)member;
        MethodInfo? getter = property.GetMethod ?? property.GetGetMethod(true);
        MethodInfo? setter = property.SetMethod ?? property.GetSetMethod(true);
        if (getter is null && setter is null)
        {
            throw new PatchBindingException($"属性 {property.DeclaringType!.Name}.{property.Name} 无可访问的 getter/setter");
        }
        if (getter is not null)
        {
            _getter = owner => getter.Invoke(owner, null);
        }
        if (setter is not null)
        {
            _setter = (owner, value) => setter.Invoke(owner, [value]);
        }
        Bound = true;
    }
}

/// <summary>实例方法访问器（强类型返回值）。</summary>
public sealed class InstanceMethod<TOwner, TResult> : Accessor
{
    private MethodInfo? _method;

    public TResult Invoke(TOwner owner, params object?[]? args) => (TResult)_method!.Invoke(owner, args)!;

    internal override void Bind(MemberInfo member)
    {
        _method = (MethodInfo)member;
        Bound = true;
    }
}

/// <summary>静态方法访问器（强类型返回值）。</summary>
public sealed class StaticMethod<TOwner, TResult> : Accessor
{
    private MethodInfo? _method;

    public TResult Invoke(params object?[]? args) => (TResult)_method!.Invoke(null, args)!;

    internal override void Bind(MemberInfo member)
    {
        _method = (MethodInfo)member;
        Bound = true;
    }
}

/// <summary>
/// 访问器绑定器：程序集一加载（ModuleInitializer）就全量绑定全部 Access* 注解字段。
/// 上游成员没了 → 进程启动即崩，错误消息带「哪个声明字段 + 缺什么」。
/// </summary>
internal static class AccessBinder
{
    [ModuleInitializer]
    internal static void Initialize() => BindAll(typeof(AccessBinder).Assembly);

    public static void BindAll(Assembly assembly)
    {
        const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        foreach (Type type in assembly.GetTypes())
        {
            foreach (FieldInfo field in type.GetFields(All))
            {
                AccessFieldAttribute? fieldAttr = field.GetCustomAttribute<AccessFieldAttribute>();
                AccessPropertyAttribute? propertyAttr = field.GetCustomAttribute<AccessPropertyAttribute>();
                AccessMethodAttribute? methodAttr = field.GetCustomAttribute<AccessMethodAttribute>();
                if (fieldAttr is null && propertyAttr is null && methodAttr is null)
                {
                    continue;
                }

                Accessor accessor = (Accessor)field.GetValue(null)!;
                try
                {
                    if (fieldAttr is not null)
                    {
                        accessor.Bind(ResolveField(fieldAttr));
                    }
                    else if (propertyAttr is not null)
                    {
                        accessor.Bind(ResolveProperty(propertyAttr));
                    }
                    else
                    {
                        accessor.Bind(ResolveMethod(methodAttr!));
                    }
                }
                catch (Exception ex)
                {
                    accessor.BindingError = ex.Message;
                    throw new PatchBindingException(
                        $"访问器绑定失败: {type.Name}.{field.Name} ({accessor.GetType().Name} -> {DescribeAttr(fieldAttr, propertyAttr, methodAttr)}): {ex.Message}");
                }
            }
        }
    }

    private static string DescribeAttr(AccessFieldAttribute? f, AccessPropertyAttribute? p, AccessMethodAttribute? m) =>
        f is not null ? $"{f.TargetType.Name}.{f.MemberName}"
        : p is not null ? $"{p.TargetType.Name}.{p.MemberName}"
        : $"{m!.TargetType.Name}.{m.MemberName}";

    private static MemberInfo ResolveMember(Type ownerType, string memberName)
    {
        // owner 链上找成员（含私有）。找到多个同名时取声明类型最靠近 owner 的。
        for (Type? type = ownerType; type is not null; type = type.BaseType)
        {
            MemberInfo? found = type
                .GetMembers(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .FirstOrDefault(m => string.Equals(m.Name, memberName, StringComparison.Ordinal));
            if (found is not null)
            {
                return found;
            }
        }
        throw new PatchBindingException($"目标不存在: {ownerType.FullName}.{memberName}（类型链上无该名成员）");
    }

    private static FieldInfo ResolveField(AccessFieldAttribute attr)
    {
        MemberInfo member = ResolveMember(attr.TargetType, attr.MemberName);
        if (member is not FieldInfo field)
        {
            throw new PatchBindingException($"{attr.TargetType.Name}.{attr.MemberName} 是 {member.MemberType}，不是字段");
        }
        return field;
    }

    private static PropertyInfo ResolveProperty(AccessPropertyAttribute attr)
    {
        MemberInfo member = ResolveMember(attr.TargetType, attr.MemberName);
        if (member is not PropertyInfo property)
        {
            throw new PatchBindingException($"{attr.TargetType.Name}.{attr.MemberName} 是 {member.MemberType}，不是属性");
        }
        return property;
    }

    private static MethodInfo ResolveMethod(AccessMethodAttribute attr)
    {
        List<MethodInfo> candidates = PatchOrdering.Sort(
                PatchOrdering.CollectCandidates(attr.TargetType, attr.MemberName))
            .OfType<MethodInfo>()
            .ToList();

        if (candidates.Count == 0)
        {
            throw new PatchBindingException($"目标不存在: {attr.TargetType.FullName}.{attr.MemberName}（类型链上无该名方法）");
        }
        if (attr.OverrideIndex < 0 || attr.OverrideIndex >= candidates.Count)
        {
            string candidateList = string.Join("\n  ", candidates.Select((c, i) => $"  [{i}] {PatchOrdering.Describe(c)}"));
            throw new PatchBindingException(
                $"OverrideIndex {attr.OverrideIndex} 越界: {attr.TargetType.Name}.{attr.MemberName} 共 {candidates.Count} 个候选:\n{candidateList}");
        }
        return candidates[attr.OverrideIndex];
    }
}
