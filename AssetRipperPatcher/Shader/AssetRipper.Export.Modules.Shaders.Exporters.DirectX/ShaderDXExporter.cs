using AssetRipper.Export.Modules.Shaders.IO;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader;

namespace AssetRipper.Export.Modules.Shaders.Exporters.DirectX;

public class ShaderDXExporter : ShaderTextExporter
{
	protected readonly GPUPlatform m_graphicApi;

	public override string Name => "ShaderDXExporter";

	public ShaderDXExporter(GPUPlatform graphicApi)
	{
		m_graphicApi = graphicApi;
	}

	public override void Export(ShaderWriter writer, ref ShaderSubProgram subProgram)
	{
		byte[] exportData = subProgram.ProgramData;
		string disassemblyText;
		if (DXShaderTextExtractor.TryDecompileText(exportData, writer.Version, m_graphicApi, out string decompiledText))
		{
			ShaderTextExporter.ExportListing(writer, "//ShaderDXExporter_Decompiler\n" + decompiledText);
		}
		else if (DXShaderTextExtractor.TryGetShaderText(exportData, writer.Version, m_graphicApi, out disassemblyText))
		{
			ShaderTextExporter.ExportListing(writer, "//ShaderDXExporter_Disassembler\n" + disassemblyText);
		}
	}
}
