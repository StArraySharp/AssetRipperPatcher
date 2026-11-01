using System.Numerics;
using AssetRipper.Assets;
using AssetRipper.Assets.Generics;
using AssetRipper.Export.UnityProjects.Textures;
using AssetRipper.SourceGenerated.Classes.ClassID_1006;
using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Classes.ClassID_687078895;
using AssetRipper.SourceGenerated.Enums;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Subclasses.SpriteMetaData;
using AssetRipper.SourceGenerated.Subclasses.SpriteSheetMetaData;
using AssetRipper.SourceGenerated.Subclasses.Vector2f;
using AssetRipperPatcher.Patching;
using AssetRipperPatcher.Sprites;

namespace AssetRipperPatcher.Patches;

/// <summary>
/// 修复 Sprite <c>outline</c>。分三级，逐级降级：
///
/// <para><b>1. 现有 outline 可用就保留。</b></para>
///
/// <para><b>2. 顶点序列。</b>
/// AssetRipper 用 <c>MeshOutlineGenerator</c> 从三角形相邻关系反推轮廓，
/// 对密集网格会断链：实测多切片图集的 64 个切片里 61 个只产出 3~4 个点（2 像素宽的碎片）。
/// 这类 Sprite 的 <c>m_RD</c> 顶点本身按边界顺序排列，直接拿来当轮廓即可。</para>
///
/// <para><b>3. alpha 轮廓兜底。</b>
/// 顶点序列也不可信时（点数太少、或包围盒覆盖率明显不足），
/// 用纹理 alpha 通道重新生成轮廓，<b>alpha 容差拉到最大</b>。</para>
///
/// <para><b>坐标系。</b>
/// 顶点是「相对 pivot」的像素坐标，加上 <c>Offset</c> 即回到「相对 rect 中心」，
/// 与 <c>outline</c> 要求的坐标系一致（实测某 133×49 切片：
/// 顶点 <c>x[-69,64]</c> + Offset.x(2.5) = <c>x[-66.5,66.5]</c> = rect 半宽）。
/// </para>
/// </summary>
internal static class SpriteOutlineRepairPatch
{
    /// <summary>
    /// 轮廓生成用的 alpha 容差。
    ///
    /// 254 是原版上限（<c>kSpriteMaxAlphaTolerance</c>），但实测过高的容差会把半透明边缘
    /// 全纳入掩码，导致轮廓追踪在连通域上退化（某切片有 3791 个 alpha 像素
    /// 却生成 0 条轮廓）。取 128 兼顾「尽量宽松」与「能稳定追踪」。
    /// </summary>
    private const byte OutlineAlphaTolerance = 128;

    /// <summary>少于这么多点的 outline 视为不可用。</summary>
    private const int MinimumViableOutlinePoints = 8;

    /// <summary>判断「形状」时至少需要这么多点，否则连闭合多边形都不成立。</summary>
    private const int MinimumViablePointsForShape = 3;

    /// <summary>
    /// 轮廓「明显不正确」的判据：包围盒面积相对 rect 面积太小
    /// （碎片、退化线段都会命中）。
    /// </summary>
    private const float MinimumCoverageRatio = 0.05F;

    [PatchMethod(typeof(TextureExportCollection), "AddSprites", PatchType = PatchType.Postfix)]
    public static void RepairOutlines(
        ITextureImporter importer,
        IReadOnlyDictionary<ISprite, ISpriteAtlas?>? textureSpriteInformation)
    {
        if (importer is null || textureSpriteInformation is null || textureSpriteInformation.Count == 0)
        {
            return;
        }
        if (!importer.Has_SpriteSheet())
        {
            return;
        }

        bool isMultiple = importer.SpriteModeE == SpriteImportMode.Multiple;

        // 顶层字段（Single 模式）与 sprites[] 条目（Multiple 模式）都要处理
        RepairSheet(importer.SpriteSheet, textureSpriteInformation);

        AccessListBase<ISpriteMetaData> sprites = importer.SpriteSheet.Sprites;
        for (int i = 0; i < sprites.Count && i < textureSpriteInformation.Count; i++)
        {
            ISprite sprite = textureSpriteInformation.ElementAt(i).Key;
            RepairOne(sprites[i], sprite, isMultiple);
            RepairPhysicsShape(sprites[i], sprite);
        }
    }

    /// <summary>
    /// 修复 <c>physicsShape</c>。
    ///
    /// 原始 <c>m_PhysicsShape</c> 是「相对 pivot 的物体空间单位」（实测
    /// 某子 Sprite：<c>x[-0.69, 0.29]</c>，ppu=100 → 像素 <c>x[-69, 29]</c>）。
    /// 但 AssetRipper 的 <c>GeneratePhysicsShape</c> 在乘 ppu 之后**又加了一次
    /// <c>pivotShift = rect.Width*pivot.X - rect.Width*0.5</c>**，导致整体偏移。
    ///
    /// 正确做法：只乘 ppu。对于 pivot 被修正过的 Sprite（<see cref="SpriteOutlinePatch"/>），
    /// 原始数据是按错误 pivot 存的，需要用「修正前后 pivot 的差值」再补偿一次。
    /// </summary>
    private static void RepairPhysicsShape(ISpriteMetaData spriteMetaData, ISprite sprite)
    {
        if (!spriteMetaData.Has_PhysicsShape())
        {
            return;
        }
        RepairPhysicsShapeCore(spriteMetaData.PhysicsShape, sprite);
    }

    /// <summary>
    /// 把 <c>sprite.PhysicsShape</c>（相对 pivot 的 units）写成「相对 rect 中心的像素」。
    /// </summary>
    private static void RepairPhysicsShapeCore(AssetList<AssetList<Vector2f>> shapes, ISprite sprite)
    {
        if (!sprite.Has_PhysicsShape() || sprite.PhysicsShape.Count == 0)
        {
            return;
        }

        float pixelsToUnits = sprite.PixelsToUnits;
        if (pixelsToUnits <= 0.0F)
        {
            return;
        }

        float rectWidth = sprite.Rect.Width;
        float rectHeight = sprite.Rect.Height;
        if (rectWidth <= 0.0F || rectHeight <= 0.0F)
        {
            return;
        }

        shapes.Clear();
        shapes.Capacity = sprite.PhysicsShape.Count;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        List<List<Vector2>> built = new(sprite.PhysicsShape.Count);

        foreach (AssetList<Vector2f> source in sprite.PhysicsShape)
        {
            List<Vector2> contour = new(source.Count);
            foreach (Vector2f p in source)
            {
                float x = p.X * pixelsToUnits;
                float y = p.Y * pixelsToUnits;
                contour.Add(new Vector2(x, y));
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
            built.Add(contour);
        }

        // 若整体超出 rect 一半，说明是「旧 pivot」留下的偏移，按包围盒中心对回原点。
        // 阈值留 2 像素容差，避免误伤本来贴边的合法数据。
        float halfWidth = rectWidth * 0.5F;
        float halfHeight = rectHeight * 0.5F;
        if (minX < -halfWidth - 2.0F || maxX > halfWidth + 2.0F
            || minY < -halfHeight - 2.0F || maxY > halfHeight + 2.0F)
        {
            float shiftX = -(minX + maxX) * 0.5F;
            float shiftY = -(minY + maxY) * 0.5F;
            foreach (List<Vector2> contour in built)
            {
                for (int i = 0; i < contour.Count; i++)
                {
                    contour[i] = new Vector2(contour[i].X + shiftX, contour[i].Y + shiftY);
                }
            }
        }

        WriteContours(shapes, built);
    }

    private static void RepairSheet(
        ISpriteSheetMetaData sheet,
        IReadOnlyDictionary<ISprite, ISpriteAtlas?> textureSpriteInformation)
    {
        if (textureSpriteInformation.Count != 1)
        {
            return;
        }

        ISprite sprite = textureSpriteInformation.Keys.First();

        if (sheet.Has_Outline())
        {
            List<List<Vector2>>? contours = BuildOutline(sprite, sheet.Outline);
            if (contours is not null)
            {
                WriteContours(sheet.Outline, contours);
            }
        }

        // Single 模式的 physicsShape 在顶层，需要单独修一次
        if (sheet.Has_PhysicsShape())
        {
            RepairPhysicsShapeCore(sheet.PhysicsShape, sprite);
        }
    }

    private static void RepairOne(ISpriteMetaData spriteMetaData, ISprite sprite, bool isMultiple)
    {
        if (!spriteMetaData.Has_Outline())
        {
            return;
        }

        // Multiple 模式：直接按 alpha 重建。
        //
        // Unity 在 Multiple 模式下把每个切片（sprites[i]）的 outline 解释为
        // **相对该切片 rect 中心**的像素坐标，不是整图坐标。
        // （实测：某多切片图集的 rect 宽 133，正确 outline 应为 x[-66.5, 66.5]；
        //   若换算到整图会变成 x[-1448, -1393]，编辑器里就飘到图片左侧外面。）
        //
        // alpha 轮廓本身是「相对 pivot」，加回 Offset 即「相对 rect 中心」，正是这里要的。
        if (isMultiple)
        {
            List<List<Vector2>>? rebuilt = BuildOutlineFromAlpha(sprite, sprite.Rect.Width, sprite.Rect.Height);
            if (rebuilt is not null && rebuilt.Count > 0)
            {
                ShiftToRectCenter(sprite, rebuilt);
                WriteContours(spriteMetaData.Outline, rebuilt);
            }
            else if (Environment.GetEnvironmentVariable("SPRITE_DEBUG") == "1")
            {
                Console.WriteLine($"[OutlineAlpha] {sprite.Name.String}: alpha 生成失败/为空");
            }
            return;
        }

        List<List<Vector2>>? contours = BuildOutline(sprite, spriteMetaData.Outline);
        if (contours is null)
        {
            return;
        }
        WriteContours(spriteMetaData.Outline, contours);
    }

    /// <summary>
    /// 把 alpha 轮廓换算成「相对切片 rect 中心」的坐标。
    ///
    /// <c>SpriteUtility.GenerateSpriteOutline</c> 输出的是**相对 pivot** 的坐标
    /// （内部按 <c>rectWidth/2 + rectOffset</c> 做中心化），加回 <c>Offset</c>
    /// 就得到「相对 rect 中心」——这正是 Unity Multiple 模式对 <c>outline</c> 的要求。
    ///
    /// 实测 <c>某 133×49 切片</c>：rect=(23,583,133,49)，Offset.x=2.5。
    /// 加回 Offset 后是 <c>x[-66.5, 66.5]</c>，正好等于 rect 宽度 133 的一半。
    /// </summary>
    private static void ShiftToRectCenter(ISprite sprite, List<List<Vector2>> contours)
    {
        if (!sprite.Has_Pivot())
        {
            return;
        }
        float offsetX = sprite.Offset.X;
        float offsetY = sprite.Offset.Y;
        if (offsetX == 0.0F && offsetY == 0.0F)
        {
            return;
        }

        foreach (List<Vector2> contour in contours)
        {
            for (int i = 0; i < contour.Count; i++)
            {
                contour[i] = new Vector2(contour[i].X + offsetX, contour[i].Y + offsetY);
            }
        }
    }

    /// <summary>
    /// 生成轮廓：现有可用则保留，否则顶点序列，再否则 alpha。
    /// 返回 null 表示保持原样。
    /// </summary>
    private static List<List<Vector2>>? BuildOutline(
        ISprite sprite,
        AssetList<AssetList<Vector2f>> existing)
    {
        float rectWidth = sprite.Rect.Width;
        float rectHeight = sprite.Rect.Height;

        // 1) 现有 outline 已经合理就用它
        if (IsOutlineUsable(existing, rectWidth, rectHeight))
        {
            return null;
        }

        // 2) 顶点序列
        List<Vector2>? fromVertices = BuildOutlineFromVertices(sprite);
        if (fromVertices is not null && IsContourUsable(fromVertices, rectWidth, rectHeight))
        {
            return [fromVertices];
        }

        // 3) alpha 兜底
        List<List<Vector2>>? fromAlpha = BuildOutlineFromAlpha(sprite, rectWidth, rectHeight);
        if (fromAlpha is not null && fromAlpha.Count > 0)
        {
            return fromAlpha;
        }

        // 4) 顶点序列虽不完美，也比原来的碎片强
        return fromVertices is null ? null : [fromVertices];
    }

    /// <summary>现有一组轮廓是否可用：点数够、且包围盒覆盖了 rect 的一定比例。</summary>
    private static bool IsOutlineUsable(
        AssetList<AssetList<Vector2f>> contours,
        float rectWidth,
        float rectHeight)
    {
        List<Vector2> points = new();
        foreach (AssetList<Vector2f> contour in contours)
        {
            foreach (Vector2f point in contour)
            {
                points.Add(new Vector2(point.X, point.Y));
            }
        }
        return IsContourUsable(points, rectWidth, rectHeight);
    }

    /// <summary>
    /// 轮廓是否「明显不正确」：点数太少，或包围盒面积相对 rect 面积过小。
    /// 碎片/退化线段会同时命中这两条。
    /// </summary>
    private static bool IsContourUsable(List<Vector2> points, float rectWidth, float rectHeight)
    {
        if (points.Count < MinimumViablePointsForShape)
        {
            return false;
        }
        if (rectWidth <= 0.0F || rectHeight <= 0.0F)
        {
            return points.Count >= MinimumViableOutlinePoints;
        }

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (Vector2 p in points)
        {
            minX = Math.Min(minX, p.X);
            maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y);
            maxY = Math.Max(maxY, p.Y);
        }

        // 轮廓相对 rect 中心，rect 面积按 width*height 计
        float coverage = (maxX - minX) * (maxY - minY) / (rectWidth * rectHeight);
        return coverage >= MinimumCoverageRatio;
    }

    /// <summary>多段轮廓的整体可用性（合并所有点后走 <see cref="IsContourUsable"/>）。</summary>
    private static bool IsContoursUsable(
        List<List<Vector2>> contours,
        float rectWidth,
        float rectHeight)
    {
        if (contours.Count == 0)
        {
            return false;
        }
        List<Vector2> points = new();
        foreach (List<Vector2> contour in contours)
        {
            points.AddRange(contour);
        }
        return IsContourUsable(points, rectWidth, rectHeight);
    }

    /// <summary>
    /// 用 alpha 生成一组轮廓。抽出来是为了能用不同 <paramref name="extrude"/> 重试。
    /// </summary>
    private static List<List<Vector2>> GenerateAlphaContours(
        byte[] pixels,
        int textureWidth,
        int textureHeight,
        ISprite sprite,
        Rectf rect,
        Vector2 offset,
        uint extrude,
        out bool hasError)
    {
        List<List<Vector2>> contours = new();
        hasError = SpriteUtility.GenerateSpriteOutline(
            pixels, textureWidth, textureHeight, sprite.PixelsToUnits,
            rect, offset,
            detail: 1.0F,
            alphaTolerance: OutlineAlphaTolerance,
            holeDetection: true,
            extrude: extrude,
            simplifyMode: (int)PathSimplifyMode.kPathEmbed,
            outLine: contours,
            outVertices: null,
            outIndices: null,
            out _);
        return contours;
    }

    /// <summary>
    /// 用 <c>m_RD</c> 的顶点序列构造轮廓。顶点按边界顺序排列，且是「相对 pivot」的像素坐标，
    /// 加 <c>Offset</c> 后即「相对 rect 中心」。
    /// </summary>
    private static List<Vector2>? BuildOutlineFromVertices(ISprite sprite)
    {
        try
        {
            VertexDataBlob blob = VertexDataBlob.Create(
                sprite.RD.VertexData, sprite.Collection.Version, sprite.Collection.EndianType);
            if (!blob.ReadData(out Vector3[]? positions, out _, out _, out _, out _,
                out _, out _, out _, out _, out _, out _, out _, out _))
            {
                return null;
            }
            if (positions is null || positions.Length < 3)
            {
                return null;
            }

            float pixelsToUnits = sprite.PixelsToUnits;
            float scale = pixelsToUnits > 0.0F ? pixelsToUnits : 1.0F;
            float offsetX = sprite.Has_Pivot() ? sprite.Offset.X : 0.0F;
            float offsetY = sprite.Has_Pivot() ? sprite.Offset.Y : 0.0F;

            List<Vector2> result = new(positions.Length);
            foreach (Vector3 p in positions)
            {
                result.Add(new Vector2(p.X * scale + offsetX, p.Y * scale + offsetY));
            }
            return result;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 用纹理 alpha 生成轮廓，alpha 容差取最大（254）。
    /// 走的是移植自 Unity 的 <see cref="SpriteUtility"/>，
    /// 结果已是「相对 rect 中心」的像素坐标。
    /// </summary>
    private static List<List<Vector2>>? BuildOutlineFromAlpha(
        ISprite sprite,
        float rectWidth,
        float rectHeight)
    {
        try
        {
            ITexture2D? texture = sprite.TryGetTexture();
            if (texture is null)
            {
                return null;
            }
            int textureWidth = texture.Width_C28;
            int textureHeight = texture.Height_C28;
            if (textureWidth <= 0 || textureHeight <= 0)
            {
                return null;
            }
            if (!SpriteOutlinePatch.TryGetFullRgba32(texture, textureWidth, textureHeight, out byte[] pixels))
            {
                return null;
            }

            Rectf rect = new(sprite.Rect.X, sprite.Rect.Y, rectWidth, rectHeight);
            Vector2 offset = sprite.Has_Pivot()
                ? new Vector2(sprite.Offset.X, sprite.Offset.Y)
                : Vector2.Zero;

            // 先按 extrude=0 试（不膨胀，轮廓紧贴 alpha 边界）；
            // 若结果明显退化（点数太少 / 覆盖率不足），再用 extrude=1 重试并择优。
            // 实测：某密集网格图集的 28 个切片在 extrude=0 下只产出 6 个点，
            // 而 extrude=1 能正常闭合；反过来某多切片图集在 extrude=1 下会整体偏移。
            List<List<Vector2>> contours = GenerateAlphaContours(
                pixels, textureWidth, textureHeight, sprite, rect, offset, extrude: 0, out bool hasError);

            if (!IsContoursUsable(contours, rectWidth, rectHeight))
            {
                List<List<Vector2>> retry = GenerateAlphaContours(
                    pixels, textureWidth, textureHeight, sprite, rect, offset, extrude: 1, out _);
                if (IsContoursUsable(retry, rectWidth, rectHeight))
                {
                    contours = retry;
                }
            }

            if (contours.Count == 0)
            {
                Debug($"empty: {sprite.Name.String} rect=({rect.X:F1},{rect.Y:F1},{rect.Width:F1},{rect.Height:F1}) " +
                    $"tex={textureWidth}x{textureHeight} hasError={hasError}");
            }
            return contours;
        }
        catch (Exception ex)
        {
            Debug($"{sprite.Name.String}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static void Debug(string message)
    {
        if (Environment.GetEnvironmentVariable("SPRITE_DEBUG") == "1")
        {
            Console.WriteLine($"[OutlineAlpha] {message}");
        }
    }

    private static void WriteContours(
        AssetList<AssetList<Vector2f>> destination,
        List<List<Vector2>> contours)
    {
        destination.Clear();
        destination.Capacity = contours.Count;
        foreach (List<Vector2> contour in contours)
        {
            AssetList<Vector2f> target = destination.AddNew();
            target.Capacity = contour.Count;
            foreach (Vector2 point in contour)
            {
                target.AddNew().SetValues(point.X, point.Y);
            }
        }
    }
}
