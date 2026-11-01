using System.Numerics;

namespace AssetRipperPatcher.Sprites;

/// <summary>
/// <c>Runtime/Math/Rect.h</c> 的 <c>Rectf</c> 子集：SpriteUtility 只用到 x/y/width/height 与四条边。
/// 与 Unity 一致，<c>y</c> 向上增长，所以 Top 是 y、Bottom 是 y + height。
/// </summary>
internal struct Rectf
{
    public float X;
    public float Y;
    public float Width;
    public float Height;

    public Rectf(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public readonly float GetRight() => X + Width;

    public float SetRight(float value)
    {
        Width = value - X;
        return value;
    }

    public readonly float GetBottom() => Y + Height;

    public float SetBottom(float value)
    {
        Height = value - Y;
        return value;
    }
}

/// <summary>
/// <c>Runtime/Graphics/SpriteFrame.h</c> 的 <c>SpriteVertex</c>：
/// 位置已换算到物体空间（单位 = 1 / pixelsToUnits），uv 是纹理空间 [0..1]。
/// </summary>
internal struct SpriteVertex
{
    public Vector3 Pos;
    public Vector2 Uv;

    public SpriteVertex(Vector3 pos, Vector2 uv)
    {
        Pos = pos;
        Uv = uv;
    }
}

/// <summary><c>Runtime/Graphics/SpriteUtility.h</c> 的 <c>PathSimplifyMode</c>。</summary>
public enum PathSimplifyMode
{
    kPathReduce = 0,
    kPathEmbed,
}
