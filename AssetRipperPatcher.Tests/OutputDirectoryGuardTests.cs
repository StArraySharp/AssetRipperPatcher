using AssetRipperPatcher;
using Xunit;

namespace AssetRipperPatcher.Tests;

/// <summary>
/// 命令行导出前「清空输出目录」的护栏回归测试。
///
/// <c>Directory.Delete(outputPath, true)</c> 不可撤销，而命令行参数写反一次
/// （把游戏目录当成输出目录）就会把游戏本体整个删掉。这些用例锁住拒绝条件，
/// 也锁住「合法但相似」的路径不能被误拒。
/// </summary>
public class OutputDirectoryGuardTests
{
    private static readonly string Root = Path.GetPathRoot(Path.GetFullPath("."))!;

    private static string P(params string[] parts) =>
        Path.GetFullPath(Path.Combine([Root, .. parts]));

    [Fact]
    public void Rejects_output_equal_to_input()
    {
        string game = P("games", "MyGame");
        Assert.False(Program.ValidateOutputDirectory(game, game, out string reason));
        Assert.Contains("equals input", reason);
    }

    [Fact]
    public void Rejects_output_equal_to_input_ignoring_trailing_separator()
    {
        string game = P("games", "MyGame");
        Assert.False(Program.ValidateOutputDirectory(game, game + Path.DirectorySeparatorChar, out _));
    }

    [Fact]
    public void Rejects_output_that_contains_input()
    {
        string game = P("games", "MyGame");
        string parent = P("games");
        Assert.False(Program.ValidateOutputDirectory(game, parent, out string reason));
        Assert.Contains("parent of input", reason);
    }

    [Fact]
    public void Rejects_output_inside_input()
    {
        string game = P("games", "MyGame");
        string nested = Path.Combine(game, "export");
        Assert.False(Program.ValidateOutputDirectory(game, nested, out string reason));
        Assert.Contains("inside input", reason);
    }

    /// <summary>删输出目录会连带把输入删掉——必须拒绝。</summary>
    [Fact]
    public void Rejects_output_that_would_delete_the_game()
    {
        string game = Path.Combine(P("games"), "MyGame");
        Assert.False(Program.ValidateOutputDirectory(game, Path.GetPathRoot(game)!, out _));
    }

    [Fact]
    public void Rejects_drive_root_and_user_profile()
    {
        Assert.False(Program.ValidateOutputDirectory(P("games", "MyGame"), Root, out string rootReason));
        Assert.Contains("drive root", rootReason);

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(Program.ValidateOutputDirectory(P("games", "MyGame"), home, out string homeReason));
        Assert.Contains("user profile", homeReason);
    }

    /// <summary>
    /// 兄弟目录不算包含：<c>C:\games\MyGame2</c> 与 <c>C:\games\MyGame</c>
    /// 仅共享前缀，删前者绝不会碰到后者。边界字符检查就是为了这个。
    /// </summary>
    [Fact]
    public void Allows_sibling_directory_sharing_a_name_prefix()
    {
        string game = P("games", "MyGame");
        string sibling = P("games", "MyGame2");
        Assert.True(Program.ValidateOutputDirectory(game, sibling, out string reason), reason);
    }

    [Fact]
    public void Allows_unrelated_output_directory()
    {
        string game = P("games", "MyGame");
        string output = P("exports", "MyGame");
        Assert.True(Program.ValidateOutputDirectory(game, output, out string reason), reason);
    }
}
