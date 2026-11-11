using AssetRipperPatcher.Api;
using NUnit.Framework;

namespace AssetRipperPatcher.Tests;

/// <summary>
/// 「选择单个文件」对话框过滤器的回归测试。
///
/// <para>UI 上的症状是：点「选择单个文件」后，对话框里除了 exe 什么都看不到。
/// 原因不是没加「所有文件」，而是它被<b>追加在具体扩展名之后</b>，
/// 而对话框只按第一项过滤。这些用例锁住「唯一的过滤器就是 *」。</para>
/// </summary>
public class OpenFileFilterTests
{
    [Test]
    public void Filter_has_a_single_entry()
    {
        var filter = DialogService.BuildOpenFileFilter();

        Assert.That(filter.Filter, Has.Count.EqualTo(1),
            "只应该有一个过滤器；多个具体扩展名会让首选项把其它文件挡在外面");
    }

    [Test]
    public void The_only_pattern_is_a_wildcard()
    {
        var filter = DialogService.BuildOpenFileFilter();
        List<string> patterns = filter.Filter.Values.Single();

        Assert.That(patterns, Is.EqualTo(new[] { "*" }),
            "唯一模式必须是 * —— 否则对话框默认仍会过滤掉不匹配的文件");
    }

    [Test]
    public void All_files_escape_hatch_stays_enabled()
    {
        var filter = DialogService.BuildOpenFileFilter();

        Assert.That(filter.IncludeAllFiles, Is.True,
            "有些平台的原生对话框不认 * 模式，仍需要兜底的「所有文件」项");
    }

    [Test]
    public void Every_extension_is_selectable()
    {
        var filter = DialogService.BuildOpenFileFilter();
        List<string> patterns = filter.Filter.Values.Single();

        // 游戏输入的实际形态：可执行文件、资源包、压缩包，以及无扩展名的散文件。
        foreach (string sample in new[] { "game.exe", "sharedassets0.assets", "bundle.bundle", "data.apk" })
        {
            Assert.That(Matches(patterns, sample), Is.True, $"{sample} 应该可选");
        }

        static bool Matches(List<string> patterns, string fileName) =>
            patterns.Any(p => p == "*" ||
                p.TrimStart('*', '.') is { Length: > 0 } ext &&
                fileName.EndsWith("." + ext, StringComparison.OrdinalIgnoreCase));
    }
}