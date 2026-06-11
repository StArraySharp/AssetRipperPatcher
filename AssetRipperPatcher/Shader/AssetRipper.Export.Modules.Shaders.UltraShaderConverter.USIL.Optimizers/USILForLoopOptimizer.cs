using System.Collections.Generic;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL.Optimizers;

public class USILForLoopOptimizer : IUSILOptimizer
{
	private class LoopInstanceInfo
	{
		public USILOperand iterRegOp;

		public USILOperand compOp;

		public USILInstructionType compType;

		public bool isInt;

		public bool isUnsigned;

		public int startIndex;

		public int loopDepth;

		public LoopInstanceInfo(USILOperand iterRegOp, USILOperand compOp, USILInstructionType compType, bool isInt, bool isUnsigned, int startIndex, int loopDepth)
		{
			this.iterRegOp = iterRegOp;
			this.compOp = compOp;
			this.compType = compType;
			this.isInt = isInt;
			this.isUnsigned = isUnsigned;
			this.startIndex = startIndex;
			this.loopDepth = loopDepth;
		}
	}

	public bool Run(UShaderProgram shader, ShaderSubProgram shaderData)
	{
		bool changes = false;
		return changes | ReplaceForLoop(shader);
	}

	private bool ReplaceForLoop(UShaderProgram shader)
	{
		bool changes = false;
		Stack<LoopInstanceInfo> loopInfos = new Stack<LoopInstanceInfo>();
		int loopDepth = 0;
		List<USILInstruction> insts = shader.instructions;
		checked
		{
			for (int i = 0; i < insts.Count - 5; i++)
			{
				if (insts[i].instructionType == USILInstructionType.EndLoop)
				{
					loopDepth--;
					if (loopInfos.Count > 0 && loopInfos.Peek().loopDepth == loopDepth)
					{
						loopInfos.Pop();
					}
				}
				if (insts[i].instructionType == USILInstructionType.Loop && IsComparisonInstruction(insts[i + 1]) && IsIfInstruction(insts[i + 2]) && insts[i + 3].instructionType == USILInstructionType.Break && insts[i + 4].instructionType == USILInstructionType.EndIf)
				{
					USILInstruction compInst = insts[i + 1];
					USILInstruction ifInst = insts[i + 2];
					if (compInst.destOperand.registerIndex == ifInst.srcOperands[0].registerIndex && DoMasksMatch(compInst.destOperand.mask, ifInst.srcOperands[0].mask))
					{
						USILOperand iterRegOp = new USILOperand(compInst.srcOperands[0]);
						USILOperand compOp = new USILOperand(compInst.srcOperands[1]);
						USILInstructionType compType = compInst.instructionType;
						bool isInt = compInst.isIntVariant;
						bool isUnsigned = compInst.isIntUnsigned;
						LoopInstanceInfo loopInfo = new LoopInstanceInfo(iterRegOp, compOp, compType, isInt, isUnsigned, i, loopDepth);
						loopInfos.Push(loopInfo);
					}
				}
				if (IsAddInstruction(insts[i]) && insts[i + 1].instructionType == USILInstructionType.EndLoop && loopInfos.Count > 0 && loopInfos.Peek().loopDepth == loopDepth - 1)
				{
					LoopInstanceInfo loopInfo2 = loopInfos.Pop();
					int startIndex = loopInfo2.startIndex;
					USILInstruction forLoopInst = insts[startIndex];
					USILInstruction addIterInst = insts[i];
					USILNumberType numberType;
					float addCount;
					if (addIterInst.isIntVariant)
					{
						numberType = ((!addIterInst.isIntUnsigned) ? USILNumberType.Int : USILNumberType.UnsignedInt);
						addCount = addIterInst.srcOperands[1].immValueInt[0];
					}
					else
					{
						numberType = USILNumberType.Float;
						addCount = addIterInst.srcOperands[1].immValueFloat[0];
					}
					if (addIterInst.instructionType == USILInstructionType.Subtract)
					{
						addCount *= -1f;
					}
					forLoopInst.instructionType = USILInstructionType.ForLoop;
					forLoopInst.srcOperands = new List<USILOperand>
					{
						loopInfo2.iterRegOp,
						loopInfo2.compOp,
						new USILOperand(unchecked((int)InvertCompareType(loopInfo2.compType))),
						new USILOperand(unchecked((int)numberType)),
						new USILOperand(addCount),
						new USILOperand(loopDepth - 1)
					};
					insts.RemoveAt(i);
					insts.RemoveAt(startIndex + 1);
					insts.RemoveAt(startIndex + 1);
					insts.RemoveAt(startIndex + 1);
					insts.RemoveAt(startIndex + 1);
					i -= 3;
					changes = true;
				}
				if (loopInfos.Count > 0)
				{
					List<USILOperand> allOperands = GetAllOperands(insts[i]);
					foreach (USILOperand op in allOperands)
					{
						if (op.operandType != USILOperandType.TempRegister || op.mask.Length != 1)
						{
							continue;
						}
						foreach (LoopInstanceInfo loopInfo3 in loopInfos)
						{
							USILOperand iterRegOp2 = loopInfo3.iterRegOp;
							if (op.registerIndex != iterRegOp2.registerIndex || op.mask[0] != iterRegOp2.mask[0])
							{
								break;
							}
							op.metadataName = USILConstants.ITER_CHARS[loopInfo3.loopDepth].ToString();
							op.metadataNameAssigned = true;
							op.displayMask = false;
						}
					}
				}
				if (insts[i].instructionType == USILInstructionType.Loop)
				{
					loopDepth++;
				}
			}
			return changes;
		}
	}

	private static bool IsComparisonInstruction(USILInstruction instruction)
	{
		USILInstructionType instructionType = instruction.instructionType;
		USILInstructionType uSILInstructionType = instructionType;
		if ((uint)(uSILInstructionType - 42) <= 5u)
		{
			return true;
		}
		return false;
	}

	private static bool IsIfInstruction(USILInstruction instruction)
	{
		USILInstructionType instructionType = instruction.instructionType;
		USILInstructionType uSILInstructionType = instructionType;
		if ((uint)(uSILInstructionType - 48) <= 1u)
		{
			return true;
		}
		return false;
	}

	private static bool IsAddInstruction(USILInstruction instruction)
	{
		USILInstructionType instructionType = instruction.instructionType;
		USILInstructionType uSILInstructionType = instructionType;
		if ((uint)(uSILInstructionType - 2) <= 1u)
		{
			return true;
		}
		return false;
	}

	private static USILInstructionType InvertCompareType(USILInstructionType type)
	{
		return type switch
		{
			USILInstructionType.LessThan => USILInstructionType.GreaterThanOrEqual, 
			USILInstructionType.GreaterThan => USILInstructionType.LessThanOrEqual, 
			USILInstructionType.LessThanOrEqual => USILInstructionType.GreaterThan, 
			USILInstructionType.GreaterThanOrEqual => USILInstructionType.LessThan, 
			_ => type, 
		};
	}

	private static bool DoOpcodesMatch(List<USILInstruction> insts, int startIndex, USILInstructionType[] instTypes)
	{
		checked
		{
			if (startIndex + instTypes.Length > insts.Count)
			{
				return false;
			}
			for (int i = 0; i < instTypes.Length; i++)
			{
				if (insts[startIndex + i].instructionType != instTypes[i])
				{
					return false;
				}
			}
			return true;
		}
	}

	private static bool DoMasksMatch(int[] maskA, int[] maskB)
	{
		if (maskA.Length != maskB.Length)
		{
			return false;
		}
		for (int i = 0; i < maskB.Length; i = checked(i + 1))
		{
			if (maskA[i] != maskB[i])
			{
				return false;
			}
		}
		return true;
	}

	private List<USILOperand> GetAllOperands(USILInstruction inst)
	{
		List<USILOperand> operands = new List<USILOperand>();
		if (inst.destOperand != null)
		{
			operands.AddRange(GetAllOperands(inst.destOperand));
		}
		if (inst.srcOperands != null)
		{
			foreach (USILOperand srcOp in inst.srcOperands)
			{
				operands.AddRange(GetAllOperands(srcOp));
			}
		}
		return operands;
	}

	private List<USILOperand> GetAllOperands(USILOperand operand)
	{
		if (operand.arrayRelative == null && (operand.children == null || operand.children.Length == 0))
		{
			return new List<USILOperand> { operand };
		}
		List<USILOperand> operands = new List<USILOperand> { operand };
		if (operand.arrayRelative != null)
		{
			operands.AddRange(GetAllOperands(operand.arrayRelative));
		}
		if (operand.children != null)
		{
			USILOperand[] children = operand.children;
			foreach (USILOperand child in children)
			{
				operands.AddRange(GetAllOperands(child));
			}
		}
		return operands;
	}
}
