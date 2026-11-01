using System.Drawing;
using System.Numerics;
using AssetRipperPatcher.Sprites;
using Xunit;
using Xunit.Abstractions;

namespace AssetRipperPatcher.Tests;

/// <summary>
/// SpriteUtility / SpriteMeshGenerator 移植的回归测试。
///
/// 这些用例的价值在于：移植的是「轮廓追踪 + 简化 + 三角化」这种一旦写错就只输出
/// 空网格或错网格、不会抛异常的算法，光靠编译通过完全无法判断。
/// </summary>
public class SpriteOutlineTests
{
    private readonly ITestOutputHelper _output;

    public SpriteOutlineTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>生成一张 RGBA32 纹理：不透明矩形画在 (x0,y0)-(x1,y1)，其余透明。</summary>
    private static byte[] MakeOpaqueRect(int width, int height, int x0, int y0, int x1, int y1)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                int i = (y * width + x) * 4;
                pixels[i + 0] = 255;
                pixels[i + 1] = 255;
                pixels[i + 2] = 255;
                pixels[i + 3] = 255;
            }
        }
        return pixels;
    }

    [Fact]
    public void GetSpriteMeshRectPixelBounds_clamps_to_texture_and_flags_overrun()
    {
        Rectf rect = new(1.4F, 2.6F, 98.0F, 97.2F);
        bool hasError = SpriteUtility.GetSpriteMeshRectPixelBounds(
            100, 100, rect, out int x, out int y, out int right, out int bottom);

        // Floorf(1.4)=1, Floorf(2.6)=2, Ceilf(99.4)=100, Ceilf(99.8)=100
        Assert.Equal(1, x);
        Assert.Equal(2, y);
        Assert.Equal(100, right);
        Assert.Equal(100, bottom);
        Assert.False(hasError);
    }

    [Fact]
    public void GetSpriteMeshRectPixelBounds_reports_error_when_rect_leaves_texture()
    {
        Rectf rect = new(-5.0F, -5.0F, 200.0F, 200.0F);
        bool hasError = SpriteUtility.GetSpriteMeshRectPixelBounds(
            64, 64, rect, out int x, out int y, out int right, out int bottom);

        Assert.True(hasError);
        Assert.Equal(-5, x);
        Assert.Equal(-5, y);
        // 夹回纹理尺寸
        Assert.Equal(64, right);
        Assert.Equal(64, bottom);
    }

    [Fact]
    public void GetAABBVerticesForSprite_returns_front_face_in_source_order()
    {
        Vector3[] corners = new Vector3[4];
        SpriteUtility.GetAABBVerticesForSprite(new Vector3(1, 2, 3), new Vector3(10, 20, 30), corners);

        Assert.Equal(new Vector3(-9, -18, 3), corners[0]);
        Assert.Equal(new Vector3(11, -18, 3), corners[1]);
        Assert.Equal(new Vector3(-9, 22, 3), corners[2]);
        Assert.Equal(new Vector3(11, 22, 3), corners[3]);
    }

    [Fact]
    public void GenerateSpriteOutline_square_shape_produces_mesh_covering_the_alpha()
    {
        const int size = 64;
        byte[] pixels = MakeOpaqueRect(size, size, 8, 8, 56, 56);

        List<List<Vector2>> outline = new();
        List<SpriteVertex> vertices = new();
        List<ushort> indices = new();

        SpriteUtility.GenerateSpriteOutline(
            pixels, size, size, pixelsToUnits: 1.0F,
            new Rectf(0, 0, size, size), Vector2.Zero,
            detail: -1.0F, alphaTolerance: 0, holeDetection: true, extrude: 1,
            (int)PathSimplifyMode.kPathEmbed,
            outline, vertices, indices, out Rectf meshRect);

        Assert.NotEmpty(outline);
        Assert.NotEmpty(vertices);
        Assert.NotEmpty(indices);
        Assert.Equal(0, indices.Count % 3);

        // 顶点都在物体空间，覆盖范围应接近 alpha 矩形（48x48 像素，中心在 (32,32)）
        Assert.All(vertices, v => Assert.Equal(0.0F, v.Pos.Z));
        float minX = vertices.Min(v => v.Pos.X);
        float maxX = vertices.Max(v => v.Pos.X);
        float minY = vertices.Min(v => v.Pos.Y);
        float maxY = vertices.Max(v => v.Pos.Y);
        _output.WriteLine($"meshRect=({meshRect.X},{meshRect.Y},{meshRect.Width},{meshRect.Height}) " +
            $"verts={vertices.Count} tris={indices.Count / 3} bounds=({minX},{minY})-({maxX},{maxY})");

        // 轮廓外扩 1 像素（kSpriteEdgeBias）后再 snap，所以允许 ±2 的误差
        Assert.InRange(maxX - minX, 46.0F, 52.0F);
        Assert.InRange(maxY - minY, 46.0F, 52.0F);
        // 中心大致在原点（rect 居中于纹理）
        Assert.InRange(minX + maxX, -2.0F, 2.0F);
        Assert.InRange(minY + maxY, -2.0F, 2.0F);

        // 索引全部在范围内
        Assert.All(indices, i => Assert.InRange((int)i, 0, vertices.Count - 1));
    }

    [Fact]
    public void GenerateSpriteOutline_uv_is_texture_space()
    {
        const int size = 32;
        byte[] pixels = MakeOpaqueRect(size, size, 4, 4, 28, 28);

        List<SpriteVertex> vertices = new();
        List<ushort> indices = new();

        SpriteUtility.GenerateSpriteOutline(
            pixels, size, size, pixelsToUnits: 1.0F,
            new Rectf(0, 0, size, size), Vector2.Zero,
            detail: -1.0F, alphaTolerance: 0, holeDetection: true, extrude: 0,
            (int)PathSimplifyMode.kPathEmbed,
            outLine: null, vertices, indices, out _);

        Assert.NotEmpty(vertices);
        Assert.All(vertices, v =>
        {
            Assert.InRange(v.Uv.X, 0.0F, 1.0F);
            Assert.InRange(v.Uv.Y, 0.0F, 1.0F);
        });
    }

    [Fact]
    public void GenerateSpriteOutline_empty_alpha_yields_no_mesh()
    {
        const int size = 32;
        byte[] pixels = new byte[size * size * 4]; // 全透明

        List<SpriteVertex> vertices = new();
        List<ushort> indices = new();

        SpriteUtility.GenerateSpriteOutline(
            pixels, size, size, pixelsToUnits: 1.0F,
            new Rectf(0, 0, size, size), Vector2.Zero,
            detail: -1.0F, alphaTolerance: 0, holeDetection: true, extrude: 0,
            (int)PathSimplifyMode.kPathEmbed,
            outLine: null, vertices, indices, out _);

        Assert.Empty(vertices);
        Assert.Empty(indices);
    }

    [Fact]
    public void GenerateSpriteOutline_with_hole_detection_keeps_hole_contour()
    {
        const int size = 64;
        byte[] pixels = MakeOpaqueRect(size, size, 8, 8, 56, 56);
        // 挖掉中间 16x16 的方孔
        for (int y = 24; y < 40; y++)
        {
            for (int x = 24; x < 40; x++)
            {
                int i = (y * size + x) * 4;
                pixels[i + 3] = 0;
            }
        }

        List<List<Vector2>> outline = new();
        SpriteUtility.GenerateSpriteOutline(
            pixels, size, size, pixelsToUnits: 1.0F,
            new Rectf(0, 0, size, size), Vector2.Zero,
            // detail >= 0 => hullTolerance = 1 - detail = 0，此时不会因为「孔太小」被裁掉
            detail: 1.0F, alphaTolerance: 0, holeDetection: true, extrude: 0,
            (int)PathSimplifyMode.kPathEmbed,
            outline, outVertices: null, outIndices: null, out _);

        _output.WriteLine($"contours={outline.Count}");
        // 外轮廓 + 内孔
        Assert.True(outline.Count >= 2, $"expected outer + hole contour, got {outline.Count}");
    }

    /// <summary>
    /// 原版 MakeShape 的孔裁剪规则：孔面积 &lt; 25% 且 hullTolerance &lt; 0（自动 LOD）时直接丢弃。
    /// 这是 SpriteMeshGenerator 的既有行为，移植必须照做。
    /// </summary>
    [Fact]
    public void GenerateSpriteOutline_small_hole_is_pruned_when_lod_is_automatic()
    {
        const int size = 64;
        byte[] pixels = MakeOpaqueRect(size, size, 8, 8, 56, 56);
        for (int y = 28; y < 36; y++)
        {
            for (int x = 28; x < 36; x++)
            {
                pixels[(y * size + x) * 4 + 3] = 0;
            }
        }

        List<List<Vector2>> automaticLod = new();
        SpriteUtility.GenerateSpriteOutline(
            pixels, size, size, 1.0F, new Rectf(0, 0, size, size), Vector2.Zero,
            detail: -1.0F, alphaTolerance: 0, holeDetection: true, extrude: 0,
            (int)PathSimplifyMode.kPathEmbed, automaticLod, null, null, out _);

        List<List<Vector2>> fixedLod = new();
        SpriteUtility.GenerateSpriteOutline(
            pixels, size, size, 1.0F, new Rectf(0, 0, size, size), Vector2.Zero,
            detail: 1.0F, alphaTolerance: 0, holeDetection: true, extrude: 0,
            (int)PathSimplifyMode.kPathEmbed, fixedLod, null, null, out _);

        _output.WriteLine($"automatic={automaticLod.Count} fixed={fixedLod.Count}");
        // 8x8 孔 = 64 px²，远小于 64*64*0.25 = 1024
        Assert.True(automaticLod.Count < fixedLod.Count,
            $"automatic LOD should drop the small hole ({automaticLod.Count} vs {fixedLod.Count})");
    }

    [Fact]
    public void GenerateQuadMesh_matches_unity_vertex_order()
    {
        List<SpriteVertex> vertices = new();
        List<ushort> indices = new();

        SpriteUtility.GenerateQuadMesh(
            64, 64, new Rectf(0, 0, 64, 64), Vector2.Zero, pixelsToUnits: 1.0F,
            vertices, indices);

        Assert.Equal(4, vertices.Count);
        Assert.Equal([0, 1, 2, 2, 1, 3], indices);

        // (-32,+32), (+32,+32), (-32,-32), (+32,-32)
        Assert.Equal(new Vector3(-32, 32, 0), vertices[0].Pos);
        Assert.Equal(new Vector3(32, 32, 0), vertices[1].Pos);
        Assert.Equal(new Vector3(-32, -32, 0), vertices[2].Pos);
        Assert.Equal(new Vector3(32, -32, 0), vertices[3].Pos);
    }

    [Fact]
    public void CompareFloatRobust_matches_ulp_semantics()
    {
        Assert.True(SpriteMath.CompareFloatRobust(0.0F, 0.0F));
        Assert.True(SpriteMath.CompareFloatRobust(0.0F, -0.0F)); // 零与负零相等
        Assert.True(SpriteMath.CompareFloatRobust(1.0F, BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(1.0F) + 5)));
        Assert.False(SpriteMath.CompareFloatRobust(1.0F, 2.0F));
        Assert.False(SpriteMath.CompareFloatRobust(0.0F, 1e-30F));
    }

    [Fact]
    public void QuadraticPolynomialRootsGeneric_handles_linear_and_no_root_cases()
    {
        // a≈0, b≠0 -> 一个根
        float r0 = -1, r1 = -1;
        Assert.Equal(1, SpriteMath.QuadraticPolynomialRootsGeneric(0.0F, 2.0F, -4.0F, ref r0, ref r1));
        Assert.Equal(2.0F, r0, 4);

        // 判别式 < 0 -> 无实根
        r0 = -1;
        r1 = -1;
        Assert.Equal(0, SpriteMath.QuadraticPolynomialRootsGeneric(1.0F, 0.0F, 1.0F, ref r0, ref r1));
        Assert.Equal(-1.0F, r0);
        Assert.Equal(-1.0F, r1);

        // x^2 - 3x + 2 -> 根 2 和 1
        r0 = -1;
        r1 = -1;
        Assert.Equal(2, SpriteMath.QuadraticPolynomialRootsGeneric(1.0F, -3.0F, 2.0F, ref r0, ref r1));
        Assert.Equal(2.0F, Math.Max(r0, r1), 4);
        Assert.Equal(1.0F, Math.Min(r0, r1), 4);
    }

    /// <summary>
    /// pivot 修复：越界的 m_Pivot（实测某子 Sprite 存了 2.3704972，
    /// 即「整图中心像素 493 ÷ rect 宽度 208」）必须换回 [0,1]。
    /// 这里锁住质心算法的数学：质心应该落在 alpha 区域中心。
    /// </summary>
    [Fact]
    public void AlphaCentroid_returns_visual_center_of_the_alpha_region()
    {
        // 100x50 纹理，rect 覆盖左半边 (0..40)，alpha 画在 x 10..30, y 10..30
        const int w = 100;
        const int h = 50;
        byte[] pixels = new byte[w * h * 4];
        for (int y = 10; y < 30; y++)
        {
            for (int x = 10; x < 30; x++)
            {
                pixels[(y * w + x) * 4 + 3] = 255;
            }
        }

        Vector2? centroid = AssetRipperPatcher.Patches.SpriteOutlinePatch.AlphaCentroid(
            pixels, w, h, rectX: 0.0F, rectY: 0.0F, rectWidth: 40.0F, rectHeight: 50.0F);

        Assert.NotNull(centroid);
        // alpha 中心 (19.5, 19.5) → 相对 rect 归一化
        Assert.Equal(19.5F / 40.0F, centroid!.Value.X, 3);
        Assert.Equal(19.5F / 50.0F, centroid.Value.Y, 3);
        // 关键：结果必须落在 [0,1]，否则 importer 的 spritePivot 非法
        Assert.InRange(centroid.Value.X, 0.0F, 1.0F);
        Assert.InRange(centroid.Value.Y, 0.0F, 1.0F);
    }

    [Fact]
    public void AlphaCentroid_returns_null_when_rect_has_no_alpha()
    {
        const int w = 40;
        const int h = 40;
        byte[] pixels = new byte[w * h * 4]; // 全透明

        Vector2? centroid = AssetRipperPatcher.Patches.SpriteOutlinePatch.AlphaCentroid(
            pixels, w, h, 0.0F, 0.0F, 40.0F, 40.0F);

        Assert.Null(centroid);
    }

    [Fact]
    public void AlphaCentroid_ignores_alpha_outside_the_rect()
    {
        const int w = 100;
        const int h = 40;
        byte[] pixels = new byte[w * h * 4];
        // rect 内 (0..20) 有 alpha
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                pixels[(y * w + x) * 4 + 3] = 255;
            }
        }
        // rect 外 (60..90) 也有 alpha，必须被忽略
        for (int y = 0; y < h; y++)
        {
            for (int x = 60; x < 90; x++)
            {
                pixels[(y * w + x) * 4 + 3] = 255;
            }
        }

        Vector2? centroid = AssetRipperPatcher.Patches.SpriteOutlinePatch.AlphaCentroid(
            pixels, w, h, rectX: 0.0F, rectY: 0.0F, rectWidth: 20.0F, rectHeight: 40.0F);

        Assert.NotNull(centroid);
        // 只算 rect 内：x 中心 9.5/20
        Assert.Equal(9.5F / 20.0F, centroid!.Value.X, 3);
    }

    [Theory]
    [InlineData(0.0F, 0.0F, 64.0F, 64.0F)]      // 正好整图
    [InlineData(8.0F, 8.0F, 48.0F, 48.0F)]      // 内缩
    [InlineData(3.4F, 2.6F, 57.3F, 49.7F)]      // 非整数边界（floor/ceil 生效）
    [InlineData(0.5F, 0.5F, 63.5F, 63.5F)]
    public void Sub_image_decode_matches_full_image_result(
        float rectX, float rectY, float rectW, float rectH)
    {
        const int size = 64;
        byte[] full = MakeOpaqueRect(size, size, 6, 5, 58, 60);
        const float pixelsToUnits = 1.0F;
        const uint extrude = 1;
        Rectf rect = new(rectX, rectY, rectW, rectH);
        Vector2 offset = new(0.25F, -0.75F);

        // 参照：整图 + GenerateSpriteOutline
        List<SpriteVertex> refVerts = new();
        List<ushort> refIndices = new();
        SpriteUtility.GenerateSpriteOutline(
            full, size, size, pixelsToUnits, rect, offset,
            detail: -1.0F, alphaTolerance: 0, holeDetection: true, extrude,
            (int)PathSimplifyMode.kPathEmbed,
            null, refVerts, refIndices, out Rectf refRect);

        // 被测：按补丁的方式裁出子图 + GenerateSpriteOutlineFromPixels
        SpriteUtility.GetSpriteMeshRectPixelBounds(size, size, rect,
            out int x0, out int y0, out int x1, out int y1);
        x0 = Math.Max(x0, 0);
        y0 = Math.Max(y0, 0);
        x1 = Math.Min(x1, size);
        y1 = Math.Min(y1, size);
        int w = x1 - x0;
        int h = y1 - y0;

        byte[] sub = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
        {
            Buffer.BlockCopy(full, ((y0 + row) * size + x0) * 4, sub, row * w * 4, w * 4);
        }

        List<SpriteVertex> subVerts = new();
        List<ushort> subIndices = new();
        SpriteUtility.GenerateSpriteOutlineFromPixels(
            sub, w, h, x0, y0, size, size, pixelsToUnits, rect, offset,
            detail: -1.0F, alphaTolerance: 0, holeDetection: true, extrude,
            (int)PathSimplifyMode.kPathEmbed,
            null, subVerts, subIndices, out Rectf subRect);

        _output.WriteLine($"rect=({rectX},{rectY},{rectW},{rectH}) slice=({x0},{y0})-({x1},{y1})");
        _output.WriteLine($"ref: verts={refVerts.Count} tris={refIndices.Count / 3} rect={refRect.X},{refRect.Y},{refRect.Width},{refRect.Height}");
        _output.WriteLine($"sub: verts={subVerts.Count} tris={subIndices.Count / 3} rect={subRect.X},{subRect.Y},{subRect.Width},{subRect.Height}");

        Assert.Equal(refVerts.Count, subVerts.Count);
        Assert.Equal(refIndices, subIndices);
        for (int i = 0; i < refVerts.Count; i++)
        {
            Assert.Equal(refVerts[i].Pos.X, subVerts[i].Pos.X, 3);
            Assert.Equal(refVerts[i].Pos.Y, subVerts[i].Pos.Y, 3);
            Assert.Equal(refVerts[i].Uv.X, subVerts[i].Uv.X, 5);
            Assert.Equal(refVerts[i].Uv.Y, subVerts[i].Uv.Y, 5);
        }
        Assert.Equal(refRect.X, subRect.X, 3);
        Assert.Equal(refRect.Y, subRect.Y, 3);
        Assert.Equal(refRect.Width, subRect.Width, 3);
        Assert.Equal(refRect.Height, subRect.Height, 3);
    }
}
