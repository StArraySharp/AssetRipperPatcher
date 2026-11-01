using System.Numerics;
using System.Reflection;
using AssetRipper.Assets;
using AssetRipper.Assets.Generics;
using AssetRipper.Export.Modules.Textures;
using AssetRipper.IO.Files;
using AssetRipper.Processing;
using AssetRipper.Processing.Textures;
using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Enums;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Subclasses.ChannelInfo;
using AssetRipper.SourceGenerated.Subclasses.SpriteRenderData;
using AssetRipper.SourceGenerated.Subclasses.SpriteVertex;
using AssetRipper.SourceGenerated.Subclasses.StreamInfo;
using AssetRipper.SourceGenerated.Subclasses.SubMesh;
using AssetRipper.SourceGenerated.Subclasses.VertexData;
using AssetRipperPatcher.Patching;
using AssetRipperPatcher.Sprites;
using AssetRipper.TextureDecoder.Rgb;
using AssetRipper.TextureDecoder.Rgb.Formats;
using SpriteVertex = AssetRipperPatcher.Sprites.SpriteVertex;

namespace AssetRipperPatcher.Patches;

/// <summary>
/// Sprite 紧致网格补丁。
///
/// 背景：Unity 在编辑器里对 <c>SpriteMeshType.Tight</c> 的 Sprite 调用
/// <c>SpriteRenderData::GenerateFullMesh</c> → <c>GenerateSpriteOutline</c>（即本仓库
/// <c>Sprites/SpriteUtility.cs</c> 与 <c>Sprites/SpriteMeshGenerator.cs</c> 的移植源），
/// 把 alpha 轮廓三角化成 m_RD 的 vertices/indices。
/// AssetRipper 直接搬运序列化数据，不重算；一旦原始数据缺失或与导出的纹理不再匹配
/// （图集重打包、纹理被裁切、m_RD 被 SpriteAtlas 覆盖），导出的 Sprite 就会没有网格。
///
/// 本补丁在 <see cref="SpriteProcessor.Process(GameData)"/> 之后，对每个 Tight Sprite
/// 用导出流程真正会读的那份纹理像素重算一遍网格，写回 m_RD。
/// </summary>
internal static class SpriteOutlinePatch
{
    /// <summary>
    /// 原版 <c>kMinSpriteDimensionForMesh</c>（Runtime/Graphics/SpriteFrame.cpp）：
    /// 小于 32 像素的 Sprite 一律铺四边形，不走轮廓生成。
    /// </summary>
    private const int kMinSpriteDimensionForMesh = 32;

    /// <summary>原版 <c>kSpriteDefaultDetail</c>：负数表示按面积自动估算 LOD。</summary>
    private const float kSpriteDefaultDetail = -1.0F;

    /// <summary>原版 <c>kSpriteDefaultAlphaTolerance</c>。</summary>
    private const byte kSpriteDefaultAlphaTolerance = 0;

    /// <summary>原版两者都用 <c>kPathEmbed</c>（见 SpriteRenderData::GenerateFullMesh）。</summary>
    private const PathSimplifyMode kSimplifyMode = PathSimplifyMode.kPathEmbed;

    [PatchMethod(typeof(SpriteProcessor), nameof(SpriteProcessor.Process), PatchType = PatchType.Postfix)]
    public static void RegenerateTightSpriteMeshes(GameData gameData)
    {
        try
        {
            RebuildAll(gameData);
        }
        catch (Exception ex)
        {
            // 补丁失败不能影响导出本身：只报告，不抛。
            Console.WriteLine($"[SpriteOutline] skipped: {ex.Message}");
        }
    }

    /// <summary>
    /// 给测试和诊断用的直达入口：对给定 GameData 重算一遍 Tight Sprite 网格。
    /// </summary>
    internal static int RegenerateTightSpriteMeshesForTesting(GameData gameData) => RebuildAll(gameData);

    private static int RebuildAll(GameData gameData)
    {
        int rebuilt = 0;
        int checkedCount = 0;
        int pivotsFixed = 0;

        foreach (IUnityObjectBase asset in gameData.GameBundle
            .FetchAssetCollections()
            .Where(c => !SpecialFileNames.IsDefaultResourceOrBuiltinExtra(c.Name))
            .SelectMany(c => c))
        {
            if (asset is not ISprite sprite)
            {
                continue;
            }
            checkedCount++;
            if (TryFixPivot(sprite))
            {
                pivotsFixed++;
            }
            if (TryRebuild(sprite))
            {
                rebuilt++;
            }
        }

        // 缓存里存的是纹理像素，跑完必须清掉，否则整轮导出期间会一直攥着这些大数组。
        PixelCache.Clear();

        if (pivotsFixed > 0)
        {
            Console.WriteLine($"[SpriteOutline] fixed out-of-range pivot on {pivotsFixed} sprite(s)");
        }
        if (rebuilt > 0)
        {
            Console.WriteLine($"[SpriteOutline] regenerated tight mesh for {rebuilt}/{checkedCount} sprite(s)");
        }
        return rebuilt;
    }

    /// <summary>
    /// 修复越界的 <c>m_Pivot</c>。
    ///
    /// AssetRipper 会原样搬运 <c>m_Pivot</c>，但有些游戏里从大图裁出来的子 Sprite
    /// 存着非法值（Unity 6 项目实测）：
    /// <code>
    /// m_Rect = (0, 5.076, 207.97, 182.85), 纹理 986x190
    /// m_Pivot.x = 2.3704972
    /// 2.3704972 * 207.97 = 493.0 = 986 / 2   ← 整图中心像素
    /// </code>
    /// 即分子是「整图绝对像素」、分母却是「rect 宽度」。这个值会被
    /// <c>SpriteMetaDataExtensions</c> 直接写进 importer 的 <c>spritePivot</c>，
    /// 而 Unity 要求它是 [0,1]；越界后整个网格被推出矩形——正是「导出的 Sprite 边界偏移」。
    ///
    /// 修复：pivot 越界时改用纹理 alpha 的质心（相对 rect 归一化）。
    /// pivot 本质是视觉锚点，对这类 logo Sprite 质心与作者意图一致
    /// （实测上述子 Sprite 的视觉中心都是 0.49~0.50）。
    ///
    /// <b>网格顶点必须一起平移</b>：<c>m_RD</c> 顶点是「相对 pivot」的物体空间坐标，
    /// pivot 一动，同一批顶点指向的图像位置就变了。平移量 = (旧pivot - 新pivot) * rectSize / ppu，
    /// 方向取反，才能让网格视觉位置保持不变。
    /// </summary>
    private static bool TryFixPivot(ISprite sprite)
    {
        if (!sprite.Has_Pivot())
        {
            return false;
        }

        Vector2 pivot = new(sprite.Pivot.X, sprite.Pivot.Y);
        if (pivot.X is >= 0.0F and <= 1.0F && pivot.Y is >= 0.0F and <= 1.0F)
        {
            return false;
        }

        Vector2 fixedPivot = PivotFromAlphaCentroid(sprite) ?? new Vector2(0.5F, 0.5F);

        // 网格顶点是「相对 pivot」的，pivot 从旧值改到新值时，
        // 同一像素点的物体空间坐标要反向平移，才能停在原来的图像位置。
        float pixelsToUnits = sprite.PixelsToUnits;
        if (pixelsToUnits > 0.0F)
        {
            float shiftX = (pivot.X - fixedPivot.X) * sprite.Rect.Width / pixelsToUnits;
            float shiftY = (pivot.Y - fixedPivot.Y) * sprite.Rect.Height / pixelsToUnits;
            ShiftMeshVertices(sprite, shiftX, shiftY);
        }

        sprite.Pivot.X = fixedPivot.X;
        sprite.Pivot.Y = fixedPivot.Y;

        // Offset 与 Pivot 是同一套语义（Offset = (Pivot - 0.5) * RectSize），必须同步修正，
        // 否则 SpriteProcessor 后面会用它覆盖掉正确值。
        Vector2 offset = new(
            (fixedPivot.X - 0.5F) * sprite.Rect.Width,
            (fixedPivot.Y - 0.5F) * sprite.Rect.Height);
        sprite.Offset.X = offset.X;
        sprite.Offset.Y = offset.Y;
        return true;
    }

    /// <summary>
    /// 平移 <c>m_RD</c> 的网格顶点（两种存储格式都覆盖：旧版 Vertices、新版 VertexData）。
    /// </summary>
    private static void ShiftMeshVertices(ISprite sprite, float shiftX, float shiftY)
    {
        if (shiftX == 0.0F && shiftY == 0.0F)
        {
            return;
        }

        ISpriteRenderData rd = sprite.RD;
        if (rd.Has_Vertices())
        {
            AccessListBase<ISpriteVertex> vertices = rd.Vertices;
            for (int i = 0; i < vertices.Count; i++)
            {
                ISpriteVertex vertex = vertices[i];
                vertex.Pos.X += shiftX;
                vertex.Pos.Y += shiftY;
            }
        }

        if (rd.Has_VertexData() && rd.VertexData.VertexCount > 0)
        {
            ShiftVertexDataPositions(rd.VertexData, shiftX, shiftY);
        }
    }

    /// <summary>
    /// 就地平移 VertexData 里 stream 0 的 float3 position（stride 由 Channels/Streams 决定）。
    /// </summary>
    private static void ShiftVertexDataPositions(IVertexData vertexData, float shiftX, float shiftY)
    {
        int vertexCount = (int)vertexData.VertexCount;
        int stride = GetPositionStride(vertexData);
        if (stride <= 0 || vertexCount <= 0)
        {
            return;
        }

        byte[] data = vertexData.Data;
        int positionOffset = GetPositionStreamOffset(vertexData);
        for (int i = 0; i < vertexCount; i++)
        {
            int offset = positionOffset + i * stride;
            if (offset + 8 >= data.Length)
            {
                break;
            }
            float x = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset, 4));
            float y = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 4, 4));
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(offset, 4), x + shiftX);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(offset + 4, 4), y + shiftY);
        }
    }

    /// <summary>stream 0 的顶点 stride（字节）。</summary>
    private static int GetPositionStride(IVertexData vertexData)
    {
        if (vertexData.Has_Streams() && vertexData.Streams.Count > 0)
        {
            return vertexData.Streams[0].Stride_Byte;
        }
        if (vertexData.Has_Channels())
        {
            int stride = 0;
            foreach (ChannelInfo channel in vertexData.Channels)
            {
                if (channel.Dimension > 0 && channel.Stream == 0)
                {
                    stride += (channel.Dimension & 0x0F) * 4; // float 分量
                }
            }
            return stride;
        }
        return 0;
    }

    /// <summary>stream 0 在 Data 里的起始偏移（前面的 stream 按 16 字节对齐）。</summary>
    private static int GetPositionStreamOffset(IVertexData vertexData)
    {
        if (!vertexData.Has_Streams() || vertexData.Streams.Count == 0)
        {
            return 0;
        }
        return (int)vertexData.Streams[0].Offset;
    }

    /// <summary>
    /// 纹理 alpha 质心，相对 <c>sprite.Rect</c> 归一化。只解码 rect 覆盖的那一小块像素。
    /// </summary>
    private static Vector2? PivotFromAlphaCentroid(ISprite sprite)
    {
        ITexture2D? texture = sprite.TryGetTexture();
        if (texture is null)
        {
            return null;
        }
        int texW = texture.Width_C28;
        int texH = texture.Height_C28;
        if (texW <= 0 || texH <= 0 || sprite.Rect.Width <= 0.0F || sprite.Rect.Height <= 0.0F)
        {
            return null;
        }
        if (!TryGetFullRgba32(texture, texW, texH, out byte[] pixels))
        {
            return null;
        }

        return AlphaCentroid(
            pixels, texW, texH,
            sprite.Rect.X, sprite.Rect.Y, sprite.Rect.Width, sprite.Rect.Height);
    }

    /// <summary>
    /// 纯函数：给定 RGBA32 像素与 rect（整图坐标），返回 alpha 质心相对 rect 的归一化坐标。
    /// 质心越界或无 alpha 时返回 null。抽成 public 是为了单元测试。
    /// </summary>
    internal static Vector2? AlphaCentroid(
        ReadOnlySpan<byte> pixels,
        int textureWidth,
        int textureHeight,
        float rectX,
        float rectY,
        float rectWidth,
        float rectHeight)
    {
        if (textureWidth <= 0 || textureHeight <= 0 || rectWidth <= 0.0F || rectHeight <= 0.0F)
        {
            return null;
        }

        int x0 = Math.Clamp((int)SpriteMath.Floorf(rectX), 0, textureWidth);
        int y0 = Math.Clamp((int)SpriteMath.Floorf(rectY), 0, textureHeight);
        int x1 = Math.Clamp((int)SpriteMath.Ceilf(rectX + rectWidth), 0, textureWidth);
        int y1 = Math.Clamp((int)SpriteMath.Ceilf(rectY + rectHeight), 0, textureHeight);

        long sumX = 0;
        long sumY = 0;
        long count = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                int index = ((y * textureWidth) + x) * 4;
                if (index + 3 >= pixels.Length)
                {
                    continue;
                }
                if (pixels[index + 3] > 0)
                {
                    sumX += x;
                    sumY += y;
                    count++;
                }
            }
        }
        if (count == 0)
        {
            return null;
        }

        float normalizedX = ((float)sumX / count - rectX) / rectWidth;
        float normalizedY = ((float)sumY / count - rectY) / rectHeight;
        if (normalizedX is < 0.0F or > 1.0F || normalizedY is < 0.0F or > 1.0F)
        {
            return null;
        }
        return new Vector2(normalizedX, normalizedY);
    }

    /// <summary>
    /// 只处理 Tight 网格且确实缺网格数据的 Sprite：已经有合法网格的保持原样，
    /// 避免用我们的近似实现覆盖掉游戏里精确的原始数据。
    /// </summary>
    private static bool TryRebuild(ISprite sprite)
    {
        ISpriteRenderData rd = sprite.RD;
        if (rd.MeshType != SpriteMeshType.Tight)
        {
            return false;
        }
        if (HasMesh(rd))
        {
            return false;
        }

        ITexture2D? texture = sprite.TryGetTexture();
        if (texture is null)
        {
            return false;
        }

        int texW = texture.Width_C28;
        int texH = texture.Height_C28;
        if (texW <= 0 || texH <= 0)
        {
            return false;
        }

        Rectf rect = new(sprite.Rect.X, sprite.Rect.Y, sprite.Rect.Width, sprite.Rect.Height);

        // 只解码 Sprite 实际用到的那块像素：整张图集（2048x2048 = 16MB）里
        // 每个 Sprite 往往只占几百像素，全量解码再切片会让峰值内存翻几十倍。
        if (!TryGetSubImage(texture, rect, texW, texH, out byte[] pixels, out Rectf pixelRect))
        {
            return false;
        }

        Vector2 offset = new(sprite.Offset.X, sprite.Offset.Y);
        uint extrude = sprite.Extrude;

        // 与原版 Sprite::Initialize 的 generateRenderMesh 判定一致
        bool generateRenderMesh = rect.Width >= kMinSpriteDimensionForMesh
            && rect.Height >= kMinSpriteDimensionForMesh;

        List<SpriteVertex> vertices = new();
        List<ushort> indices = new();
        Rectf textureRect = rect;

        if (generateRenderMesh)
        {
            SpriteUtility.GenerateSpriteOutlineFromPixels(
                pixels, (int)pixelRect.Width, (int)pixelRect.Height,
                (int)pixelRect.X, (int)pixelRect.Y,
                texW, texH, sprite.PixelsToUnits, rect, offset,
                kSpriteDefaultDetail, kSpriteDefaultAlphaTolerance, holeDetection: true,
                extrude, (int)kSimplifyMode,
                outLine: null, vertices, indices, out textureRect);

            // 原版在 GenerateFullMesh 之后把 meshRect 的偏移加回去
            textureRect.X += rect.X;
            textureRect.Y += rect.Y;

            if (vertices.Count == 0 || indices.Count == 0)
            {
                generateRenderMesh = false;
            }
        }

        if (!generateRenderMesh)
        {
            SpriteUtility.GenerateQuadMesh(texW, texH, rect, offset, sprite.PixelsToUnits, vertices, indices);
            textureRect = rect;
        }

        if (vertices.Count == 0 || indices.Count == 0)
        {
            return false;
        }

        WriteMesh(rd, texture, texW, texH, vertices, indices, textureRect, rect);
        return true;
    }

    /// <summary>m_RD 是否已经带可用网格（Tight 网格正常时 AssetRipper 会原样导出）。</summary>
    private static bool HasMesh(ISpriteRenderData rd)
    {
        if (rd.Has_VertexData() && rd.VertexData.VertexCount > 0)
        {
            return true;
        }
        if (rd.Has_Vertices() && rd.Vertices.Count > 0)
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// 解码纹理，只保留 Sprite 矩形覆盖的那块像素。
    ///
    /// 内存约束：整张图集可能是 2048x2048（16MB RGBA32），而一个 Sprite 通常只占几百像素；
    /// 同一张图集还可能被十几个 Sprite 共用。这里按纹理做**单张缓存**（同一时刻只握一份整图），
    /// 解码后立刻裁出小图并释放整图，所以峰值内存 ≈ 1 张整图 + 1 个小切片。
    /// </summary>
    private static bool TryGetSubImage(
        ITexture2D texture,
        Rectf spriteRect,
        int texW,
        int texH,
        out byte[] subImage,
        out Rectf pixelRect)
    {
        subImage = [];
        pixelRect = default;

        if (!TryGetFullRgba32(texture, texW, texH, out byte[] full))
        {
            return false;
        }

        // 与 SpriteUtility.GenerateSpriteOutline 用同一套像素边界（floor/ceil + 夹取），
        // 这样切片坐标和后续 uv 计算严格一致。
        SpriteUtility.GetSpriteMeshRectPixelBounds(texW, texH, spriteRect,
            out int x0, out int y0, out int x1, out int y1);
        x0 = Math.Max(x0, 0);
        y0 = Math.Max(y0, 0);
        x1 = Math.Min(x1, texW);
        y1 = Math.Min(y1, texH);

        int w = x1 - x0;
        int h = y1 - y0;
        if (w <= 0 || h <= 0)
        {
            return false;
        }

        subImage = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
        {
            int srcStart = ((y0 + row) * texW + x0) * 4;
            Buffer.BlockCopy(full, srcStart, subImage, row * w * 4, w * 4);
        }
        pixelRect = new Rectf(x0, y0, w, h);
        return true;
    }

    /// <summary>
    /// 整张纹理的 RGBA32，<b>行优先、原点在左下</b>（与 Unity <c>GetPixels32</c> 一致）。
    ///
    /// 注意 <see cref="TextureConverter.TryConvertToBitmap"/> 内部已调用 <c>FlipY()</c>，
    /// 它给出的位图原点在**左上**（PNG 方向）。这里统一再翻一次，让下游
    /// （<see cref="TryGetSubImage"/>、<see cref="AlphaCentroid"/>、<see cref="SpriteUtility"/>）
    /// 都能直接拿 Unity 的 <c>m_Rect.y</c>（底部原点）做索引。
    /// 之前漏了这一步，导致所有 y 方向的取像素全部偏到透明区域。
    /// </summary>
    internal static bool TryGetFullRgba32(ITexture2D texture, int texW, int texH, out byte[] rgba)
    {
        if (PixelCache.TryGet(texture, out rgba))
        {
            return true;
        }

        if (!TextureConverter.TryConvertToBitmap(texture, out DirectBitmap bitmap) || bitmap.IsEmpty)
        {
            rgba = [];
            return false;
        }

        if (bitmap is DirectBitmap<ColorRGBA<byte>, byte> rgbaBitmap)
        {
            rgba = rgbaBitmap.Bits.ToArray();
        }
        else
        {
            // 其它像素格式（R8 / RGB565 / BC…）：DirectBitmap 的颜色类型是运行期决定的，
            // 借同一个泛型转换走一次 RGBA32。
            Type bitmapType = bitmap.GetType();
            if (!bitmapType.IsGenericType)
            {
                rgba = [];
                return false;
            }
            Type[] arguments = bitmapType.GetGenericArguments();
            MethodInfo method = typeof(SpriteOutlinePatch).GetMethod(
                nameof(ConvertToRgba32), BindingFlags.NonPublic | BindingFlags.Static)!;
            object?[] parameters = [bitmap, null];
            if (method.MakeGenericMethod(arguments[0], arguments[1]).Invoke(null, parameters) is not true)
            {
                rgba = [];
                return false;
            }
            rgba = (byte[])parameters[1]!;
        }

        // 统一翻转成 Unity 的底部原点（见方法注释）
        FlipVertically(rgba, texW, texH);

        PixelCache.Set(texture, rgba);
        return true;
    }

    /// <summary>按行原地垂直翻转（RGBA32，每行 texW 个像素）。</summary>
    private static void FlipVertically(byte[] rgba, int texW, int texH)
    {
        int rowBytes = texW * 4;
        byte[] temp = new byte[rowBytes];
        for (int top = 0, bottom = texH - 1; top < bottom; top++, bottom--)
        {
            int topOffset = top * rowBytes;
            int bottomOffset = bottom * rowBytes;
            Buffer.BlockCopy(rgba, topOffset, temp, 0, rowBytes);
            Buffer.BlockCopy(rgba, bottomOffset, rgba, topOffset, rowBytes);
            Buffer.BlockCopy(temp, 0, rgba, bottomOffset, rowBytes);
        }
    }

    private static bool ConvertToRgba32<TColor, TChannel>(DirectBitmap bitmap, out byte[] rgba)
        where TColor : unmanaged, IColor<TChannel>
        where TChannel : unmanaged
    {
        RgbConverter.Convert<TColor, TChannel, ColorRGBA<byte>, byte>(
            bitmap.Bits, bitmap.Width, bitmap.Height * bitmap.Depth, out rgba);
        return true;
    }

    /// <summary>
    /// 纹理像素缓存：只保留**最近一张**纹理的 RGBA32。
    ///
    /// 不缓存全部纹理——那是几百 MB 级的内存；只留一张就能吃掉「同一图集上连续处理多个 Sprite」
    /// 这个最主要的重复解码场景，而峰值内存固定为 1 张整图。遍历结束后由 RebuildAll 清空。
    /// </summary>
    private static class PixelCache
    {
        private static ITexture2D? cachedTexture;
        private static byte[]? cachedPixels;

        public static bool TryGet(ITexture2D texture, out byte[] rgba)
        {
            if (ReferenceEquals(cachedTexture, texture) && cachedPixels is not null)
            {
                rgba = cachedPixels;
                return true;
            }
            rgba = [];
            return false;
        }

        public static void Set(ITexture2D texture, byte[] rgba)
        {
            cachedTexture = texture;
            cachedPixels = rgba;
        }

        public static void Clear()
        {
            cachedTexture = null;
            cachedPixels = null;
        }
    }

    private static void WriteMesh(
        ISpriteRenderData rd,
        ITexture2D texture,
        int texW,
        int texH,
        List<SpriteVertex> vertices,
        List<ushort> indices,
        Rectf textureRect,
        Rectf spriteRect)
    {
        // 顶点：Unity 的 SpriteVertex 只有 pos + uv，直接写回同名字段
        if (rd.Has_Vertices())
        {
            AccessListBase<ISpriteVertex> dst = rd.Vertices;
            dst.Clear();
            dst.Capacity = vertices.Count;
            foreach (SpriteVertex v in vertices)
            {
                ISpriteVertex created = dst.AddNew();
                created.Pos.X = v.Pos.X;
                created.Pos.Y = v.Pos.Y;
                created.Pos.Z = v.Pos.Z;
                created.Uv.X = v.Uv.X;
                created.Uv.Y = v.Uv.Y;
            }
        }

        if (rd.Has_Indices())
        {
            AssetList<ushort> dst = rd.Indices;
            dst.Clear();
            dst.Capacity = indices.Count;
            foreach (ushort index in indices)
            {
                dst.Add(index);
            }
        }

        // 现代版本（>= 2017 左右）用 VertexData + IndexBuffer + SubMeshes 存网格
        if (rd.Has_VertexData())
        {
            WriteVertexData(rd.VertexData, vertices);
        }
        if (rd.Has_IndexBuffer())
        {
            byte[] buffer = new byte[indices.Count * sizeof(ushort)];
            for (int i = 0; i < indices.Count; i++)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(i * 2, 2), indices[i]);
            }
            rd.IndexBuffer = buffer;
        }
        if (rd.Has_SubMeshes())
        {
            AccessListBase<ISubMesh> subMeshes = rd.SubMeshes;
            subMeshes.Clear();
            ISubMesh subMesh = subMeshes.AddNew();
            subMesh.FirstByte = 0;
            subMesh.IndexCount = (uint)indices.Count;
            subMesh.Topology = (int)MeshTopology.Triangles;
            subMesh.BaseVertex = 0;
            subMesh.FirstVertex = 0;
            subMesh.VertexCount = (uint)vertices.Count;
            subMesh.LocalAABB.Center.CopyValues(Vector3.Zero);
            subMesh.LocalAABB.Extent.CopyValues(Vector3.Zero);
        }

        // 网格覆盖的纹理矩形
        rd.TextureRect.X = textureRect.X;
        rd.TextureRect.Y = textureRect.Y;
        rd.TextureRect.Width = textureRect.Width;
        rd.TextureRect.Height = textureRect.Height;

        // textureRectOffset = textureRect 起点 - sprite rect 起点（见 Sprite::Initialize）
        rd.TextureRectOffset.X = textureRect.X - spriteRect.X;
        rd.TextureRectOffset.Y = textureRect.Y - spriteRect.Y;
    }

    /// <summary>
    /// 按 Unity 的顶点流布局写 VertexData：stream 0 = position(float3)，
    /// stream 1 = uv0(float2)，每个流按 16 字节对齐。
    /// </summary>
    private static void WriteVertexData(IVertexData vertexData, List<SpriteVertex> vertices)
    {
        const int kShaderChannelVertex = 0;
        const int kShaderChannelTexCoord0 = 4;
        const int kFloatFormat = 0;

        int vertexCount = vertices.Count;
        int posStride = 12;  // float3
        int uvStride = 8;    // float2

        int posStreamSize = Align(posStride * vertexCount);
        int uvStreamSize = Align(uvStride * vertexCount);
        byte[] data = new byte[posStreamSize + uvStreamSize];

        for (int i = 0; i < vertexCount; i++)
        {
            SpriteVertex v = vertices[i];
            int posOffset = i * posStride;
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(posOffset, 4), v.Pos.X);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(posOffset + 4, 4), v.Pos.Y);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(posOffset + 8, 4), v.Pos.Z);

            int uvOffset = posStreamSize + i * uvStride;
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(uvOffset, 4), v.Uv.X);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(uvOffset + 4, 4), v.Uv.Y);
        }

        vertexData.VertexCount = (uint)vertexCount;
        vertexData.Data = data;

        // Channels 只有在版本支持时才存在；不支持时 Unity 用 Streams 描述布局，
        // 而 AssetRipper 的读取路径两种都能吃（见 VertexDataBlob.Create）。
        if (vertexData.Has_Channels())
        {
            AssetList<ChannelInfo> channels = vertexData.Channels;
            if (channels.Count < 14)
            {
                channels.Clear();
                for (int i = 0; i < 14; i++)
                {
                    channels.AddNew();
                }
            }
            for (int i = 0; i < channels.Count; i++)
            {
                ChannelInfo channel = channels[i];
                channel.Stream = 0;
                channel.Offset = 0;
                channel.Format = kFloatFormat;
                channel.Dimension = 0;
                if (i == kShaderChannelVertex)
                {
                    channel.Dimension = 3;
                }
                else if (i == kShaderChannelTexCoord0)
                {
                    channel.Stream = 1;
                    channel.Dimension = 2;
                }
            }
        }

        if (vertexData.Has_Streams())
        {
            AssetList<StreamInfo_4> streams = vertexData.Streams;
            streams.Clear();
            StreamInfo_4 posStream = streams.AddNew();
            posStream.ChannelMask = 1u << kShaderChannelVertex;
            posStream.Offset = 0;
            posStream.Stride_Byte = (byte)posStride;
            posStream.DividerOp = 0;
            posStream.Frequency = 0;

            StreamInfo_4 uvStream = streams.AddNew();
            uvStream.ChannelMask = 1u << kShaderChannelTexCoord0;
            uvStream.Offset = (uint)posStreamSize;
            uvStream.Stride_Byte = (byte)uvStride;
            uvStream.DividerOp = 0;
            uvStream.Frequency = 0;
        }
    }

    private static int Align(int size) => (size + 15) & ~15;
}
