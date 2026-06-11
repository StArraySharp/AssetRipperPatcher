using System.Collections.Generic;
using System.Linq;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.DirectX;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL.Metadders;

public class USILInputOutputMetadder : IUSILOptimizer
{
	public bool Run(UShaderProgram shader, ShaderSubProgram shaderData)
	{
		List<USILInstruction> instructions = shader.instructions;
		foreach (USILInstruction instruction in instructions)
		{
			if (instruction.destOperand != null)
			{
				UseMetadata(instruction.destOperand, shader);
			}
			foreach (USILOperand operand in instruction.srcOperands)
			{
				UseMetadata(operand, shader);
			}
		}
		return true;
	}

	private static void UseMetadata(USILOperand operand, UShaderProgram shader)
	{
		if (operand.operandType == USILOperandType.InputRegister)
		{
			int searchMask = ((operand.mask.Length != 0) ? (1 << operand.mask[0]) : 0);
			USILInputOutput input = shader.inputs.First((USILInputOutput uSILInputOutput) => uSILInputOutput.register == operand.registerIndex && (searchMask & uSILInputOutput.mask) == searchMask);
			operand.mask = MatchMaskToInputOutput(operand.mask, input.mask, moveSwizzles: true);
			if (shader.shaderFunctionType == UShaderFunctionType.Fragment && input.type == "SV_IsFrontFace")
			{
				operand.metadataName = input.name;
			}
			else
			{
				USILOperand uSILOperand = operand;
				UShaderFunctionType shaderFunctionType = shader.shaderFunctionType;
				if (1 == 0)
				{
				}
				string metadataName = shaderFunctionType switch
				{
					UShaderFunctionType.Vertex => "v." + input.name, 
					UShaderFunctionType.Fragment => "inp." + input.name, 
					_ => "unk_input." + input.name, 
				};
				if (1 == 0)
				{
				}
				uSILOperand.metadataName = metadataName;
			}
			operand.metadataNameAssigned = true;
			return;
		}
		if (operand.operandType == USILOperandType.OutputRegister)
		{
			int searchMask2 = 0;
			for (int i = 0; i < operand.mask.Length; i = checked(i + 1))
			{
				searchMask2 |= 1 << operand.mask[i];
			}
			List<USILInputOutput> outputs = shader.outputs.Where((USILInputOutput o) => o.register == operand.registerIndex && (searchMask2 & o.mask) != 0).ToList();
			{
				foreach (USILInputOutput output in outputs)
				{
					int[] matchedMask = MatchMaskToInputOutput(operand.mask, output.mask, moveSwizzles: true);
					int[] realMatchedMask = MatchMaskToInputOutput(operand.mask, output.mask, moveSwizzles: false);
					operand.mask = matchedMask;
					USILOperand uSILOperand2 = operand;
					UShaderFunctionType shaderFunctionType2 = shader.shaderFunctionType;
					if (1 == 0)
					{
					}
					string metadataName = shaderFunctionType2 switch
					{
						UShaderFunctionType.Vertex => "o." + output.name, 
						UShaderFunctionType.Fragment => "o." + output.name, 
						_ => "unk_output." + output.name, 
					};
					if (1 == 0)
					{
					}
					uSILOperand2.metadataName = metadataName;
					operand.metadataNameAssigned = true;
				}
				return;
			}
		}
		if (DXShaderNamingUtils.HasSpecialInputOutputName(operand.operandType))
		{
			string name = DXShaderNamingUtils.GetSpecialInputOutputName(operand.operandType);
			USILOperand uSILOperand3 = operand;
			UShaderFunctionType shaderFunctionType3 = shader.shaderFunctionType;
			if (1 == 0)
			{
			}
			string metadataName = shaderFunctionType3 switch
			{
				UShaderFunctionType.Vertex => "o." + name, 
				UShaderFunctionType.Fragment => "o." + name, 
				_ => "unk_special." + name, 
			};
			if (1 == 0)
			{
			}
			uSILOperand3.metadataName = metadataName;
			operand.metadataNameAssigned = true;
		}
	}

	private static int[] MatchMaskToInputOutput(int[] mask, int maskTest, bool moveSwizzles)
	{
		int moveCount = 0;
		checked
		{
			int i;
			for (i = 0; i < 4; i++)
			{
				if ((maskTest & 1) == 1)
				{
					break;
				}
				moveCount++;
				maskTest >>= 1;
			}
			int bitCount = 0;
			for (; i < 4; i++)
			{
				if ((maskTest & 1) == 0)
				{
					break;
				}
				bitCount++;
				maskTest >>= 1;
			}
			List<int> result = new List<int>();
			for (int j = 0; j < mask.Length; j++)
			{
				if (mask[j] >= moveCount && mask[j] < bitCount + moveCount)
				{
					if (moveSwizzles)
					{
						result.Add(mask[j] - moveCount);
					}
					else
					{
						result.Add(mask[j]);
					}
				}
			}
			return result.ToArray();
		}
	}
}
