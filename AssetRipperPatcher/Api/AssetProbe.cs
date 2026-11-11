using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.Modules.Audio;
using AssetRipper.Export.Modules.Models;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Export.Modules.Textures;
using AssetRipper.Export.UnityProjects.Scripts;
using AssetRipper.Export.UnityProjects.Shaders;
using AssetRipper.GUI.Web;
using AssetRipper.GUI.Web.Paths;
using AssetRipper.Import.AssetCreation;
using AssetRipper.Import.Logging;
using AssetRipper.Processing.Textures;
using AssetRipper.Import.Structure.Assembly;
using AssetRipper.SourceGenerated.Classes.ClassID_115;
using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Classes.ClassID_128;
using AssetRipper.SourceGenerated.Classes.ClassID_156;
using AssetRipper.SourceGenerated.Classes.ClassID_189;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Classes.ClassID_43;
using AssetRipper.SourceGenerated.Classes.ClassID_48;
using AssetRipper.SourceGenerated.Classes.ClassID_49;
using AssetRipper.SourceGenerated.Classes.ClassID_83;
using AssetRipper.SourceGenerated.Classes.ClassID_329;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.Yaml;
using AssetRipper.Export.PrimaryContent;
using AssetRipperPatcher.Shader.AssetRipper.Export.Modules.Shaders.Exporters;
using System.Globalization;
using SharpGLTF.Scenes;

namespace AssetRipperPatcher.Api;

/// <summary>
/// 只读的资源投影层：把 AssetRipper 的内部对象翻译成 UI 需要的 DTO。
///
/// <para><b>为什么重新实现而不是复用 GUI.Web 的 AssetAPI。</b>
/// AssetAPI / CollectionAPI / BundleAPI 全是 <c>internal</c>，且只输出服务端渲染的 HTML。
/// 这里用到的底层类型（<see cref="GameFileLoader"/>、<see cref="PathExtensions"/>、
/// <see cref="TextureConverter"/>、<see cref="GlbMeshBuilder"/> 等）都是 public，
/// 所以可以在不改动 AssetRipper 子模块的前提下拿到同样的数据。</para>
///
/// <para><b>取舍。</b>这里只覆盖 UI 真正需要的：列表、详情、四种预览、文本。复杂的分页
/// 逻辑由调用方（AssetService）负责，本类保持无状态。</para>
/// </summary>
internal static class AssetProbe
{
	public static GameBundle? Bundle => GameFileLoader.IsLoaded ? GameFileLoader.GameBundle : null;

	public static AssetRowDto ToRow(IUnityObjectBase asset, bool includePreviewInfo = true)
	{
		(int? width, int? height) = GetDimensions(asset);
		return new AssetRowDto(
			PathId: asset.PathID,
			ClassId: asset.ClassID,
			ClassName: asset.ClassName,
			Name: asset.GetBestName(),
			Collection: asset.Collection.Name,
			CollectionPath: asset.Collection.GetPath().ToJson(),
			OriginalPath: asset.OriginalPath ?? asset.OverridePath,
			Extension: asset.GetBestExtension(),
			SizeBytes: GetSize(asset),
			Width: width,
			Height: height,
			Preview: includePreviewInfo && HasImage(asset) ? "image" : null,
			Audio: includePreviewInfo && HasAudio(asset) ? "audio" : null,
			Model: includePreviewInfo && HasModel(asset) ? "model" : null,
			Font: includePreviewInfo && HasFont(asset) ? "font" : null,
			Video: includePreviewInfo && HasVideo(asset) ? "video" : null);
	}

	public static IEnumerable<IUnityObjectBase> EnumerateAssets()
	{
		GameBundle? bundle = Bundle;
		return bundle is null ? [] : bundle.FetchAssets();
	}

	public static IUnityObjectBase? FindAsset(string collectionPathJson, long pathId)
	{
		GameBundle? bundle = Bundle;
		if (bundle is null)
		{
			return null;
		}
		CollectionPath collectionPath;
		try
		{
			collectionPath = CollectionPath.FromJson(collectionPathJson);
		}
		catch (Exception)
		{
			return null;
		}
		return bundle.TryGetAsset(new AssetPath(collectionPath, pathId));
	}

	#region 尺寸 / 大小

	private static (int?, int?) GetDimensions(IUnityObjectBase asset)
	{
		try
		{
			return asset switch
			{
				IImageTexture texture => (texture.Width_C189, texture.Height_C189),
				_ => (null, null),
			};
		}
		catch (Exception)
		{
			return (null, null);
		}
	}

	private static int? GetSize(IUnityObjectBase asset)
	{
		try
		{
			return asset switch
			{
				IImageTexture texture when texture.CheckAssetIntegrity() => GetDecodedSize(texture),
				IAudioClip clip when clip.CheckAssetIntegrity() => clip.GetAudioData().Length,
				ITextAsset textAsset => System.Text.Encoding.UTF8.GetByteCount(textAsset.Script_C49),
				_ => null,
			};
		}
		catch (Exception)
		{
			return null;
		}

		static int GetDecodedSize(IImageTexture texture)
		{
			// 图片的原始压缩数据大小对用户没意义，直接给解码后的像素字节数。
			try
			{
				return checked(texture.Width_C189 * texture.Height_C189 * 4);
			}
			catch (OverflowException)
			{
				return 0;
			}
		}
	}

	#endregion

	#region 能力探测

	public static bool HasImage(IUnityObjectBase asset) => asset switch
	{
		IImageTexture texture => texture.CheckAssetIntegrity(),
		SpriteInformationObject spriteInformationObject => spriteInformationObject.Texture.CheckAssetIntegrity(),
		ISprite sprite => SpriteConverter.Supported(sprite),
		_ => false,
	};

	public static bool HasAudio(IUnityObjectBase asset)
	{
		return asset is IAudioClip clip && clip.CheckAssetIntegrity();
	}

	public static bool HasModel(IUnityObjectBase asset)
	{
		return asset is IMesh;
	}

	public static bool HasVideo(IUnityObjectBase asset)
	{
		try
		{
			return asset is IVideoClip clip && clip.CheckIntegrity();
		}
		catch (Exception)
		{
			return false;
		}
	}

	public static bool HasFont(IUnityObjectBase asset)
	{
		return asset is IFont font && TryGetFontData(font, out _, out _, out _);
	}

	public static bool HasText(IUnityObjectBase asset)
	{
		return asset is IShader or IMonoScript or ITextAsset { Script_C49.IsEmpty: false };
	}

	#endregion

	#region 预览数据

	public static byte[]? GetImageBytes(IUnityObjectBase asset, ImageExportFormat format)
	{
		DirectBitmap bitmap = GetBitmap(asset);
		if (bitmap.IsEmpty)
		{
			return null;
		}
		using MemoryStream stream = new();
		bitmap.Save(stream, format);
		return stream.ToArray();
	}

	private static DirectBitmap GetBitmap(IUnityObjectBase asset)
	{
		try
		{
			return asset switch
			{
				IImageTexture texture => TextureConverter.TryConvertToBitmap(texture, out DirectBitmap b) ? b : DirectBitmap.Empty,
				SpriteInformationObject group => TextureConverter.TryConvertToBitmap(group.Texture, out DirectBitmap b) ? b : DirectBitmap.Empty,
				ISprite sprite => SpriteConverter.TryConvertToBitmap(sprite, out DirectBitmap b) ? b : DirectBitmap.Empty,
				_ => DirectBitmap.Empty,
			};
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.General, $"Image decode failed for '{asset.GetBestName()}': {ex.Message}");
			return DirectBitmap.Empty;
		}
	}

	public static (byte[] Data, string Mime)? GetAudioBytes(IUnityObjectBase asset, string? preferredExtension)
	{
		if (asset is not IAudioClip clip)
		{
			return null;
		}
		try
		{
			if (!AudioClipDecoder.TryDecode(clip, out byte[]? data, out string? extension, out _))
			{
				return null;
			}
			if (extension is "ogg" && preferredExtension is "wav")
			{
				byte[] wav = AudioConverter.OggToWav(data);
				if (wav.Length > 0)
				{
					return (wav, "audio/wav");
				}
			}
			return (data, $"audio/{extension}");
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.General, $"Audio decode failed for '{asset.GetBestName()}': {ex.Message}");
			return null;
		}
	}

	public static byte[]? GetModelBytes(IUnityObjectBase asset)
	{
		if (asset is not IMesh mesh)
		{
			return null;
		}
		try
		{
			SceneBuilder sceneBuilder = GlbMeshBuilder.Build(mesh);
			using MemoryStream stream = new();
			return GlbWriter.TryWrite(sceneBuilder, stream, out _) ? stream.ToArray() : null;
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.General, $"Model decode failed for '{asset.GetBestName()}': {ex.Message}");
			return null;
		}
	}

	public static (byte[] Data, string Mime)? GetFontBytes(IUnityObjectBase asset)
	{
		if (asset is IFont font && TryGetFontData(font, out byte[] data, out _, out string? mimeType))
		{
			return (data, mimeType);
		}
		return null;
	}

	public static (byte[] Data, string Mime)? GetVideoBytes(IUnityObjectBase asset)
	{
		if (asset is not IVideoClip videoClip)
		{
			return null;
		}
		try
		{
					if (videoClip.TryGetExtensionFromPath(out string? extension) && videoClip.TryGetContent(out byte[]? content) && content is not null)
			{
				return (content, $"video/{extension}");
			}
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.General, $"Video read failed for '{asset.GetBestName()}': {ex.Message}");
		}
		return null;
	}

	public static (byte[] Data, string Mime)? GetRawBytes(IUnityObjectBase asset)
	{
		byte[]? data = (asset as RawDataObject)?.RawData;
		return data is { Length: > 0 } ? (data, "application/octet-stream") : null;
	}

	#endregion

	#region 文本

	public static string? GetText(IUnityObjectBase asset)
	{
		try
		{
			return asset switch
			{
					IShader shader => GetShaderText(shader),
				IMonoScript monoScript => DumpScript(monoScript),
				ITextAsset textAsset => textAsset.Script_C49,
				_ => null,
			};
		}
		catch (Exception ex)
		{
			return $"读取文本失败：{ex}";
		}

			// 与导出路径保持一致：优先 ShaderRipper 完整反编译，失败再退回 GLSL 反汇编。
			// 不能用 DummyShaderTextExporter —— 那只是占位模板，看不到真实程序。
			static string GetShaderText(IShader shader)
			{
				string? decompiled = ShaderRipperExporter.TryDecompile(shader);
				if (!string.IsNullOrWhiteSpace(decompiled))
				{
					return decompiled;
				}

				// 最后兜底：至少给出 Shader 里存的原始脚本头部。
				if (shader.Has_Script() && !shader.Script.IsEmpty)
				{
					return shader.Script;
				}

				using StringWriter writer = new(CultureInfo.InvariantCulture);
				DummyShaderTextExporter.ExportShader(shader, writer);
				return writer.ToString();
			}

		static string DumpScript(IMonoScript script)
		{
			try
			{
				if (!script.IsScriptPresents(GameFileLoader.AssemblyManager))
				{
					return EmptyScript.GetContent(script);
				}
				// 与上游一致：真正的反编译（ILSpy）尚未接入，先给空脚本骨架。
				_ = script.GetTypeDefinition(GameFileLoader.AssemblyManager);
				return EmptyScript.GetContent(script);
			}
			catch (Exception ex)
			{
				return $"反编译失败：\n\n{ex}";
			}
		}
	}

	public static string? GetTextFileName(IUnityObjectBase asset)
	{
		return asset switch
		{
			IShader => $"{asset.GetBestName()}.shader",
			IMonoScript monoScript => $"{monoScript.ClassName_R}.cs",
			ITextAsset textAsset => $"{asset.GetBestName()}.{textAsset.GetBestExtension() ?? "txt"}",
			_ => null,
		};
	}

	public static string? GetJson(IUnityObjectBase asset)
	{
		try
		{
			using StringWriter writer = new(CultureInfo.InvariantCulture) { NewLine = "\n" };
			asset.WalkStandard(new DefaultJsonWalker(writer));
			return writer.ToString();
		}
		catch (Exception ex)
		{
			return ex.ToString();
		}
	}

	public static string? GetYaml(IUnityObjectBase asset)
	{
		try
		{
			using StringWriter writer = new(CultureInfo.InvariantCulture) { NewLine = "\n" };
			YamlWriter yamlWriter = new();
			yamlWriter.WriteHead(writer);
			YamlDocument document = new YamlWalker().ExportYamlDocument(asset, ExportIdHandler.GetMainExportID(asset));
			yamlWriter.WriteDocument(document);
			yamlWriter.WriteTail(writer);
			return writer.ToString();
		}
		catch (Exception ex)
		{
			return ex.ToString();
		}
	}

	#endregion

	private static bool TryGetFontData(IFont font, out byte[] data, out string? extension, out string? mimeType)
	{
		data = font.FontData;
		if (data is { Length: >= 4 })
		{
			(extension, mimeType) = (data[0], data[1], data[2], data[3]) switch
			{
				(0x4F, 0x54, 0x54, 0x4F) => ("otf", "font/otf"),
				(0x00, 0x01, 0x00, 0x00) => ("ttf", "font/ttf"),
				(0x74, 0x74, 0x63, 0x66) => ("ttc", "font/collection"),
				_ => ("dat", "application/octet-stream"),
			};
			return true;
		}
		extension = null;
		mimeType = null;
		return false;
	}
}
