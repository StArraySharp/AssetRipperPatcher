using System.Diagnostics;

namespace AssetRipperPatcher.Tools.UnityPackages;

/// <summary>
/// Unity 包数据生成器：从官方注册表抓包，提取 <c>List&lt;UnityPackage&gt;</c> 供主工程编译。
///
/// <para><b>为什么是独立可执行项目。</b>
/// 这一步要联网、要下载几 GB 的包、要解析几万个源文件——全是开发期一次性工作。
/// 放进主工程会让每次 <c>dotnet build</c> 都带上 Roslyn 依赖和网络风险。
/// 拆分后：工具只「生成数据」，主工程只「读数据」，两边互不感知。</para>
///
/// <para><b>用法。</b></para>
/// <code>
/// # 用默认路径（seed 清单在 tools/unity-packages.txt）
/// dotnet run --project tools/UnityPackageHarvester
///
/// # 指定清单与输出
/// dotnet run --project tools/UnityPackageHarvester -- \
///     --packages tools/unity-packages.txt \
///     --out-dir AssetRipperPatcher
///
/// # 只处理某几个包（调试用）
/// dotnet run --project tools/UnityPackageHarvester -- --only com.unity.textmeshpro
/// </code>
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Options.PrintUsage();
            return 2;
        }

        if (options.Help)
        {
            Options.PrintUsage();
            return 0;
        }

        Console.WriteLine($"seed list : {options.PackagesFile}");
        Console.WriteLine($"cache     : {options.CacheDir}");
        Console.WriteLine($"output    : {options.OutputDir}");
        Console.WriteLine();

        List<PackageRequest> packages;
        try
        {
            packages = ReadPackageList(options.PackagesFile, options.Only);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"cannot read package list: {ex.Message}");
            return 2;
        }

        if (packages.Count == 0)
        {
            Console.Error.WriteLine("no packages to process");
            return 2;
        }
        Console.WriteLine($"resolving {packages.Count} package(s)...");

        Directory.CreateDirectory(options.CacheDir);
        UnityRegistry registry = new();

        // 阶段 1：解析版本（元数据很小，顺序请求即可）。
        List<UnityRegistry.PackagePlan> plans = new();
        List<string> skipped = new();
        foreach (PackageRequest request in packages)
        {
            UnityRegistry.PackagePlan? plan = await registry.ResolveAsync(request.Name, request.Version, options.CacheDir);
            if (plan is null)
            {
                skipped.Add(request.Version is null ? request.Name : $"{request.Name}@{request.Version}");
            }
            else
            {
                plans.Add(plan.Value);
            }
        }
        Console.WriteLine($"  {plans.Count} resolvable, {skipped.Count} skipped (no usable release or not found)");

        // 阶段 2：并发下载。串行拉 163 个 tarball 要十几分钟，其中几个包上百 MB。
        Console.WriteLine($"downloading {plans.Count} archive(s) with {options.Parallelism} worker(s)...");
        int downloaded = 0;
        await Parallel.ForEachAsync(
            plans,
            new ParallelOptions { MaxDegreeOfParallelism = options.Parallelism },
            async (plan, _) =>
            {
                if (await registry.DownloadAsync(plan, options.Refresh))
                {
                    Interlocked.Increment(ref downloaded);
                }
                else
                {
                    lock (skipped)
                    {
                        skipped.Add($"{plan.Name} (download failed)");
                    }
                }
            });
        Console.WriteLine($"  {downloaded} archive(s) available");

        // 阶段 3：解包提取。
        Console.WriteLine("extracting...");
        List<PackageModel> catalog = new();

        foreach (UnityRegistry.PackagePlan plan in plans)
        {
            if (!File.Exists(plan.CachePath))
            {
                continue;
            }

            string temp = Path.Combine(Path.GetTempPath(), "uph-" + Guid.NewGuid().ToString("N"));
            try
            {
                string? root = await UnityRegistry.ExtractAsync(plan.CachePath, temp);
                if (root is null)
                {
                    Console.Error.WriteLine($"  ! {plan.Name}: extract failed");
                    continue;
                }

                PackageModel model = PackageExtractor.Extract(root, plan.Name, plan.Version);
                catalog.Add(model);

                int typeCount = model.AsmDefs.Sum(static a => a.Types.Count);
                Console.WriteLine($"  {plan.Name} {plan.Version}: {model.AsmDefs.Count} asmdef, {typeCount} type(s)");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  ! {plan.Name}: {ex.Message}");
            }
            finally
            {
                TryDeleteDirectory(temp);
            }
        }

        // 阶段 4：写产物。
        string catalogPath = Path.Combine(options.OutputDir, "UnityPackageCatalog.Generated.cs");
        CodeWriter.WriteCatalog(catalogPath, catalog);

        Console.WriteLine();
        Console.WriteLine($"wrote {catalogPath} ({catalog.Count} package(s), " +
            $"{catalog.Sum(static p => p.AsmDefs.Count)} asmdef(s), " +
            $"{catalog.Sum(static p => p.AsmDefs.Sum(static a => a.Types.Count))} type(s))");

        if (skipped.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"skipped {skipped.Count} package(s):");
            foreach (string item in skipped.OrderBy(static s => s, StringComparer.Ordinal))
            {
                Console.WriteLine($"  {item}");
            }
        }
        return 0;
    }

    /// <summary>清单里的一项：<c>name</c> 或 <c>name@version</c>（pin 到具体版本，允许预发布）。</summary>
    private readonly record struct PackageRequest(string Name, string? Version);

    private static List<PackageRequest> ReadPackageList(string path, IReadOnlyCollection<string> only)
    {
        List<PackageRequest> packages = File.ReadAllLines(path)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0 && !line.StartsWith('#'))
            .Select(static line => ParseRequest(line))
            .DistinctBy(static r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (only.Count == 0)
        {
            return packages;
        }
        // --only 既可以从清单里挑，也可以直接给清单外的包名（同样支持 name@version）。
        HashSet<string> selected = new(only.Select(static o => ParseRequest(o).Name), StringComparer.OrdinalIgnoreCase);
        packages = packages.Where(p => selected.Contains(p.Name)).ToList();
        foreach (string extra in only)
        {
            PackageRequest request = ParseRequest(extra);
            if (!packages.Contains(request, Comparer))
            {
                packages.Add(request);
            }
        }
        return packages;
    }

    private static PackageRequest ParseRequest(string text)
    {
        // 版本号本身含 '.'，包名不含 '@'，所以第一个 '@' 就是分隔符。
        int at = text.IndexOf('@', StringComparison.Ordinal);
        return at <= 0
            ? new PackageRequest(text, null)
            : new PackageRequest(text[..at], text[(at + 1)..]);
    }

    private static readonly IEqualityComparer<PackageRequest> Comparer =
        EqualityComparer<PackageRequest>.Create(static (a, b) =>
            string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Version, b.Version, StringComparison.OrdinalIgnoreCase),
            static r => StringComparer.OrdinalIgnoreCase.GetHashCode(r.Name));

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // 临时目录清理失败不影响结果
        }
    }

    /// <summary>命令行选项。</summary>
    private sealed class Options
    {
        public string PackagesFile { get; private init; } = string.Empty;
        public string CacheDir { get; private init; } = string.Empty;
        public string OutputDir { get; private init; } = string.Empty;
        public int Parallelism { get; private init; } = 8;
        public bool Refresh { get; private init; }
        public bool Help { get; private init; }
        public IReadOnlyList<string> Only { get; private init; } = [];

        public static Options Parse(string[] args)
        {
            // 从可执行文件位置往上找仓库根（含 .git 的目录），
            // 这样 `dotnet run --project tools/UnityPackageHarvester` 和直接跑 exe 都能工作。
            string root = FindRepositoryRoot();
            string packagesFile = Path.Combine(root, "tools", "unity-packages.txt");
            string cacheDir = Path.Combine(root, "tools", ".cache");
            string outputDir = Path.Combine(root, "AssetRipperPatcher");
            int parallelism = 8;
            bool refresh = false;
            bool help = false;
            List<string> only = new();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string Next(string name) =>
                    i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{name} requires a value");

                switch (arg)
                {
                    case "--packages":
                        packagesFile = Path.GetFullPath(Next(arg));
                        break;
                    case "--cache":
                        cacheDir = Path.GetFullPath(Next(arg));
                        break;
                    case "--out-dir":
                        outputDir = Path.GetFullPath(Next(arg));
                        break;
                    case "--parallel":
                        if (!int.TryParse(Next(arg), out parallelism) || parallelism < 1)
                        {
                            throw new ArgumentException("--parallel requires a positive integer");
                        }
                        break;
                    case "--only":
                        only.Add(Next(arg));
                        break;
                    case "--refresh":
                        refresh = true;
                        break;
                    case "-h":
                    case "--help":
                        help = true;
                        break;
                    default:
                        throw new ArgumentException($"unknown option: {arg}");
                }
            }

            return new Options
            {
                PackagesFile = packagesFile,
                CacheDir = cacheDir,
                OutputDir = outputDir,
                Parallelism = parallelism,
                Refresh = refresh,
                Help = help,
                Only = only,
            };
        }

        private static string FindRepositoryRoot()
        {
            // 从 AppContext.BaseDirectory 往上找含 .gitmodules（或 .git）的目录。
            string? directory = AppContext.BaseDirectory;
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory, ".gitmodules"))
                    || Directory.Exists(Path.Combine(directory, ".git")))
                {
                    return directory;
                }
                directory = Path.GetDirectoryName(directory);
            }
            // 找不到就用当前工作目录（用户显式传 --packages 时也能跑）。
            return Directory.GetCurrentDirectory();
        }

        public static void PrintUsage()
        {
            Console.WriteLine("""
                UnityPackageHarvester - extract Unity package metadata into C# data files.

                Usage:
                  dotnet run --project tools/UnityPackageHarvester [options]

                Options:
                  --packages <path>   Package name list (default: tools/unity-packages.txt).
                                      Entries may be "name" or "name@version"; a pinned
                                      version bypasses the stable-only filter.
                  --cache <dir>       Tarball cache (default: tools/.cache)
                  --out-dir <dir>     Where to write the .Generated.cs file
                                      (default: AssetRipperPatcher)
                  --parallel <n>      Concurrent downloads (default: 8)
                  --only <name>       Process only this package; repeatable
                  --refresh           Re-download even when the cache has the archive
                  -h, --help          Show this help

                Output:
                  <out-dir>/UnityPackageCatalog.Generated.cs
                                      List<UnityPackage>: packages, asmdefs, script GUIDs

                Unpinned packages use only stable releases; packages with no stable version
                are skipped, because a prerelease brings incompatible APIs and an
                unreproducible dependency graph. Pin a version explicitly (ugui@3.0.0-exp.4)
                when the only published build is a prerelease.
                """);
        }
    }
}
