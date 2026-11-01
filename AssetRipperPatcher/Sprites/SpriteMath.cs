using System.Numerics;

namespace AssetRipperPatcher.Sprites;

/// <summary>
/// 移植自 Unity 的 <c>Runtime/Math/FloatConversion.h</c>、<c>Runtime/Math/Polynomials.h</c>
/// 与 <c>Runtime/Math/Vector2.h</c> 中 SpriteMeshGenerator/SpriteUtility 实际用到的部分。
/// 数值行为必须与原版逐位一致，所以这里不用 <see cref="float.Epsilon"/> 之类的 .NET 概念。
/// </summary>
internal static class SpriteMath
{
    /// <summary>C++ <c>Vector2f::epsilon</c>（Runtime/Math/Vector2.cpp 里是 0.00001F）。</summary>
    public const float VectorEpsilon = 0.00001F;

    /// <summary>
    /// C++ <c>(std::numeric_limits&lt;float&gt;::min)()</c> = FLT_MIN，即最小正规数。
    /// 注意它<b>不是</b> <see cref="float.MinValue"/>，也不是 <see cref="float.Epsilon"/>。
    /// SpriteMeshGenerator::path::bbox 用它是原版的写法，这里照抄。
    /// </summary>
    public const float FloatMin = 1.17549435E-38F;

    public static float Floorf(float f) => MathF.Floor(f);

    public static float Ceilf(float f) => MathF.Ceiling(f);

    /// <summary>C++ <c>Roundf</c> = <c>Floorf(f + 0.5F)</c>。</summary>
    public static float Roundf(float f) => MathF.Floor(f + 0.5F);

    public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;

    public static float Clamp01(float value) => Clamp(value, 0.0F, 1.0F);

    public static float Magnitude(Vector2 v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);

    /// <summary>C++ <c>NormalizeSafe</c>：长度超过 epsilon 才归一化，否则回退到 defaultV。</summary>
    public static Vector2 NormalizeSafe(Vector2 v, Vector2 defaultV = default)
    {
        float mag = Magnitude(v);
        return mag > VectorEpsilon ? v / mag : defaultV;
    }

    /// <summary>
    /// C++ <c>CompareFloatRobust</c>：按 ULP 比较两个 float 是否"几乎相等"。
    /// 符号位不同时退化为精确比较（+0 与 -0 也走这条分支）。
    /// </summary>
    public static bool CompareFloatRobust(float f0, float f1, int maxUlps = 10)
    {
        if ((BitConverter.SingleToInt32Bits(f0) & unchecked((int)0x80000000))
            != (BitConverter.SingleToInt32Bits(f1) & unchecked((int)0x80000000)))
        {
            return f0 == f1;
        }

        int aInt = BitConverter.SingleToInt32Bits(f0);
        if (aInt < 0)
        {
            aInt = unchecked((int)0x80000000) - aInt;
        }

        int bInt = BitConverter.SingleToInt32Bits(f1);
        if (bInt < 0)
        {
            bInt = unchecked((int)0x80000000) - bInt;
        }

        return Math.Abs(aInt - bInt) <= maxUlps;
    }

    /// <summary>
    /// C++ <c>QuadraticPolynomialRootsGeneric</c>：返回实根数量（0/1/2）。
    /// 用 <c>ref</c> 而不是 <c>out</c> 是刻意的——原版只写它算出来的根，
    /// 另一个分量保持调用方传入的初值（SpriteMeshGenerator 依赖这一点）。
    /// </summary>
    public static int QuadraticPolynomialRootsGeneric(float a, float b, float c, ref float r0, ref float r1)
    {
        const float eps = 0.00001F;
        if (MathF.Abs(a) < eps)
        {
            if (MathF.Abs(b) > eps)
            {
                r0 = -c / b;
                return 1;
            }
            return 0;
        }

        float disc = b * b - 4 * a * c;
        if (disc < 0.0F)
        {
            return 0;
        }

        const float halfRcpA = 0.5F;
        float sqrtDisc = MathF.Sqrt(disc);
        float inv2A = halfRcpA / a;
        r0 = (sqrtDisc - b) * inv2A;
        r1 = (-sqrtDisc - b) * inv2A;
        return 2;
    }
}
