using AssetRipper.Export.Configuration;
using AssetRipper.Export.UnityProjects;
using AssetRipper.GUI.Web;
using AssetRipper.Import.Logging;
using AssetRipper.IO.Files;
using AssetRipper.Processing;
using AssetRipperPatcher.Patching;
using AssetRipperPatcher.Patches;
using HarmonyLib;

namespace AssetRipperPatcher;

class Program
{
    private static readonly Harmony _harmony = new("starray.unity." + nameof(AssetRipperPatcher));

    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Console.WriteLine(e.ExceptionObject);
        };

        // 命令行选项：--only Sprite,Texture2D 之类，必须在补丁注册前解析（过滤补丁读 AppState）
        args = CommandLineOptions.Apply(args);

        // 应用所有补丁：照 [PatchMethod] 注解自动注册（失败即抛，永不静默跳过）
        LocalFeatureNotice.ShowOnce("Shader decompilation", "AssetRipperPatcher");
        PatchRegistrar.RegisterAll(_harmony);

        // 命令行模式：AssetRipperPatcher <游戏路径> <输出路径>，直接导出（触发所有补丁 + 诊断）
        if (args.Length >= 2)
        {
            RunHeadlessExport(args[0], args[1]);
            return;
        }

        WebApplicationLauncher.Launch(args);
    }

    /// <summary>
    /// 命令行导出模式：直接调用 AssetRipper 的导出 API。
    /// 用法：AssetRipperPatcher <游戏根目录或文件> <输出目录>
    /// 会触发已应用的所有补丁，并生成 MonoBehaviour 诊断文件。
    /// </summary>
    private static void RunHeadlessExport(string inputPath, string outputPath)
    {
        try
        {
            // 配置日志：AssetRipper 的所有日志输出到控制台 + 写入文件
            Logger.Add(new ConsoleLogger(resizeConsole: false));
            Logger.Add(new FileLogger("AssetRipperPatcher.Headless.log"));
            Logger.LogSystemInformation("AssetRipperPatcher");
            Logger.Info(LogCategory.General, $"Headless export: input='{inputPath}' output='{outputPath}'");

            FullConfiguration settings = new();
            // 加载 bin 下的 AssetRipper.Settings.json（与 Web 模式共用同一份配置）。
            // 不加载的话下面 LogConfigurationValues 打印的是代码默认值，导出行为也和用户配置不一致。
            settings.LoadFromDefaultPath();
            settings.LogConfigurationValues();
            ExportHandler exportHandler = new(settings);

            string[] inputPaths;
            if (Directory.Exists(inputPath))
            {
                // 直接把游戏根目录交给 AssetRipper，由它自行探测 *_Data 目录并只加载其中的资源。
                // 避免递归枚举把 Mods.zip、Mods/ 下的 zip/dll 等 _Data 目录之外的内容一并导入。
                inputPaths = [inputPath];
                Logger.Info(LogCategory.Import, $"Using game directory '{inputPath}' (only *_Data content will be loaded)");
            }
            else if (File.Exists(inputPath))
            {
                inputPaths = [inputPath];
            }
            else
            {
                Logger.Error($"Input not found: '{inputPath}'");
                return;
            }

            if (Directory.Exists(outputPath))
            {
                Logger.Info(LogCategory.Export, "Clearing export directory...");
                Directory.Delete(outputPath, true);
            }

            GameData gameData = exportHandler.LoadAndProcess(inputPaths, LocalFileSystem.Instance);
            AppState.CommandLineGameData = gameData;
            exportHandler.Export(gameData, outputPath, LocalFileSystem.Instance);
            Logger.Info(LogCategory.General, "Export completed.");
            ShaderExportPatch.ReportShaderTally();
        }
        catch (Exception ex)
        {
            Logger.Error($"Headless export failed: {ex}");
        }    
    }
}