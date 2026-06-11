#define DEBUG
using System.Collections.Generic;
using System.Diagnostics;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL.Optimizers;

public class USILAndOptimizer : IUSILOptimizer
{
	public bool Run(UShaderProgram shader, ShaderSubProgram shaderData)
	{
		bool changes = false;
		HashSet<string> _comparisonResultRegisters = new HashSet<string>();
		List<USILInstruction> instructions = shader.instructions;
		foreach (USILInstruction instruction in instructions)
		{
			USILOperand destOperand = instruction.destOperand;
			if (IsComparisonOrBooleanOp(instruction))
			{
				if (destOperand != null)
				{
					SetRegisterIsComparison(destOperand, isComparison: true, _comparisonResultRegisters);
				}
			}
			else
			{
				if (instruction.instructionType != USILInstructionType.And)
				{
					continue;
				}
				USILOperand leftOperand = instruction.srcOperands[0];
				USILOperand rightOperand = instruction.srcOperands[1];
				if (rightOperand.operandType == USILOperandType.ImmediateFloat && rightOperand.immValueFloat[0] == 1f)
				{
					instruction.instructionType = USILInstructionType.MoveConditional;
					instruction.srcOperands = new List<USILOperand>
					{
						leftOperand,
						new USILOperand
						{
							operandType = USILOperandType.ImmediateFloat,
							immValueFloat = new float[1] { 1f }
						},
						new USILOperand
						{
							operandType = USILOperandType.ImmediateFloat,
							immValueFloat = new float[1]
						}
					};
					if (destOperand != null)
					{
						SetRegisterIsComparison(destOperand, isComparison: false, _comparisonResultRegisters);
					}
					continue;
				}
				bool leftIsComparison = IsRegisterComparison(leftOperand, _comparisonResultRegisters);
				bool rightIsComparison = IsRegisterComparison(rightOperand, _comparisonResultRegisters);
				if (leftIsComparison || rightIsComparison)
				{
					USILOperand cmpOperand = (leftIsComparison ? leftOperand : rightOperand);
					USILOperand resOperand = (leftIsComparison ? rightOperand : leftOperand);
					instruction.instructionType = USILInstructionType.MoveConditional;
					instruction.srcOperands = new List<USILOperand>
					{
						cmpOperand,
						resOperand,
						new USILOperand
						{
							operandType = USILOperandType.ImmediateFloat,
							immValueFloat = new float[1]
						}
					};
				}
				Debug.Assert(destOperand != null, "destOperand != null");
				if (leftIsComparison && rightIsComparison)
				{
					SetRegisterIsComparison(destOperand, isComparison: true, _comparisonResultRegisters);
				}
				else
				{
					SetRegisterIsComparison(destOperand, isComparison: false, _comparisonResultRegisters);
				}
			}
		}
		return changes;
	}

	private static void SetRegisterIsComparison(USILOperand operand, bool isComparison, HashSet<string> _comparisonResultRegisters)
	{
		int[] mask = operand.mask;
		foreach (int maskIdx in mask)
		{
			if (isComparison)
			{
				_comparisonResultRegisters.Add($"{operand.ToString(forceHideMask: true)}.{USILConstants.MASK_CHARS[maskIdx]}");
			}
			else
			{
				_comparisonResultRegisters.Remove($"{operand.ToString(forceHideMask: true)}.{USILConstants.MASK_CHARS[maskIdx]}");
			}
		}
	}

	private static bool IsRegisterComparison(USILOperand operand, HashSet<string> _comparisonResultRegisters)
	{
		int[] mask = operand.mask;
		foreach (int maskIdx in mask)
		{
			if (!_comparisonResultRegisters.Contains($"{operand.ToString(forceHideMask: true)}.{USILConstants.MASK_CHARS[maskIdx]}"))
			{
				return false;
			}
		}
		return true;
	}

	private static bool IsComparisonOrBooleanOp(USILInstruction instruction)
	{
		return instruction.IsComparisonType() || instruction.instructionType == USILInstructionType.Not;
	}
}
