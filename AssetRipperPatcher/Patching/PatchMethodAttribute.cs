namespace AssetRipperPatcher.Patching;

/// <summary>Harmony 补丁种类。</summary>
public enum PatchType
{
    Prefix,
    Postfix,
    Finalizer,
    Transpiler,
}

/// <summary>
/// 标注一个 Harmony 补丁方法。注解是单一事实来源：
/// 运行时由 <see cref="PatchRegistrar"/> 照注解自动注册，
/// 编译期由 AssetRipperPatcher.Tools 分析器照注解校验，两侧写的是同一份声明。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PatchMethodAttribute : Attribute
{
    public PatchMethodAttribute(Type type, string methodName)
    {
        TargetType = type;
        MethodName = methodName;
    }

    /// <summary>目标成员的声明类型。</summary>
    public Type TargetType { get; }

    /// <summary>目标方法名；".ctor" 表示构造函数。</summary>
    public string MethodName { get; }

    /// <summary>
    /// 重载消歧索引。候选集 = 目标类型链上全部同名方法（含私有，及继承的非私有），
    /// 按确定性规则排序（排序键见 PatchOrdering / 分析器侧 CanonicalSymbol——两侧逐字一致）。
    /// </summary>
    public int OverrideIndex { get; set; }

    /// <summary>补丁种类，默认 Postfix。</summary>
    public PatchType PatchType { get; set; } = PatchType.Postfix;
}

/// <summary>补丁/访问器在启动时绑定失败。直接失败而不是静默跳过。</summary>
public sealed class PatchBindingException : Exception
{
    public PatchBindingException(string message) : base(message) { }
}
