using AssetRipper.Assets;
using AssetRipper.Export.Modules.Shaders.Exporters;
using AssetRipper.Export.Modules.Shaders.Exporters.DirectX;
using AssetRipper.Export.Modules.Shaders.IO;
using AssetRipper.Import.Logging;
using AssetRipper.IO.Files;
using AssetRipper.SourceGenerated.Classes.ClassID_48;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader;

namespace AssetRipper.Export.UnityProjects.Shaders;

/// <summary>
/// Exports a shader as ShaderLab with every program disassembled.
/// </summary>
/// <remarks>
/// Restored from the AssetRipper history (commit 09221814d removed it) and adapted
/// to the current enum names.  Paid builds moved this behind a licence, so this is
/// what makes a disassembly export possible in a free build at all.
///
/// The patcher keeps this copy -- rather than changing the AssetRipper submodule --
/// so the restored exporter compiles into the patcher assembly under the same
/// namespace and simply wins over the one AssetRipper used to ship.
///
/// A <c>ShaderDisassemblyExporter</c> compiled here is what
/// <c>ShaderExportPatch</c> uses as its fallback once the ShaderRipper decompiler
/// declines a shader.
/// </remarks>
public sealed class ShaderDisassemblyExporter : ShaderExporterBase
{
	public override bool Export(IExportContainer container, IUnityObjectBase asset, string path, FileSystem fileSystem)
	{
		try
		{
			using Stream fileStream = fileSystem.File.Create(path);
			ExportBinary((IShader)asset, fileStream, ShaderExporterInstantiator);
		}
		catch (Exception ex)
		{
			Logger.Error(ex);
			using Stream fileStream = fileSystem.File.Create(path);
			DummyShaderTextExporter.ExportShader((IShader)asset, new InvariantStreamWriter(fileStream));
		}
		return true;
	}

	private static ShaderTextExporter ShaderExporterInstantiator(GPUPlatform graphicApi)
	{
		switch (graphicApi)
		{
			case GPUPlatform.D3D11_9x:
			case GPUPlatform.D3D11:
			case GPUPlatform.D3D9:
				return new ShaderDXExporter(graphicApi);

			case GPUPlatform.OpenGL:
			case GPUPlatform.Gles20:
			case GPUPlatform.Gles3x:
			case GPUPlatform.GlCore:
				return new ShaderGLESExporter();

			case GPUPlatform.Metal:
				return new ShaderMetalExporter();
			case GPUPlatform.Vulkan:
			case GPUPlatform.Unknown:
				return new ShaderTextExporter();

			default:
				return new ShaderUnknownExporter(graphicApi);
		}
	}

	private static void ExportBinary(IShader shader, Stream stream, Func<GPUPlatform, ShaderTextExporter> exporterInstantiator)
	{
		if (shader.Has_ParsedForm())
		{
			using ShaderWriter writer = new ShaderWriter(stream, shader, exporterInstantiator);
			shader.ParsedForm.Export(writer);
		}
		else if (shader.Has_CompressedBlob())
		{
			using ShaderWriter writer = new ShaderWriter(stream, shader, exporterInstantiator);
			string header = shader.Script.String;
			if (writer.Blobs.Length == 0)
			{
				writer.Write(header);
			}
			else
			{
				writer.Blobs[0].Export(writer, header);
			}
		}
		else
		{
			using BinaryWriter writer = new BinaryWriter(stream);
			writer.Write(shader.Script.Data);
		}
	}
}
