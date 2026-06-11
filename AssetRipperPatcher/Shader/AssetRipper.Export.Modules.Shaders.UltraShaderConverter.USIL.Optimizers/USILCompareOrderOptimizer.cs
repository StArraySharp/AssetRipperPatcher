using System.Collections.Generic;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL.Optimizers;

public class USILCompareOrderOptimizer : IUSILOptimizer
{
	public bool Run(UShaderProgram shader, ShaderSubProgram shaderData)
	{
		bool changes = false;
		List<USILInstruction> instructions = shader.instructions;
		foreach (USILInstruction instruction in instructions)
		{
			if (instruction.IsComparisonType())
			{
				USILOperand leftOperand = instruction.srcOperands[0];
				USILOperand rightOperand = instruction.srcOperands[1];
				bool leftIsConstant = IsOperandFullyConstant(leftOperand);
				bool rightIsConstant = IsOperandFullyConstant(rightOperand);
				if (leftIsConstant && !rightIsConstant)
				{
					instruction.srcOperands[0] = rightOperand;
					instruction.srcOperands[1] = leftOperand;
					instruction.instructionType = FlipCompareType(instruction.instructionType);
					changes = true;
				}
			}
		}
		return changes;
	}

	private static bool IsOperandFullyConstant(USILOperand operand)
	{
		USILOperandType operandType = operand.operandType;
		if ((uint)(operandType - 30) <= 1u)
		{
			return true;
		}
		if (operand.operandType == USILOperandType.Multiple)
		{
			bool fullyConstant = true;
			USILOperand[] children = operand.children;
			foreach (USILOperand child in children)
			{
				fullyConstant &= IsOperandFullyConstant(child);
			}
		}
		return false;
	}

	private static USILInstructionType FlipCompareType(USILInstructionType type)
	{
		if (1 == 0)
		{
		}
		USILInstructionType result = type switch
		{
			USILInstructionType.LessThan => USILInstructionType.GreaterThan, 
			USILInstructionType.GreaterThan => USILInstructionType.LessThan, 
			USILInstructionType.LessThanOrEqual => USILInstructionType.GreaterThanOrEqual, 
			USILInstructionType.GreaterThanOrEqual => USILInstructionType.LessThanOrEqual, 
			_ => type, 
		};
		if (1 == 0)
		{
		}
		return result;
	}
}
