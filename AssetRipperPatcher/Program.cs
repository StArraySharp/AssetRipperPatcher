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

        // 命令行模式：AssetRipperPatcher <游戏路径> <输出路径>，直接导出（触发所有补丁 + 诊断）。
        // 退出码必须反映结果：外面套 `&&` / CI 的脚本只能看到退出码，
        // 失败却返回 0 会让它们以为导出成功。
        if (args.Length >= 2)
        {
            Environment.ExitCode = RunHeadlessExport(args[0], args[1]) ? 0 : 1;
            return;
        }

        // GUI 模式：确保设置文件存在（首次运行会写入推荐默认值），
        // 这样 Web UI 的「设置」页读到的就是同一份配置，而不是内存里的代码默认值。
        AppSettings.LoadOrCreate();
        WebApplicationLauncher.Launch(args);
    }

    /// <summary>
    /// 命令行导出模式：直接调用 AssetRipper 的导出 API。
    /// 用法：AssetRipperPatcher <游戏根目录或文件> <输出目录>
    /// 会触发已应用的所有补丁，并生成 MonoBehaviour 诊断文件。
    /// </summary>
    /// <returns>导出成功返回 true；输入不存在、拒绝删除输出目录、或导出抛异常时返回 false。</returns>
    private static bool RunHeadlessExport(string inputPath, string outputPath)
    {
        try
        {
            // 配置日志：AssetRipper 的所有日志输出到控制台 + 写入文件
            Logger.Add(new ConsoleLogger(resizeConsole: false));
            Logger.Add(new FileLogger("AssetRipperPatcher.Headless.log"));
            Logger.LogSystemInformation("AssetRipperPatcher");
            Logger.Info(LogCategory.General, $"Headless export: input='{inputPath}' output='{outputPath}'");

            FullConfiguration settings = AppSettings.LoadOrCreate();
            settings.LogConfigurationValues();
            ExportHandler exportHandler = new(settings);

            // 先校验输入，再碰输出目录：输入写错时绝不能有任何删除动作。
            string fullInput = Path.GetFullPath(inputPath);
            if (Directory.Exists(fullInput))
            {
                // 直接把游戏根目录交给 AssetRipper，由它自行探测 *_Data 目录并只加载其中的资源。
                // 避免递归枚举把 Mods.zip、Mods/ 下的 zip/dll 等 _Data 目录之外的内容一并导入。
                Logger.Info(LogCategory.Import, $"Using game directory '{fullInput}' (only *_Data content will be loaded)");
            }
            else if (File.Exists(fullInput))
            {
            }
            else
            {
                Logger.Error($"Input not found: '{inputPath}'");
                return false;
            }

            // 删除输出目录前的安全检查。参数顺序写反一次就可能把游戏目录整个删掉，
            // 所以这里不仅检查「相等」，还检查「互相包含」，并拒绝明显危险的根目录。
            string fullOutput = Path.GetFullPath(outputPath);
            if (!ValidateOutputDirectory(fullInput, fullOutput, out string refusal))
            {
                Logger.Error($"Refusing to clear output directory: {refusal}");
                return false;
            }

            if (Directory.Exists(fullOutput))
            {
                Logger.Info(LogCategory.Export, $"Clearing export directory '{fullOutput}'...");
                Directory.Delete(fullOutput, true);
            }

            GameData gameData = exportHandler.LoadAndProcess([fullInput], LocalFileSystem.Instance);
            AppState.CommandLineGameData = gameData;
            exportHandler.Export(gameData, fullOutput, LocalFileSystem.Instance);
            Logger.Info(LogCategory.General, "Export completed.");
            ShaderExportPatch.ReportShaderTally();
            MonoBehaviourReadPatch.ReportLenientStats();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Headless export failed: {ex}");
            return false;
        }
    }

    /// <summary>
    /// 删除输出目录前的护栏。
    ///
    /// <para>命令行参数的顺序极易写反（<c>--only out in</c> 之类），而
    /// <c>Directory.Delete(outputPath, true)</c> 是不可撤销的。这里拒绝三种情况：</para>
    /// <list type="number">
    /// <item>输出目录与输入路径相同（含忽略大小写/尾部分隔符的比较）。</item>
    /// <item>两者互相包含——删除输出目录会连带删掉输入。</item>
    /// <item>输出目录是盘符根、用户主目录或当前工作目录这类「明显不该被清空」的位置。</item>
    /// </list>
    /// </summary>
    internal static bool ValidateOutputDirectory(string fullInput, string fullOutput, out string refusal)
    {
        refusal = string.Empty;

        static string Normalize(string path) =>
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        string input = Normalize(fullInput);
        string output = Normalize(fullOutput);

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(input, output, comparison))
        {
            refusal = $"output path equals input path ('{output}')";
            return false;
        }

        // 输出包含输入：删输出就会删掉游戏本体。
        if (IsSubPathOf(input, output, comparison))
        {
            refusal = $"output ('{output}') is a parent of input ('{input}')";
            return false;
        }

        // 输入包含输出：输出目录在游戏目录里面，删除后又会被重新导入，
        // 通常意味着参数写错，同样拒绝。
        if (IsSubPathOf(output, input, comparison))
        {
            refusal = $"output ('{output}') is inside input ('{input}')";
            return false;
        }

        string? volumeRoot = Path.GetPathRoot(output);
        if (volumeRoot is not null && string.Equals(output, Normalize(volumeRoot), comparison))
        {
            refusal = $"output ('{output}') is a drive root";
            return false;
        }

        string? home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home) && string.Equals(output, Normalize(home), comparison))
        {
            refusal = $"output ('{output}') is the user profile directory";
            return false;
        }

        return true;
    }

    /// <summary>
    /// <paramref name="candidate"/> 是否位于 <paramref name="ancestor"/> 之下。
    /// 相等不算（相等由调用方单独判断）。
    /// </summary>
    private static bool IsSubPathOf(string candidate, string ancestor, StringComparison comparison)
    {
        if (candidate.Length <= ancestor.Length)
        {
            return false;
        }
        if (!candidate.StartsWith(ancestor, comparison))
        {
            return false;
        }
        char boundary = candidate[ancestor.Length];
        return boundary == Path.DirectorySeparatorChar || boundary == Path.AltDirectorySeparatorChar;
    }
}
