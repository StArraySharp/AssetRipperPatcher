using System.Numerics;

namespace AssetRipperPatcher.Sprites;

/// <summary>
/// <c>Runtime/Graphics/SpriteUtility.cpp</c> 的逐函数移植：像素矩形夹取、轮廓/网格生成、AABB 四角展开。
/// </summary>
internal static class SpriteUtility
{
    /// <summary>原版 <c>kSpriteEdgeBias</c>：轮廓外扩 1 像素。</summary>
    private const float kSpriteEdgeBias = 1.0F;

    /// <summary>
    /// 原版 <c>GetSpriteMeshRectPixelBounds</c>：把不精确的纹理矩形取整到像素边界，
    /// 夹回纹理范围内，并返回"取整后越界"的诊断标志（原版在这里 Assert）。
    /// </summary>
    public static bool GetSpriteMeshRectPixelBounds(
        int textureGlWidth,
        int textureGlHeight,
        Rectf rectInaccurate,
        out int rectX,
        out int rectY,
        out int rectRight,
        out int rectBottom)
    {
        rectX = (int)SpriteMath.Floorf(rectInaccurate.X);
        rectY = (int)SpriteMath.Floorf(rectInaccurate.Y);
        rectRight = (int)SpriteMath.Ceilf(rectInaccurate.GetRight());
        rectBottom = (int)SpriteMath.Ceilf(rectInaccurate.GetBottom());

        // Sanity check to see if out of texture rect or if error is more than 1 pixel for whole-texture sprites.
        bool errorX = rectX < 0;
        bool errorY = rectY < 0;
        bool errorR = rectRight > textureGlWidth + 1;
        bool errorB = rectBottom > textureGlHeight + 1;
        bool hasError = errorX || errorY || errorR || errorB;

        // In rare cases when a texture is downscaled to match the max texture size limit it might introduce an odd scaling factor, cause a 1 pixel error and grow beyond the texture size.
        rectRight = Math.Min(rectRight, textureGlWidth);
        rectBottom = Math.Min(rectBottom, textureGlHeight);

        return hasError;
    }

    /// <summary>
    /// 原版 <c>GenerateSpriteOutline</c>：从纹理的 alpha 通道生成轮廓（outLine）与网格（outVertices/outIndices），
    /// 以及网格实际覆盖的纹理矩形（meshRect）。参数含义与原版一一对应。
    /// </summary>
    /// <param name="texturePixels">纹理 RGBA32 数据，行优先、原点在左下（与 <c>GetPixels32</c> 一致）。</param>
    /// <param name="textureGlWidth">纹理 GL 宽度（Power-of-two 修正后的上传尺寸）。</param>
    /// <param name="textureGlHeight">纹理 GL 高度。</param>
    /// <param name="outLine">非 null 时写入轮廓（每个元素是一条闭合折线，单位：物体空间）。</param>
    /// <param name="outVertices">
    /// 非 null 时写入网格顶点；此时 <paramref name="outIndices"/> 必须同时非 null（原版 Assert）。
    /// </param>
    /// <param name="meshRect">非 null 时写入网格矩形（纹理空间，已加回 rectInaccurate 的偏移）。</param>
    /// <returns>像素矩形越界的诊断标志；原版只 Assert，不改变行为。</returns>
    public static bool GenerateSpriteOutline(
        ReadOnlySpan<byte> texturePixels,
        int textureGlWidth,
        int textureGlHeight,
        float pixelsToUnits,
        Rectf rectInaccurate,
        Vector2 rectOffset,
        float detail,
        byte alphaTolerance,
        bool holeDetection,
        uint extrude,
        int simplifyMode,
        List<List<Vector2>>? outLine,
        List<SpriteVertex>? outVertices,
        List<ushort>? outIndices,
        out Rectf meshRect)
    {
        meshRect = rectInaccurate;

        if (textureGlWidth <= 0 || textureGlHeight <= 0)
        {
            return false;
        }

        int texW = textureGlWidth;
        int texH = textureGlHeight;

        // Figure out what pixels to use.
        // If a platform texture size limit is set, rectInaccurate will not define actual pixels.
        bool hasError = GetSpriteMeshRectPixelBounds(
            textureGlWidth, textureGlHeight, rectInaccurate,
            out int rectX, out int rectY, out int rectRight, out int rectBottom);
        int rectWidth = rectRight - rectX;
        int rectHeight = rectBottom - rectY;

        if (rectWidth <= 0 || rectHeight <= 0)
        {
            return hasError;
        }

        // Extract rectangle
        byte[] imageData = new byte[rectWidth * rectHeight * 4];
        {
            for (int row = 0; row < rectHeight; ++row)
            {
                int srcRow = rectY + row;
                if (srcRow < 0 || srcRow >= texH)
                {
                    continue; // 原版会读越界；这里补零，等价于"全透明"
                }
                int srcStart = (srcRow * texW + rectX) * 4;
                int count = rectWidth * 4;
                if (srcStart < 0 || srcStart + count > texturePixels.Length)
                {
                    continue;
                }
                texturePixels.Slice(srcStart, count)
                    .CopyTo(imageData.AsSpan(row * rectWidth * 4, count));
            }
        }

        return GenerateSpriteOutlineFromPixels(
            imageData, rectWidth, rectHeight, rectX, rectY,
            texW, texH, pixelsToUnits, rectInaccurate, rectOffset,
            detail, alphaTolerance, holeDetection, extrude, simplifyMode,
            outLine, outVertices, outIndices, out meshRect, hasError);
    }

    /// <summary>
    /// <see cref="GenerateSpriteOutline"/> 的核心：像素已经裁到 <paramref name="imageWidth"/>×<paramref name="imageHeight"/>，
    /// 其左上角对应整图的 (<paramref name="pixelOriginX"/>, <paramref name="pixelOriginY"/>)。
    ///
    /// 拆出来是为了让调用方（补丁）可以只解码纹理的一小块，避免为每个 Sprite 都复制整张图集。
    /// 数学与原版逐字一致：<paramref name="rectInaccurate"/> 仍是整图坐标，
    /// 所以 <c>rectAdjust</c> 与 uv 换算的结果和「传整图进来」完全相同。
    /// </summary>
    public static bool GenerateSpriteOutlineFromPixels(
        ReadOnlySpan<byte> imageData,
        int imageWidth,
        int imageHeight,
        int pixelOriginX,
        int pixelOriginY,
        int textureGlWidth,
        int textureGlHeight,
        float pixelsToUnits,
        Rectf rectInaccurate,
        Vector2 rectOffset,
        float detail,
        byte alphaTolerance,
        bool holeDetection,
        uint extrude,
        int simplifyMode,
        List<List<Vector2>>? outLine,
        List<SpriteVertex>? outVertices,
        List<ushort>? outIndices,
        out Rectf meshRect,
        bool hasPixelRectError = false)
    {
        meshRect = rectInaccurate;

        int texW = textureGlWidth;
        int texH = textureGlHeight;
        int rectWidth = imageWidth;
        int rectHeight = imageHeight;
        int rectX = pixelOriginX;
        int rectY = pixelOriginY;
        float rectAdjustX = rectInaccurate.X - rectX;
        float rectAdjustY = rectInaccurate.Y - rectY;

        if (rectWidth <= 0 || rectHeight <= 0)
        {
            return hasPixelRectError;
        }

        // Detect shape
        float hullTolerance = detail >= 0.0F ? 1.0F - SpriteMath.Clamp01(detail) : detail;
        extrude = Math.Clamp(extrude, 0U, 32U);
        SpriteMeshGenerator smg = new();
        smg.MakeShape(
            imageData, rectWidth, rectHeight, hullTolerance, alphaTolerance, holeDetection,
            extrude, kSpriteEdgeBias, simplifyMode);

        // Adjustments for offset and pixel-to-unity scale.
        // 原版写的是 1.0 / x（double 除法）再存进 float，这里保持同样的中间精度
        float scale = (float)(1.0 / pixelsToUnits);
        float scaleX = (float)(1.0 / texW);
        float scaleY = (float)(1.0 / texH);
        Vector2 oxy = new(
            rectWidth * 0.5F + rectOffset.X - rectAdjustX,
            rectHeight * 0.5F + rectOffset.Y - rectAdjustY);

        // Fill outline
        if (outLine is not null)
        {
            IReadOnlyList<SpriteMeshGenerator.Path> paths = smg.GetPaths();
            outLine.Clear();
            outLine.Capacity = paths.Count;
            foreach (SpriteMeshGenerator.Path path in paths)
            {
                List<SpriteMeshGenerator.PathVertex> poly = path.Vertices;
                List<Vector2> finalPath = new(poly.Count);
                foreach (SpriteMeshGenerator.PathVertex it in poly)
                {
                    Vector2 polyPoint = it.P;
                    Vector2 finalPoint = new((polyPoint.X - oxy.X) * scale, (polyPoint.Y - oxy.Y) * scale);
                    finalPath.Add(finalPoint);
                }
                outLine.Add(finalPath);
            }
        }

        // Fill mesh
        if (outVertices is not null)
        {
            if (outIndices is null)
            {
                throw new ArgumentNullException(nameof(outIndices), "outVertices requires outIndices (原版此处 Assert)");
            }

            // Decompose outline
            List<Vector2> vertices = new();
            List<int> indices = new();
            smg.Decompose(vertices, indices);

            if (indices.Count > 0)
            {
                outVertices.Clear();
                outIndices.Clear();
                // Assign indices
                indices.Reverse();
                outIndices.Capacity = indices.Count;
                foreach (int index in indices)
                {
                    outIndices.Add((ushort)index);
                }

                // Assign vertices
                outVertices.Capacity = vertices.Count;
                for (int i = 0; i < vertices.Count; ++i)
                {
                    SpriteVertex v = new()
                    {
                        Pos = new Vector3(
                            (vertices[i].X - oxy.X) * scale,
                            (vertices[i].Y - oxy.Y) * scale,
                            0.0F),
                        Uv = new Vector2(
                            (vertices[i].X + rectInaccurate.X) * scaleX,
                            (vertices[i].Y + rectInaccurate.Y) * scaleY),
                    };
                    outVertices.Add(v);
                }
            }
        }

        // Set mesh rect
        if (smg.FindBounds(out Rectf bounds))
        {
            meshRect = bounds;
        }
        else
        {
            meshRect = new Rectf(rectX, rectY, rectWidth, rectHeight);
        }

        return hasPixelRectError;
    }

    /// <summary>
    /// 原版 <c>GetAABBVerticesForSprite</c>：把 AABB 压平到 z = 0，返回正面四角。
    /// 顺序与原版一致（-x-y, +x-y, -x+y, +x+y）。
    /// </summary>
    public static void GetAABBVerticesForSprite(Vector3 center, Vector3 extent, Span<Vector3> outVertices)
    {
        outVertices[0] = center + new Vector3(-extent.X, -extent.Y, 0.0F);
        outVertices[1] = center + new Vector3(+extent.X, -extent.Y, 0.0F);
        outVertices[2] = center + new Vector3(-extent.X, +extent.Y, 0.0F);
        outVertices[3] = center + new Vector3(+extent.X, +extent.Y, 0.0F);
    }

    /// <summary>
    /// 原版 <c>SpriteRenderData::GenerateQuadMesh</c>：Tight 网格不可用时（纹理太小、无像素数据）
    /// 的退路——直接铺一个从 rect 拉伸到整个纹理的四边形。
    /// </summary>
    public static void GenerateQuadMesh(
        int textureGlWidth,
        int textureGlHeight,
        Rectf rect,
        Vector2 rectOffset,
        float pixelsToUnits,
        List<SpriteVertex> outVertices,
        List<ushort> outIndices)
    {
        float scaler = 1.0F / pixelsToUnits;

        float halfW = rect.Width * 0.5F * scaler;
        float halfH = rect.Height * 0.5F * scaler;
        Vector2 offset = rectOffset * scaler;
        Vector4 uv = new(
            rect.X / textureGlWidth,
            rect.Y / textureGlHeight,
            rect.GetRight() / textureGlWidth,
            rect.GetBottom() / textureGlHeight);

        outVertices.Clear();
        outVertices.Add(new SpriteVertex(new Vector3(-halfW - offset.X, +halfH - offset.Y, 0.0F), new Vector2(uv.X, uv.W)));
        outVertices.Add(new SpriteVertex(new Vector3(+halfW - offset.X, +halfH - offset.Y, 0.0F), new Vector2(uv.Z, uv.W)));
        outVertices.Add(new SpriteVertex(new Vector3(-halfW - offset.X, -halfH - offset.Y, 0.0F), new Vector2(uv.X, uv.Y)));
        outVertices.Add(new SpriteVertex(new Vector3(+halfW - offset.X, -halfH - offset.Y, 0.0F), new Vector2(uv.Z, uv.Y)));

        outIndices.Clear();
        outIndices.AddRange([0, 1, 2, 2, 1, 3]);
    }
}
