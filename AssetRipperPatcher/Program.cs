using System.Reflection;
using System.Reflection.Emit;
using AssetRipper.Assets;
using AssetRipper.Export;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Export.UnityProjects.Project;
using AssetRipper.Export.UnityProjects.Shaders;
using AssetRipper.GUI.Web;
using AssetRipper.IO.Files;
using AssetRipper.Primitives;
using AssetRipper.Processing;
using AssetRipper.SourceGenerated.Classes.ClassID_150;
using AssetRipper.SourceGenerated.Classes.ClassID_48;
using AssetRipper.SourceGenerated.Classes.ClassID_9;
using AssetRipper.SourceGenerated.Subclasses.Vector2f;
using AssetRipper.SourceGenerated.Subclasses.Vector4f;
using AssetRipperPatcher.Shader.AssetRipper.Export.Modules.Shaders.Exporters;
using HarmonyLib;
using BindingFlags = System.Reflection.BindingFlags;

namespace AssetRipperPatcher;

class Program
{
    private static Harmony _harmony = new("starray.unity." + nameof(AssetRipperPatcher));
    public static bool IsUnity6 => GameFileLoader.GameBundle.GetMaxUnityVersion().Major >= 6000;
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            Console.WriteLine(args.ExceptionObject);
        };
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
            Console.WriteLine("MetaPtr Patched");
        }
        else
        {
            Console.WriteLine("MetaPtr .ctor(long, UnityGuid, AssetType) not found");
        }
        Type projectExporterType = typeof(DummyShaderTextExporter);
        MethodInfo? exportShader = projectExporterType.GetMethod("Export", 
            BindingFlags.Public | BindingFlags.Instance,
            null,
            [typeof(IExportContainer), typeof(IUnityObjectBase), typeof(string), typeof(FileSystem)],
            null);
        if (exportShader != null)
        {
            _harmony.Patch(exportShader,
                prefix: new HarmonyMethod(typeof(Program).GetMethod(nameof(ExportShader))));
            Console.WriteLine("DummyShaderTextExporter.ExportShader Patched");
            Console.WriteLine("Shader decompilation is an experimental feature. In the official version, this feature is paid. If you can afford it, please support the official version");
        }
        else
        {
            Console.WriteLine("DummyShaderTextExporter.ExportShader not found");
        }

        // 3. 应用 PackageManifest 补丁（注入 TMP/UGUI 包依赖）
        Type manifestExporterType = typeof(PackageManifestPostExporter);
        MethodInfo? createManifest = manifestExporterType.GetMethod("CreateManifest",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null,
            [typeof(UnityVersion)],
            null);
        if (createManifest != null)
        {
            _harmony.Patch(createManifest,
                prefix: new HarmonyMethod(typeof(Program).GetMethod(nameof(PatchCreateManifest))));
            Console.WriteLine("PackageManifestPostExporter.CreateManifest Patched");
        }
        else
        {
            Console.WriteLine("PackageManifestPostExporter.CreateManifest not found");
        }

        
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
                Label continueLabel = generator.DefineLabel();

                var newInstructions = new List<CodeInstruction>
                {
                    new(OpCodes.Ldarga_S, 1), // 加载 fileID 参数的地址（ref 参数）
                    new(OpCodes.Ldarga_S, 2), // 加载 guid 参数的地址（ref 参数）
                    new(OpCodes.Call, typeof(Program).GetMethod(nameof(ProcessMetaPtr),
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

    private static ShaderDisassemblyExporter _disassemblyExporter = new();
    public static bool ExportShader(IExportContainer container, 
        IUnityObjectBase asset, string path,
        FileSystem fileSystem,ref bool __result)
    {
        __result = _disassemblyExporter.Export(container, asset, path, fileSystem);
        return false;
    }

    /// <summary>
    /// Harmony Prefix: 在导出 manifest.json 时注入 IDE 和 UGUI 包依赖
    /// </summary>
    public static bool PatchCreateManifest(UnityVersion version, ref PackageManifest __result)
    {
        // 先调用原始逻辑获取默认清单
        __result = PackageManifest.CreateDefault(version);

        // 注入 IDE 支持
        __result.Dependencies.TryAdd("com.unity.ide.rider", "3.0.31");
        __result.Dependencies.TryAdd("com.unity.ide.visualstudio", "2.0.22");

        // 仅 Unity 6+ 注入 UGUI（UGUI 自带 TMP，无需单独声明）
        if (IsUnity6)
        {
            __result.Dependencies.TryAdd("com.unity.ugui", GuidMapping.UGUIVersion);
        }

        return false; // 跳过原始方法
    }
}