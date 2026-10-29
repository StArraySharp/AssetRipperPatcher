using System.Reflection;
using System.Reflection.Emit;
using AssetRipper.Export.UnityProjects;
using AssetRipper.IO.Files;
using AssetRipper.Primitives;
using HarmonyLib;

namespace AssetRipperPatcher.Patches;

/// <summary>
/// MetaPtr 构造函数补丁：当 fileID 命中 GuidMapping 映射时，
/// 把 fileID 重写为 11500000 并替换为映射的 GUID（用于修正跨工程资源引用）。
/// </summary>
internal static class MetaPtrPatch
{
    public static void Register(Harmony harmony)
    {
        Type metaPtrType = typeof(MetaPtr);
        var constructor = metaPtrType.GetConstructor(
            BindingFlags.Public | BindingFlags.Instance,
            null,
            [typeof(long), typeof(UnityGuid), typeof(AssetType)],
            null
        );

        if (constructor != null)
        {
            // 使用 IL 编织器进行补丁
            harmony.Patch(constructor,
                transpiler: new HarmonyMethod(typeof(MetaPtrPatch).GetMethod(nameof(MetaPtr_ctor_Transpiler))));
            Console.WriteLine("MetaPtr Patched");
        }
        else
        {
            Console.WriteLine("MetaPtr .ctor(long, UnityGuid, AssetType) not found");
        }
    }

    public static IEnumerable<CodeInstruction> MetaPtr_ctor_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var codes = new List<CodeInstruction>(instructions);

        // 查找构造函数体开始的位置
        // 我们想要在构造函数开始执行前插入我们的逻辑
        bool foundStart = false;

        for (int i = 0; i < codes.Count; i++)
        {
            // 查找构造函数的开始（通常是加载 this 指针）
            if (!foundStart && codes[i].opcode == OpCodes.Ldarg_0)
            {
                Label continueLabel = generator.DefineLabel();

                var newInstructions = new List<CodeInstruction>
                {
                    new(OpCodes.Ldarga_S, 1), // 加载 fileID 参数的地址（ref 参数）
                    new(OpCodes.Ldarga_S, 2), // 加载 guid 参数的地址（ref 参数）
                    new(OpCodes.Call, typeof(MetaPtrPatch).GetMethod(nameof(ProcessMetaPtr),
                        BindingFlags.Static | BindingFlags.Public,
                        null,
                        [typeof(long).MakeByRefType(), typeof(UnityGuid).MakeByRefType()],
                        null)),
                    new(OpCodes.Br, continueLabel)
                };
                codes[i].labels.Add(continueLabel);

                codes.InsertRange(i, newInstructions);
                break;
            }
        }

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
