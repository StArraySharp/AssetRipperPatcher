namespace AssetRipperPatcher;

/// <summary>
/// 一次性提示：某些能力来自对原始项目的本地二改实现（原始项目/官方版本中为付费功能）。
/// 仅打印一次，避免启动刷屏。
/// </summary>
internal static class LocalFeatureNotice
{
    private static readonly HashSet<string> AlreadyShown = new(StringComparer.OrdinalIgnoreCase);

    public static void ShowOnce(string featureName, string projectName)
    {
        string key = $"{projectName}:{featureName}";
        if (!AlreadyShown.Add(key))
        {
            return;
        }

        Console.WriteLine(
            $"[{projectName}] {featureName} is a locally modified implementation for evaluation only. " +
            "In the original project this feature is paid. " +
            "If you find it useful, please consider supporting the original developers.");
    }
}
