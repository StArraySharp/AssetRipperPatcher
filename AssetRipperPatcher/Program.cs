using System.Reflection;
using System.Reflection.Emit;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Export.UnityProjects.Textures;
using AssetRipper.GUI.Web;
using AssetRipper.IO.Files;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated.Classes.ClassID_1006;
using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Classes.ClassID_687078895;
using AssetRipper.SourceGenerated.Subclasses.Vector2f;
using AssetRipper.SourceGenerated.Subclasses.Vector4f;
using HarmonyLib;
using BindingFlags = System.Reflection.BindingFlags;

namespace AssetRipperPatcher;

class Program
{
    private static Harmony _harmony = new ("starray.unity." + nameof(AssetRipperPatcher));
    public static void Main(string[] args)
    {
        // 1. 应用 MetaPtr 构造函数补丁
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
            _harmony.Patch(constructor,
                transpiler: new HarmonyMethod(typeof(Program).GetMethod(nameof(MetaPtr_ctor_Transpiler))));
            Console.WriteLine("MetaPtr 构造函数 IL 编织补丁已应用");
        }
        else
        {
            Console.WriteLine("未找到 MetaPtr 构造函数");
        }

        // 2. 应用 TextureExportCollection.AddSprites 方法补丁
        Type? textureExportCollectionType = typeof(TextureExportCollection);
        if (textureExportCollectionType != null)
        {
            MethodInfo? addSpritesMethod = textureExportCollectionType.GetMethod("AddSprites",
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                [typeof(IExportContainer), typeof(ITextureImporter), typeof(IReadOnlyDictionary<ISprite, ISpriteAtlas?>)],
                null);

            if (addSpritesMethod != null)
            {
                var transpilerMethod = typeof(Program).GetMethod(nameof(AddSprites_Transpiler), BindingFlags.Static | BindingFlags.Public);
                if (transpilerMethod != null)
                {
                    _harmony.Patch(addSpritesMethod,
                        transpiler: new HarmonyMethod(transpilerMethod));
                    Console.WriteLine("TextureExportCollection.AddSprites 方法 IL 编织补丁已应用");
                }
                else
                {
                    Console.WriteLine("未找到 AddSprites_Transpiler 方法");
                }
            }
            else
            {
                Console.WriteLine("未找到 TextureExportCollection.AddSprites 方法");
            }
        }

        // 启动 Web 应用
        WebApplicationLauncher.Launch(args);
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
                foundStart = true;

                Label continueLabel = generator.DefineLabel();

                var newInstructions = new List<CodeInstruction>
                {
                    new(OpCodes.Ldarg_1), // 加载 fileID 参数
                    new(OpCodes.Call, typeof(Program).GetMethod(nameof(ProcessFileID))),
                    new(OpCodes.Starg_S, 1),
                    new(OpCodes.Ldarg_2), // 加载 guid 参数
                    new(OpCodes.Call, typeof(Program).GetMethod(nameof(ProcessGuid))),
                    new(OpCodes.Starg_S, 2),
                    new(OpCodes.Br, continueLabel)
                };
                codes[i].labels.Add(continueLabel);

                codes.InsertRange(i, newInstructions);
                break;
            }
        }

        return codes;
    }

    private static long currentFileId;
    public static long ProcessFileID(long fileID)
    {
        if (GuidMapping.FileIDAndGuid.TryGetValue(fileID, out _))
        {
            Console.WriteLine($"处理 TMP fileID: {fileID} -> 11500000");
            currentFileId = fileID;
            return 11500000L;
        }

        return fileID;
    }

    public static UnityGuid ProcessGuid(UnityGuid guid)
    {
        if (GuidMapping.FileIDAndGuid.TryGetValue(currentFileId, out var newGuid))
        {
            Console.WriteLine($"处理 TMP GUID: {guid} -> {newGuid}");
            return newGuid;
        }

        return guid;
    }

    public static IEnumerable<CodeInstruction> AddSprites_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var codes = new List<CodeInstruction>(instructions);
        var modifiedIndices = new List<int>();

        Console.WriteLine($"开始分析 IL 代码，共 {codes.Count} 条指令");

        for (int i = 0; i < codes.Count; i++)
        {
            var code = codes[i];

            // 查找 CopyValues 方法调用 - 注意：根据 IL 注释，CopyValues 是 callvirt 而不是 call
            if ((code.opcode == OpCodes.Call || code.opcode == OpCodes.Callvirt) && code.operand is MethodInfo methodInfo)
            {
                if (methodInfo.Name == "CopyValues")
                {
                    bool foundMatch = false;
                    for (int lookback = 1; lookback <= 10; lookback++)
                    {
                        if (i >= lookback && codes[i-lookback].opcode == OpCodes.Callvirt && codes[i-lookback].operand is MethodInfo potentialGetter)
                        {
                            if (potentialGetter.Name.Contains("get_SpritePivot") || potentialGetter.Name.Contains("get_SpriteBorder"))
                            {
                                // 现在查找对应的 sprite getter
                                for (int lookback2 = 1; lookback2 <= 5; lookback2++)
                                {
                                    int spriteGetterIndex = i - lookback2;
                                    if (spriteGetterIndex >= 0 && spriteGetterIndex != i-lookback &&
                                        codes[spriteGetterIndex].opcode == OpCodes.Callvirt &&
                                        codes[spriteGetterIndex].operand is MethodInfo spriteGetter)
                                    {
                                        if (potentialGetter.Name.Contains("get_SpritePivot") && spriteGetter.Name.Contains("get_Pivot"))
                                        {
                                            codes[i] = new CodeInstruction(OpCodes.Call, typeof(Program).GetMethod(nameof(CustomSpritePivotCopyValues)));
                                            modifiedIndices.Add(i);
                                            foundMatch = true;
                                            break;
                                        }
                                        if (potentialGetter.Name.Contains("get_SpriteBorder") && spriteGetter.Name.Contains("get_Border"))
                                        {
                                            codes[i] = new CodeInstruction(OpCodes.Call, typeof(Program).GetMethod(nameof(CustomSpriteBorderCopyValues)));
                                            modifiedIndices.Add(i);
                                            foundMatch = true;
                                            break;
                                        }
                                    }
                                }
                                if (foundMatch) break;
                            }
                        }
                    }
                }
            }
        }
        return codes;
    }
    
    public static void CustomSpritePivotCopyValues(object target, object source)
    {
        if (target is Vector2f targetPivot && source is Vector2f sourcePivot)
        {
            Console.WriteLine("自定义 SpritePivot.CopyValues 调用");
            Console.WriteLine($"目标对象: {target.GetType().Name}");
            Console.WriteLine($"源对象: {source.GetType().Name}");
            Console.WriteLine($"原始枢轴值: 目标 X={targetPivot.X}, Y={targetPivot.Y}, 源 X={sourcePivot.X}, Y={sourcePivot.Y}");
            targetPivot.CopyValues(sourcePivot);
            Console.WriteLine($"复制后枢轴值: X={targetPivot.X}, Y={targetPivot.Y}");
        }
    }

    public static void CustomSpriteBorderCopyValues(object target, object source)
    {
        if (target is Vector4f targetBorder && source is Vector4f sourceBorder)
        {
            Console.WriteLine("自定义 SpriteBorder.CopyValues 调用");
            Console.WriteLine($"目标对象: {target.GetType().Name ?? "null"}");
            Console.WriteLine($"源对象: {source.GetType().Name ?? "null"}");
            Console.WriteLine($"原始边框值: 目标 X={targetBorder.X}, Y={targetBorder.Y}, Z={targetBorder.Z}, W={targetBorder.W}");
            Console.WriteLine($"源边框值: X={sourceBorder.X}, Y={sourceBorder.Y}, Z={sourceBorder.Z}, W={sourceBorder.W}");
            targetBorder.CopyValues(sourceBorder);
            Console.WriteLine($"复制后边框值: X={targetBorder.X}, Y={targetBorder.Y}, Z={targetBorder.Z}, W={targetBorder.W}");
        }
    }
}