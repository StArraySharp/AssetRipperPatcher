using System.Reflection;
using AssetRipper.Assets;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.Modules.Textures;
using AssetRipper.Export.PrimaryContent.Textures;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Export.UnityProjects.Textures;
using AssetRipper.IO.Files;
using AssetRipper.SourceGenerated.Classes.ClassID_189;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Enums;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.TextureDecoder.Rgb;
using AssetRipper.TextureDecoder.Rgb.Formats;
using AssetRipperPatcher.Patching;

namespace AssetRipperPatcher.Patches;

/// <summary>
/// 纯 alpha 纹理（<c>TextureFormat.Alpha8</c> / <c>GraphicsFormat.A8_UNorm</c>）导出修复。
///
/// 问题：这类纹理在磁盘上只有 8 位 alpha，没有颜色。AssetRipper 用
/// <c>ColorA&lt;byte&gt;</c> 解出来，再转成 <c>ColorRGBA</c> 时 R/G/B 填 0，
/// 导出的 PNG 就成了「黑色 + alpha」。
///
/// Unity 的行为是：纯 alpha 纹理在 UI/精灵里按「白色 × alpha」显示，
/// 导入时勾了 Alpha Is Transparency 的贴图更是只取 alpha 当遮罩。
/// 所以导出成白底透明图才对——否则白色图标导出后会变成黑色不可见。
/// </summary>
internal static class AlphaTexturePatch
{
    /// <summary>
    /// 拦截 <see cref="TextureAssetExporter.Export(IExportContainer, IUnityObjectBase, string, FileSystem)"/>：
    /// 纯 alpha 纹理走自己的白底转换，其余纹理交回原实现。
    /// </summary>
    [PatchMethod(typeof(TextureAssetExporter), nameof(TextureAssetExporter.Export), OverrideIndex = 1, PatchType = PatchType.Prefix)]
    public static bool ExportAlphaTexture(
        TextureAssetExporter __instance,
        IExportContainer container,
        IUnityObjectBase asset,
        string path,
        FileSystem fileSystem,
        ref bool __result)
    {
        if (asset is not ITexture2D texture2D)
        {
            return true;
        }

        // 宽或高为 0 的纹理没有任何像素可导出。
        // 典型来源：TMP 字体图集的「占位」Texture2D —— 它们 CompleteImageSize=0、
        // ImageData 为空、StreamData 未设置，但 CheckAssetIntegrity() 仍返回 true，
        // 于是原逻辑会尝试解码并报 "Unable to convert ... to bitmap"。
        // 这里直接判为「无内容」，跳过导出（不算失败）。
        if (texture2D.Width_C28 <= 0 || texture2D.Height_C28 <= 0)
        {
            __result = false;
            return false;
        }

        if (!texture2D.CheckAssetIntegrity())
        {
            return true;
        }
        if (!IsPureAlphaTexture(texture2D))
        {
            return true;
        }
        if (!TextureConverter.TryConvertToBitmap(texture2D, out DirectBitmap bitmap) || bitmap.IsEmpty)
        {
            return true;
        }
        if (!TryGetAlphaChannel(bitmap, out byte[] alpha, out int width, out int height))
        {
            return true;
        }

        // 白底：RGB 全 255，alpha 保留原值
        byte[] rgba = new byte[width * height * 4];
        for (int i = 0, j = 0; i < alpha.Length; i++, j += 4)
        {
            rgba[j + 0] = 255;
            rgba[j + 1] = 255;
            rgba[j + 2] = 255;
            rgba[j + 3] = alpha[i];
        }

        DirectBitmap<ColorRGBA<byte>, byte> whiteBitmap = new(width, height, 1, rgba);
        using (Stream stream = fileSystem.File.Create(path))
        {
            whiteBitmap.Save(stream, texture2D.GetTextureExportFormat(
                __instance.PreferOriginalTextureExtension, __instance.ImageExportFormat));
        }
        __result = true;
        return false;
    }

    /// <summary>只有 alpha 通道、没有颜色信息的纹理格式。</summary>
    private static bool IsPureAlphaTexture(IImageTexture texture)
    {
        if (texture is not ITexture2D texture2D)
        {
            return false;
        }

        // Unity 2020+ 起 Format_C28 是 GraphicsFormat，更早是 TextureFormat。
        // 两种枚举里「纯 alpha」的名字不同，各自判断。
        int rawFormat = texture2D.Format_C28;
        if (Enum.IsDefined(typeof(TextureFormat), rawFormat)
            && (TextureFormat)rawFormat == TextureFormat.Alpha8)
        {
            return true;
        }
        if (Enum.IsDefined(typeof(GraphicsFormat), rawFormat)
            && (GraphicsFormat)rawFormat == GraphicsFormat.A8_UNorm)
        {
            return true;
        }
        return false;
    }

    /// <summary>把解码结果取出 alpha 通道（支持 ColorA 与 ColorRGBA 两种位图）。</summary>
    private static bool TryGetAlphaChannel(DirectBitmap bitmap, out byte[] alpha, out int width, out int height)
    {
        width = bitmap.Width;
        height = bitmap.Height;
        alpha = [];

        if (bitmap is DirectBitmap<ColorRGBA<byte>, byte> rgbaBitmap)
        {
            Span<byte> bits = rgbaBitmap.Bits;
            alpha = new byte[width * height];
            for (int i = 0, j = 3; i < alpha.Length; i++, j += 4)
            {
                if (j >= bits.Length)
                {
                    return false;
                }
                alpha[i] = bits[j];
            }
            return true;
        }

        // ColorA<byte> 位图：每像素 1 字节，就是 alpha
        Type bitmapType = bitmap.GetType();
        if (!bitmapType.IsGenericType)
        {
            return false;
        }
        Type[] arguments = bitmapType.GetGenericArguments();
        if (arguments.Length != 2)
        {
            return false;
        }
        MethodInfo method = typeof(AlphaTexturePatch).GetMethod(
            nameof(ExtractAlpha), BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] parameters = [bitmap, null];
        if (method.MakeGenericMethod(arguments[0], arguments[1]).Invoke(null, parameters) is not true)
        {
            return false;
        }
        alpha = (byte[])parameters[1]!;
        return true;
    }

    private static bool ExtractAlpha<TColor, TChannel>(DirectBitmap bitmap, out byte[] alpha)
        where TColor : unmanaged, IColor<TChannel>
        where TChannel : unmanaged
    {
        // 统一转成 RGBA 再取 alpha，避免依赖具体颜色类型的布局
        RgbConverter.Convert<TColor, TChannel, ColorRGBA<byte>, byte>(
            bitmap.Bits, bitmap.Width, bitmap.Height * bitmap.Depth, out byte[] rgba);
        alpha = new byte[bitmap.Width * bitmap.Height];
        for (int i = 0, j = 3; i < alpha.Length; i++, j += 4)
        {
            alpha[i] = rgba[j];
        }
        return true;
    }
}
