using System.Numerics;
using AssetRipperPatcher.Patches;
using AssetRipperPatcher.Sprites;
using Xunit;
using Xunit.Abstractions;

namespace AssetRipperPatcher.Tests;

/// <summary>
/// <c>SpriteOutlineRepairPatch.BuildOutlineFromPixels</c> 的 alpha 容差降级回归。
///
/// 这一层容易写错的地方很具体：如果「有轮廓就返回」，某个只在最高容差下产出的
/// 3~4 点碎片会被当成结果，此后永不降级——而更低容差明明能得到完整轮廓。
/// 正确策略是「只有可用才采纳，否则降级；全部不可用才退回第一份非空」。
/// </summary>
public class SpriteAlphaFallbackTests
{
    private readonly ITestOutputHelper _output;

    public SpriteAlphaFallbackTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private const int Size = 64;

    /// <summary>抗锯齿圆盘；peak 控制整张图的最大 alpha。</summary>
    private static byte[] MakeSoftCircle(float radius, byte peak)
    {
        byte[] pixels = new byte[Size * Size * 4];
        float center = Size * 0.5F;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float dx = x + 0.5F - center;
                float dy = y + 0.5F - center;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                float coverage = Math.Clamp(radius + 0.5F - dist, 0.0F, 1.0F);
                int i = (y * Size + x) * 4;
                pixels[i + 0] = 255;
                pixels[i + 1] = 255;
                pixels[i + 2] = 255;
                pixels[i + 3] = (byte)MathF.Round(coverage * peak);
            }
        }
        return pixels;
    }

    private List<List<Vector2>> Build(byte[] pixels)
    {
        Rectf rect = new(0.0F, 0.0F, Size, Size);
        return SpriteOutlineRepairPatch.BuildOutlineFromPixels(
            pixels, Size, Size, rect, Vector2.Zero,
            pixelsToUnits: 1.0F, rectWidth: Size, rectHeight: Size, spriteName: "test");
    }

    private int Points(List<List<Vector2>> contours) => contours.Sum(c => c.Count);

    [Fact]
    public void Opaque_artwork_is_usable_at_the_primary_tolerance()
    {
        List<List<Vector2>> contours = Build(MakeSoftCircle(24.0F, 255));
        _output.WriteLine($"contours={contours.Count} points={Points(contours)}");
        Assert.NotEmpty(contours);
        Assert.True(Points(contours) >= 8, "不透明素材应该产出完整轮廓");
    }

    /// <summary>
    /// 峰值恰好等于首选容差（200）→ 该档一个实体像素都没有，必须降级而不是返回空。
    /// 这正是「Alpha8 遮罩 / 低峰值光晕」在真实素材里的表现。
    /// </summary>
    [Fact]
    public void Faint_artwork_degrades_instead_of_vanishing()
    {
        List<List<Vector2>> contours = Build(MakeSoftCircle(24.0F, 200));
        _output.WriteLine($"contours={contours.Count} points={Points(contours)}");
        Assert.NotEmpty(contours);
        Assert.True(Points(contours) >= 8, "降级后应拿到可用轮廓，而不是空结果");
    }

    /// <summary>
    /// 峰值 60：200 和 128 两档都选不出实体像素，只有 0 档能出轮廓。
    /// 用来锁住「降级要一路降到最低档」。
    /// </summary>
    [Fact]
    public void Very_faint_artwork_reaches_the_lowest_tolerance()
    {
        List<List<Vector2>> contours = Build(MakeSoftCircle(24.0F, 60));
        _output.WriteLine($"contours={contours.Count} points={Points(contours)}");
        Assert.NotEmpty(contours);
    }

    [Fact]
    public void Fully_transparent_artwork_yields_nothing()
    {
        byte[] empty = new byte[Size * Size * 4];
        List<List<Vector2>> contours = Build(empty);
        Assert.Empty(contours);
    }

    /// <summary>
    /// 全空输入必须返回空列表而不是 null —— 调用方用 <c>Count &gt; 0</c> 判断，
    /// 返回 null 会让调用方多一条判空分支（而且容易被漏掉）。
    /// </summary>
    [Fact]
    public void Result_is_never_null_for_decoded_pixels()
    {
        Assert.NotNull(Build(new byte[Size * Size * 4]));
    }
}