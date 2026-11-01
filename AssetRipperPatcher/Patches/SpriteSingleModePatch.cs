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
using AssetRipper.SourceGenerated.Subclasses.Vector2f;
using AssetRipperPatcher.Patching;

namespace AssetRipperPatcher.Patches;

/// <summary>
/// 修复 Sprite importer 的元数据，让 Unity 的 Sprite Editor 轮廓线落在正确位置。
///
/// <para><b>问题。</b>
/// 实测（Unity 6 项目）有一批从大图裁出来的子 Sprite，
/// 每个 Sprite 都有**独立的 <see cref="ITexture2D"/>**，但那张纹理其实是**整张大图**：
/// <code>
/// logo_text_fire : rect = (0, 0, 406.92, 152.97)     纹理 = 1024 x 301
/// 某子 Sprite: rect = (0, 5.08, 207.97, 182.85) 纹理 = 986 x 190
/// </code>
/// AssetRipper 的 <see cref="TextureExportCollection.AddSprites"/> 判定
/// <c>sprite.Rect == sprite.RD.TextureRect &amp;&amp; sprite.Name == texture.Name</c>
/// 成立，于是选了 Single 模式；而 Unity 的 Single 模式意味着「整张纹理就是一个 Sprite」，
/// rect 被完全忽略。
///
/// 但 outline / pivot 是按 **rect 中心** 归一化的，Unity 却按 **整图中心** 解释，
/// 于是轮廓线被整体平移——表现就是「Sprite Editor 里轮廓线飘到图中间」。
///
/// <para><b>修法（保持 Single + 不裁图）。</b>
/// 不换 Multiple、不裁剪 PNG，而是把坐标换算到整图坐标系：
/// <code>
/// 整图坐标 = 相对 rect 中心的坐标 + (rect 中心 - 纹理中心)
/// pivot整图 = (pivotRect * rectSize + rectPos) / textureSize
/// </code>
/// 实测换算后某子 Sprite 的 outline 从 <c>x[-104,104]</c> 变成 <c>x[-493,-285]</c>，
/// 正好对应「冰」字在 986 宽整图中的左侧位置。
/// </para>
/// </summary>
internal static class SpriteSingleModePatch
{
    /// <summary>
    /// 挂在 <see cref="TextureExportCollection.AddSprites"/> 之后。
    /// </summary>
    [PatchMethod(typeof(TextureExportCollection), "AddSprites", PatchType = PatchType.Postfix)]
    public static void FixSpriteSheetCoordinates(
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

        if (importer.SpriteModeE == SpriteImportMode.Single && textureSpriteInformation.Count == 1)
        {
            KeyValuePair<ISprite, ISpriteAtlas?> kvp = textureSpriteInformation.First();
            FixSingleMode(importer, kvp.Key);
        }
        else
        {
            // Multiple 模式：Unity 按 sprites[i].rect 裁切，坐标本来就相对各自 rect，
            // 无需换算；这里只保证 sprites[] 已填好，并清掉不可用的 outline。
            EnsureMultipleEntries(importer, textureSpriteInformation);
        }
    }

    /// <summary>
    /// Single 模式：把 outline / physicsShape / pivot 从「相对 rect」换算到「相对整张纹理」。
    /// </summary>
    private static void FixSingleMode(ITextureImporter importer, ISprite sprite)
    {
        ITexture2D? texture = sprite.TryGetTexture();
        if (texture is null)
        {
            return;
        }
        int textureWidth = texture.Width_C28;
        int textureHeight = texture.Height_C28;
        if (textureWidth <= 0 || textureHeight <= 0)
        {
            return;
        }

        float rectWidth = sprite.Rect.Width;
        float rectHeight = sprite.Rect.Height;
        if (rectWidth <= 0.0F || rectHeight <= 0.0F)
        {
            return;
        }

        // rect 中心与纹理中心的差值（像素）
        float shiftX = (sprite.Rect.X + rectWidth * 0.5F) - textureWidth * 0.5F;
        float shiftY = (sprite.Rect.Y + rectHeight * 0.5F) - textureHeight * 0.5F;

        // 1) 顶层 outline / physicsShape：相对 rect 中心 -> 相对整图中心
        //
        // 两者都要平移。Single 模式下 Unity 把整张纹理当作一个 Sprite，
        // 所以 outline 和 physicsShape 都以「整图中心」为原点。
        // 实测某子 Sprite：physicsShape 不平移会停在 x[-51.5,96.5]，
        // 而冰字实际在 x[-440.5,-292.5]（= PNG 的 x[0,205] 减整图中心 493）。
        ShiftContours(importer.SpriteSheet.Outline, shiftX, shiftY);
        ShiftContours(importer.SpriteSheet.PhysicsShape, shiftX, shiftY);

        // 2) pivot：从「相对 rect 归一化」换算到「相对整图归一化」
        if (importer.Has_SpritePivot() && sprite.Has_Pivot())
        {
            float pivotX = (sprite.Pivot.X * rectWidth + sprite.Rect.X) / textureWidth;
            float pivotY = (sprite.Pivot.Y * rectHeight + sprite.Rect.Y) / textureHeight;
            importer.SpritePivot.SetValues(pivotX, pivotY);
        }

        // 3) Single 模式下 Unity 的 Sprite Editor 读 sprites[0]，需要一条与之同步的条目
        EnsureSingleEntry(importer, sprite);
    }

    /// <summary>把每条轮廓的每个点平移 (dx, dy) 像素。</summary>
    private static void ShiftContours(AssetList<AssetList<Vector2f>> contours, float dx, float dy)
    {
        if (dx == 0.0F && dy == 0.0F)
        {
            return;
        }
        foreach (AssetList<Vector2f> contour in contours)
        {
            foreach (Vector2f point in contour)
            {
                point.X += dx;
                point.Y += dy;
            }
        }
    }

    /// <summary>
    /// Single 模式必须往 <c>sprites[]</c> 里塞一条，否则 Sprite Editor 读不到 outline。
    /// rect 保持原始子矩形（不裁图），outline 用已换算到整图坐标的那一份。
    /// </summary>
    private static void EnsureSingleEntry(ITextureImporter importer, ISprite sprite)
    {
        AccessListBase<ISpriteMetaData> sprites = importer.SpriteSheet.Sprites;
        if (sprites.Count > 0)
        {
            // 已经存在：同步 outline，避免与顶层字段不一致
            ISpriteMetaData existing = sprites[0];
            CopyContours(importer.SpriteSheet.Outline, existing.Outline);
            CopyContours(importer.SpriteSheet.PhysicsShape, existing.PhysicsShape);
            return;
        }

        ISpriteMetaData spriteMetaData = sprites.AddNew();
        CopyContours(importer.SpriteSheet.Outline, spriteMetaData.Outline);
        CopyContours(importer.SpriteSheet.PhysicsShape, spriteMetaData.PhysicsShape);

        spriteMetaData.Name = sprite.Name;
        spriteMetaData.Rect.CopyValues(sprite.Rect.CastToStruct());
        spriteMetaData.Alignment = (int)SpriteAlignment.Custom;
        if (importer.Has_SpritePivot())
        {
            spriteMetaData.Pivot.CopyValues(importer.SpritePivot.CastToStruct());
        }
        if (spriteMetaData.Has_Border() && sprite.Has_Border())
        {
            spriteMetaData.Border.CopyValues(sprite.Border.CastToStruct());
        }
    }

    /// <summary>Multiple 模式：确保每个 sprite 都有一条记录（坐标相对各自 rect，不换算）。</summary>
    private static void EnsureMultipleEntries(
        ITextureImporter importer,
        IReadOnlyDictionary<ISprite, ISpriteAtlas?> textureSpriteInformation)
    {
        AccessListBase<ISpriteMetaData> sprites = importer.SpriteSheet.Sprites;
        if (sprites.Count == textureSpriteInformation.Count)
        {
            return;
        }

        sprites.Clear();
        sprites.Capacity = textureSpriteInformation.Count;
        foreach ((ISprite sprite, ISpriteAtlas? atlas) in textureSpriteInformation)
        {
            sprites.AddNew().FillSpriteMetaData(sprite, atlas);
        }
    }

    private static void CopyContours(
        AssetList<AssetList<Vector2f>> source,
        AssetList<AssetList<Vector2f>> destination)
    {
        destination.Clear();
        destination.Capacity = source.Count;
        foreach (AssetList<Vector2f> contour in source)
        {
            AssetList<Vector2f> copy = destination.AddNew();
            copy.Capacity = contour.Count;
            foreach (Vector2f point in contour)
            {
                copy.AddNew().CopyValues(point);
            }
        }
    }
}
