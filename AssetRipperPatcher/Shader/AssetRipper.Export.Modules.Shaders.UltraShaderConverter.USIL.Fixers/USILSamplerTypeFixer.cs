using System.Collections.Generic;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL.Fixers;

public class USILSamplerTypeFixer : IUSILOptimizer
{
	public static readonly HashSet<string?> BUILTIN_SAMPLER_TEXTURE_NAMES = new HashSet<string> { "unity_Lightmap", "unity_ShadowMask", "unity_DynamicLightmap", "unity_SpecCube0", "unity_ProbeVolumeSH", "_ShadowMapTexture" };

	public bool Run(UShaderProgram shader, ShaderSubProgram shaderData)
	{
		bool changes = false;
		List<USILInstruction> instructions = shader.instructions;
		foreach (USILInstruction instruction in instructions)
		{
			if (!instruction.IsSampleType())
			{
				continue;
			}
			USILOperand sampleOperand = instruction.srcOperands[2];
			if (sampleOperand.operandType == USILOperandType.SamplerRegister || !sampleOperand.metadataNameAssigned)
			{
				break;
			}
			if (BUILTIN_SAMPLER_TEXTURE_NAMES.Contains(sampleOperand.metadataName))
			{
				int samplerTypeIdx = GetSamplerTypeIdx(instruction.instructionType);
				if (samplerTypeIdx != -1)
				{
					instruction.srcOperands[samplerTypeIdx] = new USILOperand(1);
					changes = true;
				}
			}
		}
		return changes;
	}

	private static int GetSamplerTypeIdx(USILInstructionType type)
	{
		if (1 == 0)
		{
		}
		int result = type switch
		{
			USILInstructionType.Sample => 3, 
			USILInstructionType.SampleLODBias => 4, 
			USILInstructionType.SampleComparison => 4, 
			USILInstructionType.SampleComparisonLODZero => 4, 
			USILInstructionType.SampleLOD => 4, 
			_ => -1, 
		};
		if (1 == 0)
		{
		}
		return result;
	}
}
