using System.Reflection;
using System.Reflection.Emit;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Primitives;
using AssetRipperPatcher.Patching;
using HarmonyLib;

namespace AssetRipperPatcher.Patches;

/// <summary>
/// MetaPtr 构造函数补丁：当 fileID 命中 GuidMapping 映射时，
/// 把 fileID 重写为 11500000 并替换为映射的 GUID（用于修正跨工程资源引用）。
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

    // MetaPtr 是 record struct：主构造 (long,UnityGuid,AssetType) + 显式 (long)。
    // 候选集按参数个数升序：(long) 是 index 0，主构造是 index 1。
    [PatchMethod(typeof(MetaPtr), ".ctor", OverrideIndex = 1, PatchType = PatchType.Transpiler)]
    public static IEnumerable<CodeInstruction> MetaPtr_ctor_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod)
    {
        var codes = new List<CodeInstruction>(instructions);

        int anchor = codes.FindIndex(static c => c.opcode == OpCodes.Ldarg_0);
        if (anchor < 0)
        {
            throw new PatchBindingException(
                $"MetaPtrPatch: 在 {__originalMethod.FullDescription()} 的 IL 中找不到{AnchorDescription}，" +
                "transpiler 无法插入 GuidMapping 重写逻辑。" +
                (codes.Count == 0 ? "（指令序列为空）" : $"（共 {codes.Count} 条指令）"));
        }

        Label continueLabel = generator.DefineLabel();
        var injected = new List<CodeInstruction>
        {
            new(OpCodes.Ldarga_S, 1), // ref fileID
            new(OpCodes.Ldarga_S, 2), // ref guid
            new(OpCodes.Call, typeof(MetaPtrPatch).GetMethod(nameof(ProcessMetaPtr),
                BindingFlags.Static | BindingFlags.Public,
                null,
                [typeof(long).MakeByRefType(), typeof(UnityGuid).MakeByRefType()],
                null)!),
            new(OpCodes.Br, continueLabel),
        };
        codes[anchor].labels.Add(continueLabel);
        codes.InsertRange(anchor, injected);

        return codes;
    }

    public static void ProcessMetaPtr(ref long fileID, ref UnityGuid guid)
    {
        if (GuidMapping.TryGet(fileID, out var newGuid))
        {
            fileID = 11500000L;
            guid = newGuid;
        }
    }
}
