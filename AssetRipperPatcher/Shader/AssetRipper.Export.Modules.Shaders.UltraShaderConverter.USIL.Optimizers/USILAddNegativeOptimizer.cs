using System.Collections.Generic;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL.Optimizers;

public class USILAddNegativeOptimizer : IUSILOptimizer
{
	public bool Run(UShaderProgram shader, ShaderSubProgram shaderData)
	{
		bool changes = false;
		List<USILInstruction> instructions = shader.instructions;
		foreach (USILInstruction instruction in instructions)
		{
			if (instruction.instructionType == USILInstructionType.Add)
			{
				USILOperand leftOperand = instruction.srcOperands[0];
				USILOperand rightOperand = instruction.srcOperands[1];
				if (IsTrulyNegative(rightOperand))
				{
					instruction.instructionType = USILInstructionType.Subtract;
					NegateOperand(rightOperand);
					changes = true;
				}
				else if (IsTrulyNegative(leftOperand) && !IsTrulyNegative(rightOperand))
				{
					instruction.instructionType = USILInstructionType.Subtract;
					NegateOperand(leftOperand);
					instruction.srcOperands[0] = rightOperand;
					instruction.srcOperands[1] = leftOperand;
					changes = true;
				}
			}
		}
		return changes;
	}

	private static bool IsTrulyNegative(USILOperand operand)
	{
		switch (operand.operandType)
		{
		case USILOperandType.ImmediateInt:
		{
			int[] immValueInt = operand.immValueInt;
			foreach (int imm2 in immValueInt)
			{
				if (imm2 > 0)
				{
					return false;
				}
			}
			return true;
		}
		case USILOperandType.ImmediateFloat:
		{
			float[] immValueFloat = operand.immValueFloat;
			foreach (float imm in immValueFloat)
			{
				if (imm > 0f)
				{
					return false;
				}
			}
			return true;
		}
		case USILOperandType.Multiple:
		{
			USILOperand[] children = operand.children;
			foreach (USILOperand child in children)
			{
				if (!IsTrulyNegative(child))
				{
					return false;
				}
			}
			return true;
		}
		default:
			return operand.negative;
		}
	}

	private static void NegateOperand(USILOperand operand)
	{
		checked
		{
			switch (operand.operandType)
			{
			case USILOperandType.ImmediateInt:
			{
				for (int j = 0; j < operand.immValueInt.Length; j++)
				{
					operand.immValueInt[j] = -operand.immValueInt[j];
				}
				break;
			}
			case USILOperandType.ImmediateFloat:
			{
				for (int k = 0; k < operand.immValueFloat.Length; k++)
				{
					operand.immValueFloat[k] = 0f - operand.immValueFloat[k];
				}
				break;
			}
			case USILOperandType.Multiple:
			{
				USILOperand[] children = operand.children;
				foreach (USILOperand child in children)
				{
					NegateOperand(child);
				}
				break;
			}
			default:
				operand.negative = !operand.negative;
				break;
			}
		}
	}
}
