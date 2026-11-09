using System.Reflection;
using System.Reflection.Emit;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Primitives;
using AssetRipperPatcher.Patching;
using HarmonyLib;

namespace AssetRipperPatcher.Patches;

/// <summary>
/// MetaPtr 构造函数补丁：把「指向已换成 UPM 包的裸 DLL」的脚本引用，
/// 改写成包里 <c>.cs.meta</c> 的真实引用。
///
/// <para><b>改什么。</b>
/// AssetRipper 导出裸 DLL 时写
/// <c>{fileID: MD4('s'+0+0+0+ns+name)[0..4], guid: MD5(assemblyName), type: 3}</c>。
/// 删掉 DLL 后这个 <c>guid</c> 指向不存在的资产，工程里满屏 <c>Missing (Mono Script)</c>，
/// 而且**不可恢复**——包内程序集的 GUID 是 Unity 首次导入时随机生成的。</para>
///
/// <para><b>怎么知道该改哪条。</b>
/// fileID 是「命名空间 + 类型名」的确定性哈希（不含程序集），所以只要遍历
/// <see cref="UnityPackageCatalog"/> 里的类型算一遍 fileID 就能反查。
/// 候选可能跨多个包/版本，用当前工程的 Unity 版本筛掉装不上的包。</para>
/// </summary>
internal static class MetaPtrPatch
{
    /// <summary>
    /// transpiler 的 IL 锚点：构造函数的 <c>ldarg.0</c>（第一条加载 this 的指令）。
    ///
    /// <para><b>为什么找不到就抛。</b>
    /// 老实现找不到锚点时把指令原样返回，补丁静默失效——测试只证明「Harmony 绑上了」，
    /// 证明不了「指令真插进去了」。上游把构造函数改成表达式体 / 内联之后，
    /// 这类失败会以「TMP 引用莫名其妙没修好」的形式出现，极难定位。
    /// 这里让失败在打补丁时立刻炸掉，带上目标签名。</para>
    /// </summary>
    private const string AnchorDescription = "第一处 ldarg.0（构造函数入口）";

    /// <summary>导出期的 Unity 版本；<see cref="UnityVersion"/> 的默认值表示未知（不做版本筛选）。</summary>
    internal static UnityVersion ExportVersion { get; set; }

    // MetaPtr 是 record struct：主构造 (long,UnityGuid,AssetType) + 显式 (long)。
    // 候选集按参数个数升序：(long) 是 index 0，主构造是 index 1。
    [PatchMethod(typeof(MetaPtr), ".ctor", OverrideIndex = 1, PatchType = PatchType.Transpiler)]
    public static IEnumerable<CodeInstruction> MetaPtr_ctor_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod)
    {
        List<CodeInstruction> codes = new(instructions);

        int anchor = codes.FindIndex(static c => c.opcode == OpCodes.Ldarg_0);
        if (anchor < 0)
        {
            throw new PatchBindingException(
                $"MetaPtrPatch: 在 {__originalMethod.FullDescription()} 的 IL 中找不到{AnchorDescription}，" +
                "transpiler 无法插入包内脚本引用重写逻辑。" +
                (codes.Count == 0 ? "（指令序列为空）" : $"（共 {codes.Count} 条指令）"));
        }

        Label continueLabel = generator.DefineLabel();
        List<CodeInstruction> injected =
        [
            new(OpCodes.Ldarga_S, 1), // ref fileID
            new(OpCodes.Ldarga_S, 2), // ref guid
            new(OpCodes.Call, typeof(MetaPtrPatch).GetMethod(nameof(ProcessMetaPtr),
                BindingFlags.Static | BindingFlags.Public,
                null,
                [typeof(long).MakeByRefType(), typeof(UnityGuid).MakeByRefType()],
                null)!),
            new(OpCodes.Br, continueLabel),
        ];
        codes[anchor].labels.Add(continueLabel);
        codes.InsertRange(anchor, injected);

        return codes;
    }

    public static void ProcessMetaPtr(ref long fileID, ref UnityGuid guid)
    {
        if (fileID == UnityPackageCatalog.MonoScriptFileId)
        {
            return; // 已经是包引用，不要重复处理
        }

        if (UnityPackageCatalog.TryGetScriptGuid(fileID, ExportVersion, out UnityGuid packageGuid, out _))
        {
            fileID = UnityPackageCatalog.MonoScriptFileId;
            guid = packageGuid;
        }
    }
}
