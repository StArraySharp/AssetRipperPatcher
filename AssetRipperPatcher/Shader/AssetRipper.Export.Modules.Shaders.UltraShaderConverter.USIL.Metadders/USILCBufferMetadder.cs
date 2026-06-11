using System.Collections.Generic;
using System.Linq;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.Modules.Shaders.ShaderBlob.Parameters;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL.Metadders;

public class USILCBufferMetadder : IUSILOptimizer
{
	public bool Run(UShaderProgram shader, ShaderSubProgram shaderData)
	{
		List<USILInstruction> instructions = shader.instructions;
		foreach (USILInstruction instruction in instructions)
		{
			if (instruction.destOperand != null)
			{
				UseMetadata(instruction.destOperand, shaderData);
			}
			foreach (USILOperand operand in instruction.srcOperands)
			{
				UseMetadata(operand, shaderData);
			}
		}
		return true;
	}

	private static void UseMetadata(USILOperand operand, ShaderSubProgram shaderData)
	{
		if (operand.operandType != USILOperandType.ConstantBuffer)
		{
			return;
		}
		int cbRegIdx = operand.registerIndex;
		int cbArrIdx = operand.arrayIndex;
		List<int> operandMaskAddresses = new List<int>();
		int[] mask = operand.mask;
		checked
		{
			foreach (int operandMask in mask)
			{
				operandMaskAddresses.Add(cbArrIdx * 16 + operandMask * 4);
			}
			HashSet<NumericShaderParameter> cbParams = new HashSet<NumericShaderParameter>();
			List<int> cbMasks = new List<int>();
			BufferBinding binding = shaderData.ConstantBufferBindings.First((BufferBinding b) => b.Index == cbRegIdx);
			ConstantBuffer constantBuffer = shaderData.ConstantBuffers.First((ConstantBuffer b) => b.Name == binding.Name);
			NumericShaderParameter[] allNumericParams = constantBuffer.AllNumericParams;
			foreach (NumericShaderParameter param in allNumericParams)
			{
				int paramCbStart = param.Index;
				int paramCbSize = param.RowCount * param.ColumnCount * 4;
				int paramCbEnd = paramCbStart + paramCbSize;
				unchecked
				{
					foreach (int operandMaskAddress in operandMaskAddresses)
					{
						if (operandMaskAddress >= paramCbStart && operandMaskAddress < paramCbEnd)
						{
							cbParams.Add(param);
							int maskIndex = checked(operandMaskAddress - paramCbStart) / 4;
							if (param.IsMatrix)
							{
								maskIndex %= 4;
							}
							cbMasks.Add(maskIndex);
						}
					}
				}
			}
			StructParameter[] structParams = constantBuffer.StructParams;
			foreach (StructParameter stParam in structParams)
			{
				NumericShaderParameter[] allNumericMembers = stParam.AllNumericMembers;
				foreach (NumericShaderParameter cbParam in allNumericMembers)
				{
					int paramCbStart2 = cbParam.Index;
					int paramCbSize2 = cbParam.RowCount * cbParam.ColumnCount * 4;
					int paramCbEnd2 = paramCbStart2 + paramCbSize2;
					unchecked
					{
						foreach (int operandMaskAddress2 in operandMaskAddresses)
						{
							if (operandMaskAddress2 >= paramCbStart2 && operandMaskAddress2 < paramCbEnd2)
							{
								cbParams.Add(cbParam);
								int maskIndex2 = checked(operandMaskAddress2 - paramCbStart2) / 4;
								if (cbParam.IsMatrix)
								{
									maskIndex2 %= 4;
								}
								cbMasks.Add(maskIndex2);
							}
						}
					}
				}
			}
			if (cbParams.Count > 1)
			{
				operand.operandType = USILOperandType.Multiple;
				operand.children = new USILOperand[cbParams.Count];
				int i2 = 0;
				List<string> paramStrs = new List<string>();
				{
					foreach (NumericShaderParameter param2 in cbParams)
					{
						USILOperand childOperand = new USILOperand();
						childOperand.operandType = USILOperandType.ConstantBuffer;
						childOperand.mask = MatchMaskToConstantBuffer(operand.mask, param2.Index, param2.RowCount);
						childOperand.metadataName = param2.Name;
						childOperand.metadataNameAssigned = true;
						childOperand.arrayRelative = operand.arrayRelative;
						childOperand.arrayIndex -= unchecked(param2.Index / 16);
						childOperand.metadataNameWithArray = operand.arrayRelative != null && !param2.IsMatrix;
						operand.children[i2++] = childOperand;
					}
					return;
				}
			}
			if (cbParams.Count == 1)
			{
				NumericShaderParameter param3 = cbParams.First();
				if (param3.IsMatrix)
				{
					operand.operandType = USILOperandType.Matrix;
					operand.transposeMatrix = true;
				}
				operand.arrayIndex -= unchecked(param3.Index / 16);
				operand.mask = cbMasks.ToArray();
				operand.metadataName = param3.Name;
				operand.metadataNameAssigned = true;
				operand.metadataNameWithArray = operand.arrayRelative != null && !param3.IsMatrix;
				if (cbMasks.Count == param3.RowCount && !param3.IsMatrix)
				{
					operand.displayMask = false;
				}
			}
		}
	}

	private static int[] MatchMaskToConstantBuffer(int[] mask, int pos, int size)
	{
		int offset = pos / 4 % 4;
		List<int> result = new List<int>();
		checked
		{
			for (int i = 0; i < mask.Length; i++)
			{
				if (mask[i] >= offset && mask[i] < offset + size)
				{
					result.Add(mask[i] - offset);
				}
			}
			return result.ToArray();
		}
	}
}
