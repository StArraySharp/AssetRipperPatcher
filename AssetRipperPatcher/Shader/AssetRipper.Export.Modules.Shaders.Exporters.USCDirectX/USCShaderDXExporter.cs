using AssetRipper.Export.Modules.Shaders.IO;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader;

namespace AssetRipper.Export.Modules.Shaders.Exporters.USCDirectX;

public class USCShaderDXExporter : ShaderTextExporter
{
	public UShaderProgram? uShaderProgram;

	protected readonly GPUPlatform m_graphicApi;

	public override string Name => "ShaderUSCDXExporter";

	public USCShaderDXExporter(GPUPlatform graphicApi)
	{
		uShaderProgram = null;
		m_graphicApi = graphicApi;
	}

	public override void Export(ShaderWriter writer, ref ShaderSubProgram subProgram)
	{
		byte[] exportData = subProgram.ProgramData;
		string disassemblyText;
		if (USCDXShaderTextExtractor.TryDecompileText(exportData, writer.Version, m_graphicApi, subProgram, out string decompiledText, out uShaderProgram))
		{
			ShaderTextExporter.ExportListing(writer, "// Exported with USC Decompiler\n" + decompiledText);
		}
		else if (USCDXShaderTextExtractor.TryGetShaderText(exportData, writer.Version, m_graphicApi, out disassemblyText))
		{
			ShaderTextExporter.ExportListing(writer, "// ShaderDXExporter_Disassembler\n" + disassemblyText);
		}
	}
}
