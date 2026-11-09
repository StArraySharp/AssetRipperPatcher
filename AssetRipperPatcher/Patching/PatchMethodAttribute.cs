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

    /// <summary>未显式声明优先级时 <see cref="Priority"/> 的取值。</summary>
    public const int UnsetPriority = int.MinValue;

    /// <summary>
    /// Harmony 优先级，数值越大越先执行。<b>同一目标 + 同一 PatchType 上有多个补丁时必须显式声明</b>，
    /// 否则执行顺序取决于 <c>assembly.GetTypes()</c> 的返回次序（不保证稳定）。
    /// 未声明时沿用 Harmony 默认值 <c>Priority.Normal</c>。
    /// </summary>
    public int Priority { get; set; } = UnsetPriority;

    /// <summary>是否显式声明了 <see cref="Priority"/>。</summary>
    public bool HasPriority => Priority != UnsetPriority;
}

/// <summary>补丁/访问器在启动时绑定失败。直接失败而不是静默跳过。</summary>
public sealed class PatchBindingException : Exception
{
    public PatchBindingException(string message) : base(message) { }
}
