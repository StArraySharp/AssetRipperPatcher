namespace AssetRipperPatcher;

/// <summary>
/// 一个 asmdef —— 也就是一个编译产物程序集。
///
/// <para><c>name</c> 既是 <c>.asmdef</c> 里的 <c>name</c> 字段，也是编译出的 DLL 文件名，
/// 正是 AssetRipper 原样复制到 <c>Assets/Plugins</c> 的那个名字。</para>
///
/// <para><c>guid</c> 是该 <c>.asmdef.meta</c> 的 GUID（<c>AssemblyDefinitionImporter</c>）。
/// 注意它<b>不能</b>用于 <c>m_Script</c>——脚本引用需要 <see cref="ScriptType.Guid"/>
/// 那种 <c>MonoImporter</c> 的 GUID。</para>
/// </summary>
public sealed class AsmDef
{
    /// <summary>程序集名（= <c>.asmdef</c> 的 <c>name</c>，不含 <c>.dll</c>）。</summary>
    public string name { get; set; } = string.Empty;

    /// <summary><c>.asmdef.meta</c> 的 GUID。</summary>
    public string guid { get; set; } = string.Empty;

    /// <summary>该程序集里可序列化的类型（<c>MonoBehaviour</c> / <c>ScriptableObject</c> / <c>[Serializable]</c>）。</summary>
    public List<ScriptType> types { get; set; } = new();
}

/// <summary>
/// 一个可序列化类型，带它在包里 <c>.cs.meta</c> 的 GUID。
///
/// <para>AssetRipper 导出裸 DLL 时给脚本引用写 <c>guid = MD5(程序集名)</c>，
/// 换成 UPM 包后这个 GUID 不存在。包里的 <c>.cs.meta</c> GUID 是打包时固定下来的，
/// 用它替换就能让引用继续指向正确的 MonoScript。</para>
/// </summary>
public sealed class ScriptType
{
    /// <summary>命名空间；空串表示全局命名空间。</summary>
    public string @namespace { get; set; } = string.Empty;

    /// <summary>类型名；嵌套类型用 <c>Outer+Inner</c>（Unity 的完整名形式）。</summary>
    public string name { get; set; } = string.Empty;

    /// <summary>该 <c>.cs.meta</c> 的 GUID（<c>MonoImporter</c>，可用于 <c>m_Script</c>）。</summary>
    public AssetRipper.Primitives.UnityGuid guid { get; set; }
}
