using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL;

public interface IUSILOptimizer
{
	bool Run(UShaderProgram shader, ShaderSubProgram shaderData);
}
