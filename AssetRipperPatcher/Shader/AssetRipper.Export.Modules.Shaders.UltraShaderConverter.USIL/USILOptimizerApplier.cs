using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL.Fixers;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL.Metadders;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL.Optimizers;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL;

public static class USILOptimizerApplier
{
	private static readonly IUSILOptimizer[] OPTIMIZER_TYPES = new IUSILOptimizer[9]
	{
		new USILCBufferMetadder(),
		new USILSamplerMetadder(),
		new USILInputOutputMetadder(),
		new USILSamplerTypeFixer(),
		new USILGetDimensionsFixer(),
		new USILCompareOrderOptimizer(),
		new USILAddNegativeOptimizer(),
		new USILAndOptimizer(),
		new USILForLoopOptimizer()
	};

	public static void Apply(UShaderProgram shader, ShaderSubProgram shaderData)
	{
		for (int i = 0; i < OPTIMIZER_TYPES.Length; i = checked(i + 1))
		{
			OPTIMIZER_TYPES[i].Run(shader, shaderData);
		}
	}
}
