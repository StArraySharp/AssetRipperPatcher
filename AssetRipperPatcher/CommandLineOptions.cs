namespace AssetRipperPatcher;

/// <summary>
/// 命令行选项解析。只处理 patcher 自己的开关，其余参数原样留给 AssetRipper 的 Web 启动器。
/// </summary>
internal static class CommandLineOptions
{
    public const string OnlyOption = "--only";

    private const string Usage =
        """
        Usage: AssetRipperPatcher <input> <output> [options]

          --only <Type[,Type...]>   Only export assets whose class name matches
                                    (case-insensitive, '*' wildcard), e.g.
                                      --only Sprite
                                      --only Sprite,Texture2D
                                      --only Sprite*

        Without <input> <output> the AssetRipper web UI is launched; the option is
        still honoured there if you pass it explicitly.
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
