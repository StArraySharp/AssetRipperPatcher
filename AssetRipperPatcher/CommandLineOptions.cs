namespace AssetRipperPatcher;

/// <summary>
/// 命令行选项解析。只处理 patcher 自己的开关，其余参数原样留给 AssetRipper 的 Web 启动器。
/// </summary>
internal static class CommandLineOptions
{
    public const string OnlyOption = "--only";
    public const string OnlyStrictOption = "--only-strict";
    public const string RemovePackagedDllsOption = "--remove-packaged-dlls";
        public const string WebOption = "--web";
        public const string LegacyUiOption = "--legacy-ui";
        public const string PortOption = "--port";
        public const string NoOpenOption = "--no-open";

        private const string Usage =
            """
            Usage: AssetRipperPatcher <input> <output> [options]

              --only <Type[,Type...]>   Only export assets whose class name matches
                                        (case-insensitive, '*' wildcard), e.g.
                                          --only Sprite
                                          --only Sprite,Texture2D
                                          --only Sprite*
                                        Dependencies of the matches are exported too,
                                        so the project has no dangling references.

              --only-strict             With --only: keep only the matched types, without
                                        following references. Faster, but the output will
                                        contain dangling guid references.

            Without <input> <output> the desktop UI opens in a native window.

              --web                     Serve the UI and open it in the default browser
                                        instead of a native window (useful for DevTools).
              --legacy-ui               Launch the original AssetRipper web UI instead.
              --port <N>                Listen on a fixed port (default: any free port).
              --no-open                 Do not open a window or browser automatically.
            """;

    /// <summary>
    /// 解析并吃掉 <c>--only</c> 参数；返回剩余参数。
    /// </summary>
    public static string[] Apply(string[] args)
    {
        List<string> remaining = new(args.Length);
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "--help" or "-h" or "/?")
            {
                Console.WriteLine(Usage);
                Environment.Exit(0);
                return [];
            }

            if (arg.Equals(OnlyOption, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    Fail($"{OnlyOption} requires a value");
                    return [];
                }
                string value = args[++i];
                if (string.IsNullOrWhiteSpace(value))
                {
                    Fail($"{OnlyOption} requires a non-empty value");
                    return [];
                }
                AppState.CommandLineFilter = AppState.AssetFilter.Parse(value);
                Console.WriteLine($"[AssetFilter] --only '{value}'");
                continue;
            }

            if (arg.Equals(OnlyStrictOption, StringComparison.OrdinalIgnoreCase))
            {
                AppState.StrictOnly = true;
                Console.WriteLine($"[AssetFilter] {OnlyStrictOption}: dependencies will NOT be exported");
                continue;
            }

            if (arg.Equals(RemovePackagedDllsOption, StringComparison.OrdinalIgnoreCase))
            {
                AppState.RemovePackagedDlls = true;
                Console.WriteLine($"[Plugins] {RemovePackagedDllsOption}: packaged DLLs will be deleted " +
                    "(script references to them will break)");
                continue;
            }

                        if (arg.Equals(WebOption, StringComparison.OrdinalIgnoreCase))
                        {
                            AppState.OpenUiInBrowser = true;
                            continue;
                        }

                        if (arg.Equals(LegacyUiOption, StringComparison.OrdinalIgnoreCase))
                        {
                            AppState.UseLegacyUi = true;
                            continue;
                        }

                        if (arg.Equals(NoOpenOption, StringComparison.OrdinalIgnoreCase))
                        {
                            AppState.NoAutoOpen = true;
                            continue;
                        }

                        if (arg.Equals(PortOption, StringComparison.OrdinalIgnoreCase))
                        {
                            if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out int port) || port is < 0 or > 65535)
                            {
                                Fail($"{PortOption} requires a port number (0-65535)");
                                return [];
                            }
                            AppState.UiPort = port;
                            i++;
                            continue;
                        }

                        if (arg.StartsWith(PortOption + "=", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!int.TryParse(arg[(PortOption.Length + 1)..], out int port) || port is < 0 or > 65535)
                            {
                                Fail($"{PortOption} requires a port number (0-65535)");
                                return [];
                            }
                            AppState.UiPort = port;
                            continue;
                        }

            // 支持 --only=Value 这种写法
            int eq = arg.IndexOf('=');
            if (eq > 0 && arg[..eq].Equals(OnlyOption, StringComparison.OrdinalIgnoreCase))
            {
                string value = arg[(eq + 1)..];
                if (string.IsNullOrWhiteSpace(value))
                {
                    Fail($"{OnlyOption} requires a non-empty value");
                    return [];
                }
                AppState.CommandLineFilter = AppState.AssetFilter.Parse(value);
                Console.WriteLine($"[AssetFilter] --only '{value}'");
                continue;
            }

            remaining.Add(arg);
        }
        return remaining.ToArray();
    }

    private static void Fail(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        Console.Error.WriteLine();
        Console.Error.WriteLine(Usage);
        Environment.Exit(2);
    }
}
