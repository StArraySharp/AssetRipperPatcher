using System.Numerics;
using AssetRipperPatcher.Sprites;
using Xunit;
using Xunit.Abstractions;

namespace AssetRipperPatcher.Tests;

/// <summary>
/// alpha 容差语义的回归测试。
///
/// 依据 Unity 源码 <c>Runtime/Geometry/SpriteMeshGenerator.h</c> 的 <c>mask</c> 构造函数：
/// <code>if (img[x+y*width].a &gt; acut) bv.set(x+y*w);</code>
/// 即容差是「严格大于才算实体像素」，254 只放行 alpha=255。
/// 对应 Unity 的三个常量：
/// <list type="bullet">
/// <item><c>kSpriteDefaultAlphaTolerance = 0</c>（运行时渲染网格）</item>
/// <item><c>kSpriteEditorDefaultAlphaTolerance = 200</c>（编辑器生成 outline 的默认值）</item>
/// <item><c>kSpriteMaxAlphaTolerance = 254</c>（仅 UI 滑块上限，<c>clamp(0, 254)</c>）</item>
/// </list>
/// </summary>
public class AlphaToleranceTests
{
    private readonly ITestOutputHelper _output;

    public AlphaToleranceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>抗锯齿圆盘：边缘 1 像素从满值平滑降到 0。</summary>
    private static byte[] MakeSoftCircle(int size, float radius, byte peak)
    {
        byte[] pixels = new byte[size * size * 4];
        float center = size * 0.5F;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5F - center;
                float dy = y + 0.5F - center;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                float coverage = Math.Clamp(radius + 0.5F - dist, 0.0F, 1.0F);
                int i = (y * size + x) * 4;
                pixels[i + 0] = 255;
                pixels[i + 1] = 255;
                pixels[i + 2] = 255;
                pixels[i + 3] = (byte)MathF.Round(coverage * peak);
            }
        }
        return pixels;
    }

    private static List<List<Vector2>> Outline(byte[] pixels, int size, byte tolerance)
    {
        List<List<Vector2>> outline = new();
        SpriteUtility.GenerateSpriteOutline(
            pixels, size, size, pixelsToUnits: 1.0F,
            new Rectf(0, 0, size, size), Vector2.Zero,
            detail: 1.0F, alphaTolerance: tolerance, holeDetection: true, extrude: 0,
            (int)PathSimplifyMode.kPathEmbed,
            outline, outVertices: null, outIndices: null, out _);
        return outline;
    }

    /// <summary>
    /// 容差判定是「alpha &gt; tolerance」的严格大于语义。
    /// 峰值恰好 200 的素材在 200 下必须全空（199 才算全部纳入）。
    /// </summary>
    [Fact]
    public void Tolerance_is_strictly_greater_than()
    {
        const int size = 48;
        byte[] flat = MakeSoftCircle(size, 16.0F, 200);
        // 抹掉抗锯齿过渡，让所有非零像素都恰好等于 200
        for (int i = 3; i < flat.Length; i += 4)
        {
            if (flat[i] > 0)
            {
                flat[i] = 200;
            }
        }

        _output.WriteLine($"tol=200 -> contours={Outline(flat, size, 200).Count}");
        _output.WriteLine($"tol=199 -> contours={Outline(flat, size, 199).Count}");

        Assert.Empty(Outline(flat, size, 200));
        Assert.NotEmpty(Outline(flat, size, 199));
    }

    /// <summary>
    /// 254（UI 滑块上限）会把半透明素材整块判成背景。
    /// 这正是不能用 254 当头号容差的原因：Unity 编辑器默认值是 200。
    /// </summary>
    [Fact]
    public void Max_tolerance_254_drops_faint_artwork_but_200_keeps_it()
    {
        const int size = 64;
        byte[] faint = MakeSoftCircle(size, 24.0F, 200);

        int atMax = Outline(faint, size, 254).Count;
        int atDefault = Outline(faint, size, 200).Count;
        _output.WriteLine($"peak-200 artwork: tol=254 -> {atMax}, tol=200 -> {atDefault}");

        Assert.Equal(0, atMax);
        Assert.Equal(0, atDefault); // 峰值恰好 200，严格大于语义下同样全空

        // 峰值 210 时，编辑器默认值 200 能正常出轮廓，254 依然全空
        byte[] brighter = MakeSoftCircle(size, 24.0F, 210);
        int atMaxBrighter = Outline(brighter, size, 254).Count;
        int atDefaultBrighter = Outline(brighter, size, 200).Count;
        _output.WriteLine($"peak-210 artwork: tol=254 -> {atMaxBrighter}, tol=200 -> {atDefaultBrighter}");

        Assert.Equal(0, atMaxBrighter);
        Assert.NotEmpty(Outline(brighter, size, 200));
    }

    /// <summary>
    /// 254 还会吃掉抗锯齿边缘：同一条软边圆，254 的点数与包围盒覆盖率都明显缩水。
    /// </summary>
    [Fact]
    public void Max_tolerance_254_shrinks_anti_aliased_edges()
    {
        const int size = 64;
        byte[] soft = MakeSoftCircle(size, 24.0F, 255);

        List<List<Vector2>> at254 = Outline(soft, size, 254);
        List<List<Vector2>> at200 = Outline(soft, size, 200);

        int points254 = at254.Sum(c => c.Count);
        int points200 = at200.Sum(c => c.Count);
        _output.WriteLine($"points: tol=254 -> {points254}, tol=200 -> {points200}");

        Assert.True(points200 > points254,
            $"编辑器默认值 200 应保留更多轮廓点（200:{points200} vs 254:{points254}）");
    }
}
