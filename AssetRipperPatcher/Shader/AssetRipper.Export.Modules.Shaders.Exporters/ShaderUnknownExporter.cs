using AssetRipper.Export.Modules.Shaders.IO;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader;

namespace AssetRipper.Export.Modules.Shaders.Exporters;

public class ShaderUnknownExporter : ShaderTextExporter
{
	private readonly GPUPlatform m_graphicApi;

	public override string Name => "ShaderUnknownExporter";

	public ShaderUnknownExporter(GPUPlatform graphicApi)
	{
		m_graphicApi = graphicApi;
	}

	public override void Export(ShaderWriter writer, ref ShaderSubProgram subProgram)
	{
		writer.Write($"/*ShaderUnknownExporter : Can't export program data {m_graphicApi} as a text*/");
	}
}
