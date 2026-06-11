#define DEBUG
using System.Collections.Generic;
using System.Diagnostics;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL.Optimizers;

public class USILMatrixMulOptimizer : IUSILOptimizer
{
	private static readonly int[] XYZW_MASK = new int[4] { 0, 1, 2, 3 };

	private static readonly int[] XXXX_MASK = new int[4];

	private static readonly int[] YYYY_MASK = new int[4] { 1, 1, 1, 1 };

	private static readonly int[] ZZZZ_MASK = new int[4] { 2, 2, 2, 2 };

	private static readonly int[] WWWW_MASK = new int[4] { 3, 3, 3, 3 };

	private static readonly int[] XYZ_MASK = new int[3] { 0, 1, 2 };

	private static readonly int[] XXX_MASK = new int[3];

	private static readonly int[] YYY_MASK = new int[3] { 1, 1, 1 };

	private static readonly int[] ZZZ_MASK = new int[3] { 2, 2, 2 };

	public bool Run(UShaderProgram shader, ShaderSubProgram shaderData)
	{
		bool changes = false;
		changes |= ReplaceMulMatrixVec4W1(shader);
		changes |= ReplaceMulMatrixVec4(shader);
		return changes | ReplaceMulMatrixVec3(shader);
	}

	private static bool ReplaceMulMatrixVec4W1(UShaderProgram shader)
	{
		bool changes = false;
		List<USILInstruction> insts = shader.instructions;
		checked
		{
			for (int i = 0; i < insts.Count - 3; i++)
			{
				if (!USILOptimizerUtil.DoOpcodesMatch(insts, i, new USILInstructionType[4]
				{
					USILInstructionType.Multiply,
					USILInstructionType.MultiplyAdd,
					USILInstructionType.MultiplyAdd,
					USILInstructionType.Add
				}))
				{
					continue;
				}
				USILInstruction inst0 = insts[i];
				USILInstruction inst1 = insts[i + 1];
				USILInstruction inst2 = insts[i + 2];
				USILInstruction inst3 = insts[i + 3];
				if (inst0.srcOperands[1].operandType != USILOperandType.Matrix || inst0.srcOperands[1].arrayIndex != 1 || !USILOptimizerUtil.DoMasksMatch(inst0.srcOperands[1], XYZW_MASK) || inst1.srcOperands[0].operandType != USILOperandType.Matrix || inst1.srcOperands[0].arrayIndex != 0 || !USILOptimizerUtil.DoMasksMatch(inst1.srcOperands[0], XYZW_MASK) || inst2.srcOperands[0].operandType != USILOperandType.Matrix || inst2.srcOperands[0].arrayIndex != 2 || !USILOptimizerUtil.DoMasksMatch(inst2.srcOperands[0], XYZW_MASK) || inst3.srcOperands[1].operandType != USILOperandType.Matrix || inst3.srcOperands[1].arrayIndex != 3 || !USILOptimizerUtil.DoMasksMatch(inst3.srcOperands[1], XYZW_MASK))
				{
					continue;
				}
				Debug.Assert(inst0.destOperand != null, "inst0.destOperand is not null");
				Debug.Assert(inst1.destOperand != null, "inst1.destOperand is not null");
				Debug.Assert(inst2.destOperand != null, "inst2.destOperand is not null");
				Debug.Assert(inst3.destOperand != null, "inst3.destOperand is not null");
				int tmp0Index = inst0.destOperand.registerIndex;
				int tmp1Index = inst1.destOperand.registerIndex;
				int tmp2Index = inst2.destOperand.registerIndex;
				int tmp3Index = inst3.destOperand.registerIndex;
				if (tmp0Index == tmp1Index && tmp1Index == tmp2Index && tmp2Index == tmp3Index && inst0.destOperand.registerIndex == tmp0Index && inst1.destOperand.registerIndex == tmp0Index && inst1.srcOperands[2].registerIndex == tmp0Index && inst2.srcOperands[2].registerIndex == tmp0Index && inst2.destOperand.registerIndex == tmp1Index && inst3.srcOperands[0].registerIndex == tmp1Index)
				{
					int inpIndex = inst0.srcOperands[0].registerIndex;
					if (inst0.srcOperands[0].registerIndex == inpIndex && USILOptimizerUtil.DoMasksMatch(inst0.srcOperands[0], YYYY_MASK) && inst1.srcOperands[1].registerIndex == inpIndex && USILOptimizerUtil.DoMasksMatch(inst1.srcOperands[1], XXXX_MASK) && inst2.srcOperands[1].registerIndex == inpIndex && USILOptimizerUtil.DoMasksMatch(inst2.srcOperands[1], ZZZZ_MASK))
					{
						USILOperand mulInputVec3Operand = new USILOperand(inst0.srcOperands[0]);
						USILOperand mulInputMat4x4Operand = new USILOperand(inst0.srcOperands[1]);
						USILOperand mulOutputOperand = new USILOperand(inst3.destOperand);
						mulInputMat4x4Operand.displayMask = false;
						mulInputVec3Operand.mask = new int[3] { 0, 1, 2 };
						USILOperand uSILOperand = new USILOperand();
						uSILOperand.operandType = USILOperandType.ImmediateFloat;
						uSILOperand.immValueFloat = new float[1] { 1f };
						USILOperand mulInput1Operand = uSILOperand;
						uSILOperand = new USILOperand();
						uSILOperand.operandType = USILOperandType.Multiple;
						uSILOperand.children = new USILOperand[2] { mulInputVec3Operand, mulInput1Operand };
						USILOperand mulInputVec4Operand = uSILOperand;
						USILInstruction mulInstruction = new USILInstruction
						{
							instructionType = USILInstructionType.MultiplyMatrixByVector,
							destOperand = mulOutputOperand,
							srcOperands = new List<USILOperand> { mulInputMat4x4Operand, mulInputVec4Operand }
						};
						insts.RemoveRange(i, 4);
						insts.Insert(i, mulInstruction);
						changes = true;
					}
				}
			}
			return changes;
		}
	}

	private static bool ReplaceMulMatrixVec4(UShaderProgram shader)
	{
		bool changes = false;
		List<USILInstruction> insts = shader.instructions;
		checked
		{
			for (int i = 0; i < insts.Count - 3; i++)
			{
				if (!USILOptimizerUtil.DoOpcodesMatch(insts, i, new USILInstructionType[4]
				{
					USILInstructionType.Multiply,
					USILInstructionType.MultiplyAdd,
					USILInstructionType.MultiplyAdd,
					USILInstructionType.MultiplyAdd
				}))
				{
					continue;
				}
				USILInstruction inst0 = insts[i];
				USILInstruction inst1 = insts[i + 1];
				USILInstruction inst2 = insts[i + 2];
				USILInstruction inst3 = insts[i + 3];
				if (inst0.srcOperands[1].operandType != USILOperandType.Matrix || inst0.srcOperands[1].arrayIndex != 1 || !USILOptimizerUtil.DoMasksMatch(inst0.srcOperands[1], XYZW_MASK) || inst1.srcOperands[0].operandType != USILOperandType.Matrix || inst1.srcOperands[0].arrayIndex != 0 || !USILOptimizerUtil.DoMasksMatch(inst1.srcOperands[0], XYZW_MASK) || inst2.srcOperands[0].operandType != USILOperandType.Matrix || inst2.srcOperands[0].arrayIndex != 2 || !USILOptimizerUtil.DoMasksMatch(inst2.srcOperands[0], XYZW_MASK) || inst3.srcOperands[0].operandType != USILOperandType.Matrix || inst3.srcOperands[0].arrayIndex != 3 || !USILOptimizerUtil.DoMasksMatch(inst3.srcOperands[0], XYZW_MASK))
				{
					continue;
				}
				Debug.Assert(inst0.destOperand != null, "inst0.destOperand is not null");
				Debug.Assert(inst1.destOperand != null, "inst1.destOperand is not null");
				Debug.Assert(inst2.destOperand != null, "inst2.destOperand is not null");
				Debug.Assert(inst3.destOperand != null, "inst3.destOperand is not null");
				int tmp0Index = inst0.destOperand.registerIndex;
				int tmp1Index = inst1.destOperand.registerIndex;
				int tmp2Index = inst2.destOperand.registerIndex;
				int tmp3Index = inst3.destOperand.registerIndex;
				if (tmp0Index != tmp1Index || tmp1Index != tmp2Index || tmp2Index != tmp3Index)
				{
					continue;
				}
				int tmpIndex = inst0.destOperand.registerIndex;
				if (inst0.destOperand.registerIndex == tmpIndex && inst1.destOperand.registerIndex == tmpIndex && inst1.srcOperands[2].registerIndex == tmpIndex && inst2.destOperand.registerIndex == tmpIndex && inst2.srcOperands[2].registerIndex == tmpIndex && inst3.srcOperands[2].registerIndex == tmpIndex)
				{
					int inpIndex = inst0.srcOperands[0].registerIndex;
					if (inst0.srcOperands[0].registerIndex == inpIndex && USILOptimizerUtil.DoMasksMatch(inst0.srcOperands[0], YYYY_MASK) && inst1.srcOperands[1].registerIndex == inpIndex && USILOptimizerUtil.DoMasksMatch(inst1.srcOperands[1], XXXX_MASK) && inst2.srcOperands[1].registerIndex == inpIndex && USILOptimizerUtil.DoMasksMatch(inst2.srcOperands[1], ZZZZ_MASK) && inst3.srcOperands[1].registerIndex == inpIndex && USILOptimizerUtil.DoMasksMatch(inst3.srcOperands[1], WWWW_MASK))
					{
						USILOperand mulInputVec4Operand = new USILOperand(inst0.srcOperands[0]);
						USILOperand mulInputMat4x4Operand = new USILOperand(inst0.srcOperands[1]);
						USILOperand mulOutputOperand = new USILOperand(inst3.destOperand);
						mulInputMat4x4Operand.displayMask = false;
						mulInputVec4Operand.mask = new int[4] { 0, 1, 2, 3 };
						USILInstruction mulInstruction = new USILInstruction
						{
							instructionType = USILInstructionType.MultiplyMatrixByVector,
							destOperand = mulOutputOperand,
							srcOperands = new List<USILOperand> { mulInputMat4x4Operand, mulInputVec4Operand }
						};
						insts.RemoveRange(i, 4);
						insts.Insert(i, mulInstruction);
						changes = true;
					}
				}
			}
			return changes;
		}
	}

	private static bool ReplaceMulMatrixVec3(UShaderProgram shader)
	{
		bool changes = false;
		List<USILInstruction> insts = shader.instructions;
		checked
		{
			for (int i = 0; i < insts.Count - 3; i++)
			{
				if (!USILOptimizerUtil.DoOpcodesMatch(insts, i, new USILInstructionType[4]
				{
					USILInstructionType.Multiply,
					USILInstructionType.MultiplyAdd,
					USILInstructionType.MultiplyAdd,
					USILInstructionType.Add
				}))
				{
					continue;
				}
				USILInstruction inst0 = insts[i];
				USILInstruction inst1 = insts[i + 1];
				USILInstruction inst2 = insts[i + 2];
				USILInstruction inst3 = insts[i + 3];
				if (inst0.srcOperands[1].operandType != USILOperandType.Matrix || inst0.srcOperands[1].arrayIndex != 1 || !USILOptimizerUtil.DoMasksMatch(inst0.srcOperands[1], XYZ_MASK) || inst1.srcOperands[0].operandType != USILOperandType.Matrix || inst1.srcOperands[0].arrayIndex != 0 || !USILOptimizerUtil.DoMasksMatch(inst1.srcOperands[0], XYZ_MASK) || inst2.srcOperands[0].operandType != USILOperandType.Matrix || inst2.srcOperands[0].arrayIndex != 2 || !USILOptimizerUtil.DoMasksMatch(inst2.srcOperands[0], XYZ_MASK) || inst3.srcOperands[1].operandType != USILOperandType.Matrix || inst3.srcOperands[1].arrayIndex != 3 || !USILOptimizerUtil.DoMasksMatch(inst3.srcOperands[1], XYZ_MASK))
				{
					continue;
				}
				Debug.Assert(inst0.destOperand != null, "inst0.destOperand is not null");
				Debug.Assert(inst1.destOperand != null, "inst1.destOperand is not null");
				Debug.Assert(inst2.destOperand != null, "inst2.destOperand is not null");
				Debug.Assert(inst3.destOperand != null, "inst3.destOperand is not null");
				int tmp0Index = inst0.destOperand.registerIndex;
				int tmp1Index = inst1.destOperand.registerIndex;
				int tmp2Index = inst2.destOperand.registerIndex;
				int tmp3Index = inst3.destOperand.registerIndex;
				if (tmp0Index == tmp1Index && tmp1Index == tmp2Index && tmp2Index == tmp3Index && inst0.destOperand.registerIndex == tmp0Index && inst1.destOperand.registerIndex == tmp0Index && inst1.srcOperands[2].registerIndex == tmp0Index && inst2.srcOperands[2].registerIndex == tmp0Index && inst2.destOperand.registerIndex == tmp1Index && inst3.srcOperands[0].registerIndex == tmp1Index)
				{
					int inpIndex = inst0.srcOperands[0].registerIndex;
					if (inst0.srcOperands[0].registerIndex == inpIndex && USILOptimizerUtil.DoMasksMatch(inst0.srcOperands[0], YYY_MASK) && inst1.srcOperands[1].registerIndex == inpIndex && USILOptimizerUtil.DoMasksMatch(inst1.srcOperands[1], XXX_MASK) && inst2.srcOperands[1].registerIndex == inpIndex && USILOptimizerUtil.DoMasksMatch(inst2.srcOperands[1], ZZZ_MASK))
					{
						USILOperand mulInputVec3Operand = new USILOperand(inst0.srcOperands[0]);
						USILOperand mulInputMat3x3Operand = new USILOperand(inst0.srcOperands[1]);
						USILOperand mulOutputOperand = new USILOperand(inst3.destOperand);
						mulInputMat3x3Operand.displayMask = false;
						mulInputVec3Operand.mask = new int[3] { 0, 1, 2 };
						USILInstruction mulInstruction = new USILInstruction
						{
							instructionType = USILInstructionType.MultiplyMatrixByVector,
							destOperand = mulOutputOperand,
							srcOperands = new List<USILOperand> { mulInputMat3x3Operand, mulInputVec3Operand }
						};
						insts.RemoveRange(i, 4);
						insts.Insert(i, mulInstruction);
						changes = true;
					}
				}
			}
			return changes;
		}
	}
}
