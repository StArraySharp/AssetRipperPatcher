#define DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Blocks;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.DirectX;

public class DirectXProgramToUSIL
{
	private delegate void InstHandler(SHDRInstruction inst);

	private readonly DirectXCompiledShader _dxShader;

	public UShaderProgram shader;

	private readonly Dictionary<Opcode, InstHandler> _instructionHandlers;

	private readonly Dictionary<int, ResourceDimension> _resourceToDimension;

	private List<USILLocal> Locals => shader.locals;

	private List<USILInstruction> Instructions => shader.instructions;

	private List<USILInputOutput> Inputs => shader.inputs;

	private List<USILInputOutput> Outputs => shader.outputs;

	public DirectXProgramToUSIL(DirectXCompiledShader dxShader)
	{
		_dxShader = dxShader;
		shader = new UShaderProgram();
		_instructionHandlers = new Dictionary<Opcode, InstHandler>
		{
			{
				Opcode.mov,
				HandleMov
			},
			{
				Opcode.movc,
				HandleMovc
			},
			{
				Opcode.add,
				HandleAdd
			},
			{
				Opcode.iadd,
				HandleAdd
			},
			{
				Opcode.mul,
				HandleMul
			},
			{
				Opcode.imul,
				HandleIMul
			},
			{
				Opcode.umul,
				HandleIMul
			},
			{
				Opcode.div,
				HandleDiv
			},
			{
				Opcode.udiv,
				HandleDiv
			},
			{
				Opcode.mad,
				HandleMad
			},
			{
				Opcode.imad,
				HandleMad
			},
			{
				Opcode.and,
				HandleAnd
			},
			{
				Opcode.or,
				HandleOr
			},
			{
				Opcode.not,
				HandleNot
			},
			{
				Opcode.ftoi,
				HandleFtoi
			},
			{
				Opcode.ftou,
				HandleFtoi
			},
			{
				Opcode.itof,
				HandleItof
			},
			{
				Opcode.utof,
				HandleItof
			},
			{
				Opcode.min,
				HandleMin
			},
			{
				Opcode.imin,
				HandleMin
			},
			{
				Opcode.umin,
				HandleMin
			},
			{
				Opcode.max,
				HandleMax
			},
			{
				Opcode.imax,
				HandleMax
			},
			{
				Opcode.umax,
				HandleMax
			},
			{
				Opcode.sqrt,
				HandleSqrt
			},
			{
				Opcode.rsq,
				HandleRsq
			},
			{
				Opcode.log,
				HandleLog
			},
			{
				Opcode.exp,
				HandleExp
			},
			{
				Opcode.rcp,
				HandleRcp
			},
			{
				Opcode.frc,
				HandleFrc
			},
			{
				Opcode.round_ni,
				HandleRoundNi
			},
			{
				Opcode.round_pi,
				HandleRoundPi
			},
			{
				Opcode.round_ne,
				HandleRoundNe
			},
			{
				Opcode.round_z,
				HandleRoundZ
			},
			{
				Opcode.sincos,
				HandleSincos
			},
			{
				Opcode.ishl,
				HandleIShl
			},
			{
				Opcode.ishr,
				HandleIShr
			},
			{
				Opcode.ushr,
				HandleIShr
			},
			{
				Opcode.dp2,
				HandleDp2
			},
			{
				Opcode.dp3,
				HandleDp3
			},
			{
				Opcode.dp4,
				HandleDp4
			},
			{
				Opcode.sample,
				HandleSample
			},
			{
				Opcode.sample_c,
				HandleSampleC
			},
			{
				Opcode.sample_c_lz,
				HandleSampleC
			},
			{
				Opcode.sample_l,
				HandleSampleL
			},
			{
				Opcode.sample_b,
				HandleSampleL
			},
			{
				Opcode.sample_d,
				HandleSampleD
			},
			{
				Opcode.ld,
				HandleLd
			},
			{
				Opcode.ldms,
				HandleLd
			},
			{
				Opcode.ld_structured,
				HandleLdStructured
			},
			{
				Opcode.discard,
				HandleDiscard
			},
			{
				Opcode.resinfo,
				HandleResInfo
			},
			{
				Opcode.sampleinfo,
				HandleSampleInfo
			},
			{
				Opcode.deriv_rtx,
				HandleDerivRt
			},
			{
				Opcode.deriv_rty,
				HandleDerivRt
			},
			{
				Opcode.deriv_rtx_coarse,
				HandleDerivRt
			},
			{
				Opcode.deriv_rty_coarse,
				HandleDerivRt
			},
			{
				Opcode.deriv_rtx_fine,
				HandleDerivRt
			},
			{
				Opcode.deriv_rty_fine,
				HandleDerivRt
			},
			{
				Opcode.@if,
				HandleIf
			},
			{
				Opcode.@else,
				HandleElse
			},
			{
				Opcode.endif,
				HandleEndIf
			},
			{
				Opcode.loop,
				HandleLoop
			},
			{
				Opcode.endloop,
				HandleEndLoop
			},
			{
				Opcode.@break,
				HandleBreak
			},
			{
				Opcode.breakc,
				HandleBreakc
			},
			{
				Opcode.@continue,
				HandleContinue
			},
			{
				Opcode.continuec,
				HandleContinuec
			},
			{
				Opcode.eq,
				HandleEq
			},
			{
				Opcode.ieq,
				HandleEq
			},
			{
				Opcode.ne,
				HandleNeq
			},
			{
				Opcode.ine,
				HandleNeq
			},
			{
				Opcode.lt,
				HandleLt
			},
			{
				Opcode.ilt,
				HandleLt
			},
			{
				Opcode.ult,
				HandleLt
			},
			{
				Opcode.ge,
				HandleGe
			},
			{
				Opcode.ige,
				HandleGe
			},
			{
				Opcode.uge,
				HandleGe
			},
			{
				Opcode.ret,
				HandleRet
			},
			{
				Opcode.dcl_temps,
				HandleTemps
			},
			{
				Opcode.dcl_resource,
				HandleResource
			},
			{
				Opcode.customdata,
				HandleCustomData
			}
		};
		_resourceToDimension = new Dictionary<int, ResourceDimension>();
	}

	public void Convert()
	{
		ConvertInstructions();
		ConvertInputs();
		ConvertOutputs();
	}

	private void ConvertInstructions()
	{
		SHDR shdr = _dxShader.Shdr;
		AddOutputLocal();
		List<SHDRInstruction> instructions = shdr.shaderInstructions;
		for (int i = 0; i < instructions.Count; i = checked(i + 1))
		{
			SHDRInstruction inst = instructions[i];
			if (_instructionHandlers.ContainsKey(inst.opcode))
			{
				_instructionHandlers[inst.opcode](inst);
			}
			else if (!SHDRInstruction.IsDeclaration(inst.opcode))
			{
				Instructions.Add(new USILInstruction
				{
					instructionType = USILInstructionType.Comment,
					destOperand = new USILOperand
					{
						comment = $"unsupported_{inst.opcode}",
						operandType = USILOperandType.Comment
					},
					srcOperands = new List<USILOperand>()
				});
			}
		}
	}

	private void ConvertInputs()
	{
		ISGN isgn = _dxShader.Isgn;
		ISGN.Input[] dxInputs = isgn.inputs;
		foreach (ISGN.Input dxInput in dxInputs)
		{
			string inputType = dxInput.name;
			string inputName = DXShaderNamingUtils.GetISGNInputName(dxInput);
			int register = dxInput.register;
			int mask = dxInput.mask;
			USILInputOutput usilInput = new USILInputOutput
			{
				type = inputType,
				name = inputName,
				register = register,
				mask = mask,
				isOutput = false
			};
			Inputs.Add(usilInput);
		}
	}

	private void ConvertOutputs()
	{
		OSGN osgn = _dxShader.Osgn;
		OSGN.Output[] dxOutputs = osgn.outputs;
		foreach (OSGN.Output dxOutput in dxOutputs)
		{
			string outputType = dxOutput.name;
			string outputName = DXShaderNamingUtils.GetOSGNOutputName(dxOutput);
			int register = dxOutput.register;
			int mask = dxOutput.mask;
			USILInputOutput usilOutput = new USILInputOutput
			{
				type = outputType,
				name = outputName,
				register = register,
				mask = mask,
				isOutput = true
			};
			Outputs.Add(usilOutput);
		}
	}

	public override string ToString()
	{
		StringBuilder sb = new StringBuilder();
		int depth = 4;
		foreach (USILInstruction instruction in Instructions)
		{
			USILInstructionType instructionType = instruction.instructionType;
			if (((uint)(instructionType - 50) <= 1u || instructionType == USILInstructionType.EndLoop) ? true : false)
			{
				depth = checked(depth - 1);
			}
			sb.Append(new string(' ', checked(depth * 4)));
			sb.AppendLine(instruction.ToString());
			instructionType = instruction.instructionType;
			if (((uint)(instructionType - 48) <= 2u || instructionType == USILInstructionType.Loop) ? true : false)
			{
				depth = checked(depth + 1);
			}
		}
		return sb.ToString();
	}

	private void FillUSILOperand(SHDRInstructionOperand dxOperand, USILOperand usilOperand, int[] mask, bool immIsInt)
	{
		usilOperand.mask = mask;
		usilOperand.negative = (dxOperand.extendedData & 0x40) >> 6 == 1;
		usilOperand.absoluteValue = (dxOperand.extendedData & 0x80) >> 7 == 1;
		checked
		{
			switch (dxOperand.operand)
			{
			case Operand.Null:
				usilOperand.operandType = USILOperandType.Null;
				break;
			case Operand.ConstantBuffer:
				if (dxOperand.subOperands[1] != null)
				{
					int cbSlotIdx2 = dxOperand.arraySizes[0];
					int cbOff = dxOperand.registerNumber;
					SHDRInstructionOperand cbOperand = dxOperand.subOperands[1];
					usilOperand.operandType = USILOperandType.ConstantBuffer;
					usilOperand.registerIndex = cbSlotIdx2;
					usilOperand.arrayRelative = new USILOperand();
					FillUSILOperand(cbOperand, usilOperand.arrayRelative, cbOperand.swizzle, immIsInt: false);
					usilOperand.arrayIndex = cbOff;
				}
				else
				{
					int cbSlotIdx3 = dxOperand.arraySizes[0];
					int cbArrIdx2 = dxOperand.arraySizes[1];
					usilOperand.operandType = USILOperandType.ConstantBuffer;
					usilOperand.registerIndex = cbSlotIdx3;
					usilOperand.arrayIndex = cbArrIdx2;
				}
				break;
			case Operand.IndexableTemp:
			{
				int cbSlotIdx = dxOperand.arraySizes[0];
				int cbArrIdx = dxOperand.arraySizes[1];
				usilOperand.operandType = USILOperandType.ConstantBuffer;
				usilOperand.registerIndex = cbSlotIdx;
				usilOperand.arrayIndex = cbArrIdx;
				break;
			}
			case Operand.Input:
			{
				int inRegIdx = dxOperand.arraySizes[0];
				usilOperand.operandType = USILOperandType.InputRegister;
				usilOperand.registerIndex = inRegIdx;
				break;
			}
			case Operand.Output:
			{
				int outRegIdx = dxOperand.arraySizes[0];
				usilOperand.operandType = USILOperandType.OutputRegister;
				usilOperand.registerIndex = outRegIdx;
				break;
			}
			case Operand.Temp:
			{
				int tmpRegIdx = ((dxOperand.arraySizes.Length != 0) ? dxOperand.arraySizes[0] : 0);
				usilOperand.operandType = USILOperandType.TempRegister;
				usilOperand.registerIndex = tmpRegIdx;
				break;
			}
			case Operand.Resource:
			{
				int rscRegIdx = dxOperand.arraySizes[0];
				usilOperand.operandType = USILOperandType.ResourceRegister;
				usilOperand.registerIndex = rscRegIdx;
				break;
			}
			case Operand.Sampler:
			{
				int smpRegIdx = dxOperand.arraySizes[0];
				usilOperand.operandType = USILOperandType.SamplerRegister;
				usilOperand.registerIndex = smpRegIdx;
				break;
			}
			case Operand.InputCoverageMask:
				usilOperand.operandType = USILOperandType.InputCoverageMask;
				break;
			case Operand.InputThreadGroupID:
				usilOperand.operandType = USILOperandType.InputThreadGroupID;
				break;
			case Operand.InputThreadID:
				usilOperand.operandType = USILOperandType.InputThreadID;
				break;
			case Operand.InputThreadIDInGroup:
				usilOperand.operandType = USILOperandType.InputThreadIDInGroup;
				break;
			case Operand.InputThreadIDInGroupFlattened:
				usilOperand.operandType = USILOperandType.InputThreadIDInGroupFlattened;
				break;
			case Operand.InputPrimitiveID:
				usilOperand.operandType = USILOperandType.InputPrimitiveID;
				break;
			case Operand.InputForkInstanceID:
				usilOperand.operandType = USILOperandType.InputForkInstanceID;
				break;
			case Operand.InputGSInstanceID:
				usilOperand.operandType = USILOperandType.InputGSInstanceID;
				break;
			case Operand.InputDomainPoint:
				usilOperand.operandType = USILOperandType.InputDomainPoint;
				break;
			case Operand.OutputControlPointID:
				usilOperand.operandType = USILOperandType.OutputControlPointID;
				break;
			case Operand.OutputDepth:
				usilOperand.operandType = USILOperandType.OutputDepth;
				break;
			case Operand.OutputCoverageMask:
				usilOperand.operandType = USILOperandType.OutputCoverageMask;
				break;
			case Operand.OutputDepthGreaterEqual:
				usilOperand.operandType = USILOperandType.OutputDepthGreaterEqual;
				break;
			case Operand.OutputDepthLessEqual:
				usilOperand.operandType = USILOperandType.OutputDepthLessEqual;
				break;
			case Operand.StencilRef:
				usilOperand.operandType = USILOperandType.StencilRef;
				break;
			case Operand.Immediate32:
				usilOperand.immIsInt = immIsInt;
				usilOperand.operandType = (immIsInt ? USILOperandType.ImmediateInt : USILOperandType.ImmediateFloat);
				if (dxOperand.immValues.Length == 1)
				{
					if (immIsInt)
					{
						usilOperand.immValueInt = new int[1] { ConvertFloatToInt((float)dxOperand.immValues[0]) };
					}
					else
					{
						usilOperand.immValueFloat = new float[1] { (float)dxOperand.immValues[0] };
					}
					break;
				}
				if (dxOperand.immValues.Length == 4)
				{
					if (immIsInt)
					{
						usilOperand.immValueInt = new int[mask.Length];
						for (int i = 0; i < mask.Length; i++)
						{
							usilOperand.immValueInt[i] = ConvertFloatToInt((float)dxOperand.immValues[mask[i]]);
						}
					}
					else
					{
						usilOperand.immValueFloat = new float[mask.Length];
						for (int j = 0; j < mask.Length; j++)
						{
							usilOperand.immValueFloat[j] = (float)dxOperand.immValues[mask[j]];
						}
					}
					break;
				}
				throw new Exception($"{dxOperand.immValues.Length} imm values not possible");
			case Operand.ImmediateConstantBuffer:
			{
				int icbSlotIdx = dxOperand.arraySizes[0];
				int icbOff = dxOperand.registerNumber;
				SHDRInstructionOperand icbOperand = dxOperand.subOperands[0];
				usilOperand.operandType = USILOperandType.ImmediateConstantBuffer;
				usilOperand.registerIndex = icbSlotIdx;
				usilOperand.arrayRelative = new USILOperand();
				FillUSILOperand(icbOperand, usilOperand.arrayRelative, icbOperand.swizzle, immIsInt: false);
				usilOperand.arrayIndex = icbOff;
				break;
			}
			default:
				throw new NotSupportedException($"operand type {dxOperand.operand} not supported");
			}
		}
	}

	private static int[] MapMask(int[] destMask, int[] srcMask)
	{
		if (srcMask == null || srcMask.Length == 0)
		{
			return destMask;
		}
		if (destMask == null || destMask.Length == 0 || destMask.Length == 4)
		{
			return srcMask;
		}
		if (destMask.Length == 1)
		{
			return new int[1] { srcMask[0] };
		}
		int[] mask = new int[destMask.Length];
		for (int i = 0; i < mask.Length; i = checked(i + 1))
		{
			mask[i] = srcMask[destMask[i]];
		}
		return mask;
	}

	private void HandleMov(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Move,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0 },
			saturate = inst.saturated
		};
		Instructions.Add(usilInst);
	}

	private void HandleMovc(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		SHDRInstructionOperand src1 = inst.operands[2];
		SHDRInstructionOperand src2 = inst.operands[3];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		USILOperand usilSrc1 = new USILOperand();
		USILOperand usilSrc2 = new USILOperand();
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), immIsInt: false);
		FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), immIsInt: false);
		FillUSILOperand(src2, usilSrc2, MapMask(dest.swizzle, src2.swizzle), immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.MoveConditional,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0, usilSrc1, usilSrc2 },
			saturate = inst.saturated
		};
		Instructions.Add(usilInst);
	}

	private void HandleAdd(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		SHDRInstructionOperand src1 = inst.operands[2];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		USILOperand usilSrc1 = new USILOperand();
		bool isInt = inst.opcode == Opcode.iadd;
		FillUSILOperand(dest, usilDest, dest.swizzle, isInt);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), isInt);
		FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), isInt);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Add,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0, usilSrc1 },
			saturate = inst.saturated,
			isIntVariant = isInt
		};
		Instructions.Add(usilInst);
	}

	private void HandleMul(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		SHDRInstructionOperand src1 = inst.operands[2];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		USILOperand usilSrc1 = new USILOperand();
		Opcode opcode = inst.opcode;
		bool flag = ((opcode == Opcode.imul || opcode == Opcode.umul) ? true : false);
		bool isInt = flag;
		FillUSILOperand(dest, usilDest, dest.swizzle, isInt);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), isInt);
		FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), isInt);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Multiply,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0, usilSrc1 },
			saturate = inst.saturated,
			isIntVariant = isInt,
			isIntUnsigned = (inst.opcode == Opcode.umul)
		};
		Instructions.Add(usilInst);
	}

	private void HandleIMul(SHDRInstruction inst)
	{
		SHDRInstructionOperand destHI = inst.operands[0];
		SHDRInstructionOperand destLO = inst.operands[1];
		SHDRInstructionOperand src0 = inst.operands[2];
		SHDRInstructionOperand src1 = inst.operands[3];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		USILOperand usilSrc1 = new USILOperand();
		FillUSILOperand(destLO, usilDest, destLO.swizzle, immIsInt: true);
		FillUSILOperand(src0, usilSrc0, MapMask(destLO.swizzle, src0.swizzle), immIsInt: true);
		FillUSILOperand(src1, usilSrc1, MapMask(destLO.swizzle, src1.swizzle), immIsInt: true);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Multiply,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0, usilSrc1 },
			saturate = inst.saturated,
			isIntVariant = true,
			isIntUnsigned = (inst.opcode == Opcode.umul)
		};
		Instructions.Add(usilInst);
	}

	private void HandleDiv(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		SHDRInstructionOperand src1 = inst.operands[2];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		USILOperand usilSrc1 = new USILOperand();
		bool isInt = inst.opcode == Opcode.udiv;
		FillUSILOperand(dest, usilDest, dest.swizzle, isInt);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), isInt);
		FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), isInt);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Divide,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0, usilSrc1 },
			saturate = inst.saturated,
			isIntVariant = isInt,
			isIntUnsigned = (inst.opcode == Opcode.udiv)
		};
		Instructions.Add(usilInst);
	}

	private void HandleMad(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		SHDRInstructionOperand src1 = inst.operands[2];
		SHDRInstructionOperand src2 = inst.operands[3];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		USILOperand usilSrc1 = new USILOperand();
		USILOperand usilSrc2 = new USILOperand();
		Opcode opcode = inst.opcode;
		bool flag = ((opcode == Opcode.imad || opcode == Opcode.umad) ? true : false);
		bool isInt = flag;
		FillUSILOperand(dest, usilDest, dest.swizzle, isInt);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), isInt);
		FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), isInt);
		FillUSILOperand(src2, usilSrc2, MapMask(dest.swizzle, src2.swizzle), isInt);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.MultiplyAdd,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0, usilSrc1, usilSrc2 },
			saturate = inst.saturated,
			isIntVariant = isInt,
			isIntUnsigned = (inst.opcode == Opcode.umad)
		};
		Instructions.Add(usilInst);
	}

	private void HandleAnd(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		SHDRInstructionOperand src1 = inst.operands[2];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		USILOperand usilSrc1 = new USILOperand();
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), immIsInt: false);
		FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.And,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0, usilSrc1 }
		};
		Instructions.Add(usilInst);
	}

	private void HandleOr(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		SHDRInstructionOperand src1 = inst.operands[2];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		USILOperand usilSrc1 = new USILOperand();
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), immIsInt: false);
		FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Or,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0, usilSrc1 }
		};
		Instructions.Add(usilInst);
	}

	private void HandleNot(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Not,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0 }
		};
		Instructions.Add(usilInst);
	}

	private void HandleFtoi(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), immIsInt: false);
		USILInstruction uSILInstruction = new USILInstruction
		{
			srcOperands = new List<USILOperand> { usilSrc0 }
		};
		Opcode opcode = inst.opcode;
		USILInstructionType instructionType = opcode switch
		{
			Opcode.ftoi => USILInstructionType.FloatToInt, 
			_ => USILInstructionType.FloatToUInt, 
		};
		uSILInstruction.instructionType = instructionType;
		uSILInstruction.destOperand = usilDest;
		USILInstruction usilInst = uSILInstruction;
		Instructions.Add(usilInst);
	}

	private void HandleItof(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), immIsInt: false);
		USILInstruction uSILInstruction = new USILInstruction()
		{
			srcOperands = new List<USILOperand> { usilSrc0 }
		};
		Opcode opcode = inst.opcode;
		USILInstructionType instructionType = opcode switch
		{
			Opcode.itof => USILInstructionType.IntToFloat, 
			Opcode.utof => USILInstructionType.UIntToFloat, 
			_ => USILInstructionType.UIntToFloat, 
		};
		uSILInstruction.instructionType = instructionType;
		uSILInstruction.destOperand = usilDest;
		uSILInstruction.srcOperands = new List<USILOperand> { usilSrc0 };
		USILInstruction usilInst = uSILInstruction;
		Instructions.Add(usilInst);
	}

	private void HandleMin(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		SHDRInstructionOperand src1 = inst.operands[2];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		USILOperand usilSrc1 = new USILOperand();
		Opcode opcode = inst.opcode;
		bool flag = ((opcode == Opcode.imin || opcode == Opcode.umin) ? true : false);
		bool isInt = flag;
		FillUSILOperand(dest, usilDest, dest.swizzle, isInt);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), isInt);
		FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), isInt);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Minimum,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0, usilSrc1 },
			isIntVariant = isInt,
			isIntUnsigned = (inst.opcode == Opcode.umin)
		};
		Instructions.Add(usilInst);
	}

	private void HandleMax(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		SHDRInstructionOperand src1 = inst.operands[2];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		USILOperand usilSrc1 = new USILOperand();
		Opcode opcode = inst.opcode;
		bool flag = ((opcode == Opcode.imax || opcode == Opcode.umax) ? true : false);
		bool isInt = flag;
		FillUSILOperand(dest, usilDest, dest.swizzle, isInt);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), isInt);
		FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), isInt);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Maximum,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0, usilSrc1 },
			isIntVariant = isInt,
			isIntUnsigned = (inst.opcode == Opcode.umax)
		};
		Instructions.Add(usilInst);
	}

	private void HandleSqrt(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.SquareRoot,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0 }
		};
		Instructions.Add(usilInst);
	}

	private void HandleRsq(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.SquareRootReciprocal,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0 }
		};
		Instructions.Add(usilInst);
	}

	private void HandleLog(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Logarithm2,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0 }
		};
		Instructions.Add(usilInst);
	}

	private void HandleExp(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Exponential,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0 }
		};
		Instructions.Add(usilInst);
	}

	private void HandleRcp(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Reciprocal,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0 }
		};
		Instructions.Add(usilInst);
	}

	private void HandleFrc(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Fractional,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0 }
		};
		Instructions.Add(usilInst);
	}

	private void HandleRoundNi(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Floor,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0 }
		};
		Instructions.Add(usilInst);
	}

	private void HandleRoundPi(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Ceiling,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0 }
		};
		Instructions.Add(usilInst);
	}

	private void HandleRoundNe(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Round,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0 }
		};
		Instructions.Add(usilInst);
	}

	private void HandleRoundZ(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Truncate,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0 }
		};
		Instructions.Add(usilInst);
	}

	private void HandleSincos(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest0 = inst.operands[0];
		SHDRInstructionOperand dest1 = inst.operands[1];
		SHDRInstructionOperand src = inst.operands[2];
		if (dest0.operand != Operand.Null)
		{
			USILOperand usilSinDest = new USILOperand();
			USILOperand usilSinSrc = new USILOperand();
			FillUSILOperand(dest0, usilSinDest, dest0.swizzle, immIsInt: false);
			FillUSILOperand(src, usilSinSrc, MapMask(dest0.swizzle, src.swizzle), immIsInt: false);
			USILInstruction usilSinInst = new USILInstruction
			{
				instructionType = USILInstructionType.Sine,
				destOperand = usilSinDest,
				srcOperands = new List<USILOperand> { usilSinSrc }
			};
			Instructions.Add(usilSinInst);
		}
		if (dest1.operand != Operand.Null)
		{
			USILOperand usilCosDest = new USILOperand();
			USILOperand usilCosSrc = new USILOperand();
			FillUSILOperand(dest1, usilCosDest, dest1.swizzle, immIsInt: false);
			FillUSILOperand(src, usilCosSrc, MapMask(dest1.swizzle, src.swizzle), immIsInt: false);
			USILInstruction usilCosInst = new USILInstruction
			{
				instructionType = USILInstructionType.Cosine,
				destOperand = usilCosDest,
				srcOperands = new List<USILOperand> { usilCosSrc }
			};
			Instructions.Add(usilCosInst);
		}
	}

	private void HandleIShl(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		SHDRInstructionOperand src1 = inst.operands[2];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		USILOperand usilSrc1 = new USILOperand();
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: true);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), immIsInt: true);
		FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), immIsInt: true);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.ShiftLeft,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0, usilSrc1 },
			isIntVariant = true
		};
		Instructions.Add(usilInst);
	}

	private void HandleIShr(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		SHDRInstructionOperand src1 = inst.operands[2];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		USILOperand usilSrc1 = new USILOperand();
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: true);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), immIsInt: true);
		FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), immIsInt: true);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.ShiftRight,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0, usilSrc1 },
			isIntVariant = true
		};
		Instructions.Add(usilInst);
	}

	private void HandleDp2(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		SHDRInstructionOperand src1 = inst.operands[2];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		USILOperand usilSrc1 = new USILOperand();
		int[] mask = new int[2] { 0, 1 };
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(src0, usilSrc0, mask, immIsInt: false);
		FillUSILOperand(src1, usilSrc1, mask, immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.DotProduct2,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0, usilSrc1 },
			saturate = inst.saturated
		};
		Instructions.Add(usilInst);
	}

	private void HandleDp3(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		SHDRInstructionOperand src1 = inst.operands[2];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		USILOperand usilSrc1 = new USILOperand();
		int[] mask = new int[3] { 0, 1, 2 };
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(src0, usilSrc0, mask, immIsInt: false);
		FillUSILOperand(src1, usilSrc1, mask, immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.DotProduct3,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0, usilSrc1 },
			saturate = inst.saturated
		};
		Instructions.Add(usilInst);
	}

	private void HandleDp4(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		SHDRInstructionOperand src1 = inst.operands[2];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		USILOperand usilSrc1 = new USILOperand();
		int[] mask = new int[4] { 0, 1, 2, 3 };
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(src0, usilSrc0, mask, immIsInt: false);
		FillUSILOperand(src1, usilSrc1, mask, immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.DotProduct4,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0, usilSrc1 },
			saturate = inst.saturated
		};
		Instructions.Add(usilInst);
	}

	private void HandleSample(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand srcAddress = inst.operands[1];
		SHDRInstructionOperand srcResource = inst.operands[2];
		SHDRInstructionOperand srcSampler = inst.operands[3];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrcAddress = new USILOperand();
		USILOperand usilSrcResource = new USILOperand();
		USILOperand usilSrcSampler = new USILOperand();
		USILOperand usilSamplerType = new USILOperand(0);
		int[] mask = new int[4] { 0, 1, 2, 3 };
		ResourceDimension dimension = _resourceToDimension[srcResource.arraySizes[0]];
		int[] uvMask = GetSampleLocationMask(dimension);
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(srcAddress, usilSrcAddress, MapMask(uvMask, srcAddress.swizzle), immIsInt: false);
		FillUSILOperand(srcResource, usilSrcResource, MapMask(dest.swizzle, srcResource.swizzle), immIsInt: false);
		FillUSILOperand(srcSampler, usilSrcSampler, mask, immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Sample,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrcAddress, usilSrcResource, usilSrcSampler, usilSamplerType }
		};
		Instructions.Add(usilInst);
	}

	private void HandleSampleC(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand srcAddress = inst.operands[1];
		SHDRInstructionOperand srcResource = inst.operands[2];
		SHDRInstructionOperand srcSampler = inst.operands[3];
		SHDRInstructionOperand srcReferenceValue = inst.operands[4];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrcAddress = new USILOperand();
		USILOperand usilSrcResource = new USILOperand();
		USILOperand usilSrcSampler = new USILOperand();
		USILOperand usilSrcReferenceValue = new USILOperand();
		USILOperand usilSamplerType = new USILOperand(0);
		int[] mask = new int[4] { 0, 1, 2, 3 };
		ResourceDimension dimension = _resourceToDimension[srcResource.arraySizes[0]];
		int[] uvMask = GetSampleLocationMask(dimension);
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(srcAddress, usilSrcAddress, MapMask(uvMask, srcAddress.swizzle), immIsInt: false);
		FillUSILOperand(srcResource, usilSrcResource, MapMask(dest.swizzle, srcResource.swizzle), immIsInt: false);
		FillUSILOperand(srcSampler, usilSrcSampler, mask, immIsInt: false);
		FillUSILOperand(srcReferenceValue, usilSrcReferenceValue, mask, immIsInt: false);
		USILInstruction uSILInstruction = new USILInstruction()
		{
			srcOperands = new List<USILOperand>
				{ usilSrcAddress, usilSrcResource, usilSrcSampler, usilSrcReferenceValue, usilSamplerType }
		};
		Opcode opcode = inst.opcode;
		if (1 == 0)
		{
		}
		USILInstructionType instructionType = opcode switch
		{
			Opcode.sample_c => USILInstructionType.SampleComparison, 
			Opcode.sample_c_lz => USILInstructionType.SampleComparisonLODZero, 
			_ => USILInstructionType.SampleComparisonLODZero, 
		};
		uSILInstruction.instructionType = instructionType;
		uSILInstruction.destOperand = usilDest;
		uSILInstruction.srcOperands = new List<USILOperand> { usilSrcAddress, usilSrcResource, usilSrcSampler, usilSrcReferenceValue, usilSamplerType };
		USILInstruction usilInst = uSILInstruction;
		Instructions.Add(usilInst);
	}

	private void HandleSampleL(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand srcAddress = inst.operands[1];
		SHDRInstructionOperand srcResource = inst.operands[2];
		SHDRInstructionOperand srcSampler = inst.operands[3];
		SHDRInstructionOperand srcLOD = inst.operands[4];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrcAddress = new USILOperand();
		USILOperand usilSrcResource = new USILOperand();
		USILOperand usilSrcSampler = new USILOperand();
		USILOperand usilSrcLOD = new USILOperand();
		USILOperand usilSamplerType = new USILOperand(0);
		int[] mask = new int[4] { 0, 1, 2, 3 };
		ResourceDimension dimension = _resourceToDimension[srcResource.arraySizes[0]];
		int[] uvMask = GetSampleLocationMask(dimension);
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(srcAddress, usilSrcAddress, MapMask(uvMask, srcAddress.swizzle), immIsInt: false);
		FillUSILOperand(srcResource, usilSrcResource, MapMask(dest.swizzle, srcResource.swizzle), immIsInt: false);
		FillUSILOperand(srcSampler, usilSrcSampler, mask, immIsInt: false);
		FillUSILOperand(srcLOD, usilSrcLOD, srcLOD.swizzle, immIsInt: false);
		USILInstruction uSILInstruction = new USILInstruction()
		{
			srcOperands = new List<USILOperand> { usilSrcAddress, usilSrcResource, usilSrcSampler, usilSrcLOD, usilSamplerType }
		};
		Opcode opcode = inst.opcode;
		if (1 == 0)
		{
		}
		USILInstructionType instructionType = opcode switch
		{
			Opcode.sample_b => USILInstructionType.SampleLODBias, 
			Opcode.sample_l => USILInstructionType.SampleLOD, 
			_ => USILInstructionType.SampleLOD, 
		};
		if (1 == 0)
		{
		}
		uSILInstruction.instructionType = instructionType;
		uSILInstruction.destOperand = usilDest;
		USILInstruction usilInst = uSILInstruction;
		Instructions.Add(usilInst);
	}

	private void HandleSampleD(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand srcAddress = inst.operands[1];
		SHDRInstructionOperand srcResource = inst.operands[2];
		SHDRInstructionOperand srcSampler = inst.operands[3];
		SHDRInstructionOperand srcXDerivatives = inst.operands[4];
		SHDRInstructionOperand srcYDerivatives = inst.operands[5];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrcAddress = new USILOperand();
		USILOperand usilSrcResource = new USILOperand();
		USILOperand usilSrcSampler = new USILOperand();
		USILOperand usilSrcXDerivatives = new USILOperand();
		USILOperand usilSrcYDerivatives = new USILOperand();
		int[] mask = new int[4] { 0, 1, 2, 3 };
		ResourceDimension dimension = _resourceToDimension[srcResource.arraySizes[0]];
		int[] uvMask = GetSampleLocationMask(dimension);
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(srcAddress, usilSrcAddress, MapMask(uvMask, srcAddress.swizzle), immIsInt: false);
		FillUSILOperand(srcResource, usilSrcResource, MapMask(dest.swizzle, srcResource.swizzle), immIsInt: false);
		FillUSILOperand(srcSampler, usilSrcSampler, mask, immIsInt: false);
		FillUSILOperand(srcXDerivatives, usilSrcXDerivatives, srcXDerivatives.swizzle, immIsInt: false);
		FillUSILOperand(srcYDerivatives, usilSrcYDerivatives, srcYDerivatives.swizzle, immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.SampleDerivative,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrcAddress, usilSrcResource, usilSrcSampler, usilSrcXDerivatives, usilSrcYDerivatives }
		};
		Instructions.Add(usilInst);
	}

	private void HandleLd(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand srcAddress = inst.operands[1];
		SHDRInstructionOperand srcResource = inst.operands[2];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrcAddress = new USILOperand();
		USILOperand usilSrcResource = new USILOperand();
		ResourceDimension dimension = _resourceToDimension[srcResource.arraySizes[0]];
		int[] uvMask = GetLoadLocationMask(dimension);
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(srcAddress, usilSrcAddress, MapMask(uvMask, srcAddress.swizzle), immIsInt: false);
		FillUSILOperand(srcResource, usilSrcResource, MapMask(dest.swizzle, srcResource.swizzle), immIsInt: false);
		USILInstruction uSILInstruction = new USILInstruction()
		{
			srcOperands = new List<USILOperand> { usilSrcAddress, usilSrcResource }
		};
		Opcode opcode = inst.opcode;
		USILInstructionType instructionType = opcode switch
		{
			Opcode.ld => USILInstructionType.LoadResource, 
			Opcode.ldms => USILInstructionType.LoadResourceMultisampled, 
			_ => USILInstructionType.LoadResourceMultisampled, 
		};
		uSILInstruction.instructionType = instructionType;
		uSILInstruction.destOperand = usilDest;
		uSILInstruction.srcOperands = new List<USILOperand> { usilSrcAddress, usilSrcResource };
		USILInstruction usilInst = uSILInstruction;
		Instructions.Add(usilInst);
	}

	private void HandleLdStructured(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand srcAddress = inst.operands[1];
		SHDRInstructionOperand srcByteOffset = inst.operands[2];
		SHDRInstructionOperand src0 = inst.operands[3];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrcAddress = new USILOperand();
		USILOperand usilSrcByteOffset = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		if (src0.operand == Operand.UnorderedAccessView)
		{
			throw new NotSupportedException("uav on ld_structured not supported");
		}
		if (src0.operand == Operand.ThreadGroupSharedMemory)
		{
			throw new NotSupportedException("gsm on ld_structured not supported");
		}
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(srcAddress, usilSrcAddress, srcAddress.swizzle, immIsInt: true);
		FillUSILOperand(srcByteOffset, usilSrcByteOffset, srcByteOffset.swizzle, immIsInt: true);
		FillUSILOperand(src0, usilSrc0, src0.swizzle, immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.LoadResourceStructured,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrcAddress, usilSrcByteOffset, usilSrc0 }
		};
		Instructions.Add(usilInst);
	}

	private void HandleDiscard(SHDRInstruction inst)
	{
		SHDRInstructionOperand src0 = inst.operands[0];
		USILOperand usilSrc0 = new USILOperand();
		FillUSILOperand(src0, usilSrc0, src0.swizzle, immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Discard,
			destOperand = null,
			srcOperands = new List<USILOperand> { usilSrc0 }
		};
		HandleIf(inst);
		Instructions.Add(usilInst);
		HandleEndIf(inst);
	}

	private void HandleResInfo(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		SHDRInstructionOperand src1 = inst.operands[2];
		USILOperand usilResource = new USILOperand();
		USILOperand usilMipLevel = new USILOperand();
		USILOperand usilWidth = new USILOperand();
		USILOperand usilHeight = new USILOperand();
		USILOperand usilDepthOrArraySize = new USILOperand();
		USILOperand usilMipCount = new USILOperand();
		bool hasWidth = false;
		bool hasHeight = false;
		bool hasDepth = false;
		bool hasMipCount = false;
		for (int i = 0; i < dest.swizzle.Length; i = checked(i + 1))
		{
			switch (src1.swizzle[i])
			{
			case 0:
				hasWidth = true;
				break;
			case 1:
				hasHeight = true;
				break;
			case 2:
				hasDepth = true;
				break;
			case 3:
				hasMipCount = true;
				break;
			}
		}
		FillUSILOperand(src1, usilResource, new int[4] { 0, 1, 2, 3 }, immIsInt: false);
		FillUSILOperand(src0, usilMipLevel, src0.swizzle, immIsInt: false);
		if (hasWidth)
		{
			FillUSILOperand(dest, usilWidth, new int[1], immIsInt: false);
		}
		else
		{
			usilWidth.operandType = USILOperandType.Null;
		}
		if (hasHeight)
		{
			FillUSILOperand(dest, usilHeight, new int[1] { 1 }, immIsInt: false);
		}
		else
		{
			usilHeight.operandType = USILOperandType.Null;
		}
		if (hasDepth)
		{
			FillUSILOperand(dest, usilDepthOrArraySize, new int[1] { 2 }, immIsInt: false);
		}
		else
		{
			usilDepthOrArraySize.operandType = USILOperandType.Null;
		}
		if (hasMipCount)
		{
			FillUSILOperand(dest, usilMipCount, new int[1] { 3 }, immIsInt: false);
		}
		else
		{
			usilMipCount.operandType = USILOperandType.Null;
		}
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.ResourceDimensionInfo,
			destOperand = null,
			srcOperands = new List<USILOperand> { usilResource, usilMipLevel, usilWidth, usilHeight, usilDepthOrArraySize, usilMipCount }
		};
		Instructions.Add(usilInst);
	}

	private void HandleSampleInfo(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(src0, usilSrc0, src0.swizzle, immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.SampleCountInfo,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0 }
		};
		Instructions.Add(usilInst);
	}

	private void HandleDerivRt(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		FillUSILOperand(dest, usilDest, dest.swizzle, immIsInt: false);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), immIsInt: false);
		USILInstruction uSILInstruction = new USILInstruction()
		{
			srcOperands = new List<USILOperand> { usilSrc0 }
		};
		Opcode opcode = inst.opcode;
		USILInstructionType instructionType = opcode switch
		{
			Opcode.deriv_rtx => USILInstructionType.DerivativeRenderTargetX, 
			Opcode.deriv_rty => USILInstructionType.DerivativeRenderTargetY, 
			Opcode.deriv_rtx_coarse => USILInstructionType.DerivativeRenderTargetXCoarse, 
			Opcode.deriv_rty_coarse => USILInstructionType.DerivativeRenderTargetYCoarse, 
			Opcode.deriv_rtx_fine => USILInstructionType.DerivativeRenderTargetXFine, 
			Opcode.deriv_rty_fine => USILInstructionType.DerivativeRenderTargetYFine, 
			_ => USILInstructionType.DerivativeRenderTargetX, 
		};
		uSILInstruction.instructionType = instructionType;
		uSILInstruction.destOperand = usilDest;
		uSILInstruction.srcOperands = new List<USILOperand> { usilSrc0 };
		USILInstruction usilInst = uSILInstruction;
		Instructions.Add(usilInst);
	}

	private void HandleIf(SHDRInstruction inst)
	{
		SHDRInstructionOperand src0 = inst.operands[0];
		USILOperand usilSrc0 = new USILOperand();
		FillUSILOperand(src0, usilSrc0, src0.swizzle, immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = (((inst.instData & 0x40000) != 0) ? USILInstructionType.IfTrue : USILInstructionType.IfFalse),
			destOperand = null,
			srcOperands = new List<USILOperand> { usilSrc0 }
		};
		Instructions.Add(usilInst);
	}

	private void HandleElse(SHDRInstruction inst)
	{
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Else,
			destOperand = null,
			srcOperands = new List<USILOperand>()
		};
		Instructions.Add(usilInst);
	}

	private void HandleEndIf(SHDRInstruction inst)
	{
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.EndIf,
			destOperand = null,
			srcOperands = new List<USILOperand>()
		};
		Instructions.Add(usilInst);
	}

	private void HandleLoop(SHDRInstruction inst)
	{
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Loop,
			destOperand = null,
			srcOperands = new List<USILOperand>()
		};
		Instructions.Add(usilInst);
	}

	private void HandleEndLoop(SHDRInstruction inst)
	{
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.EndLoop,
			destOperand = null,
			srcOperands = new List<USILOperand>()
		};
		Instructions.Add(usilInst);
	}

	private void HandleBreak(SHDRInstruction inst)
	{
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Break,
			destOperand = null,
			srcOperands = new List<USILOperand>()
		};
		Instructions.Add(usilInst);
	}

	private void HandleBreakc(SHDRInstruction inst)
	{
		SHDRInstructionOperand src0 = inst.operands[0];
		USILOperand usilSrc0 = new USILOperand();
		FillUSILOperand(src0, usilSrc0, src0.swizzle, immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Break,
			destOperand = null,
			srcOperands = new List<USILOperand> { usilSrc0 }
		};
		HandleIf(inst);
		Instructions.Add(usilInst);
		HandleEndIf(inst);
	}

	private void HandleContinue(SHDRInstruction inst)
	{
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Continue,
			destOperand = null,
			srcOperands = new List<USILOperand>()
		};
		Instructions.Add(usilInst);
	}

	private void HandleContinuec(SHDRInstruction inst)
	{
		SHDRInstructionOperand src0 = inst.operands[0];
		USILOperand usilSrc0 = new USILOperand();
		FillUSILOperand(src0, usilSrc0, src0.swizzle, immIsInt: false);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Continue,
			destOperand = null,
			srcOperands = new List<USILOperand> { usilSrc0 }
		};
		HandleIf(inst);
		Instructions.Add(usilInst);
		HandleEndIf(inst);
	}

	private void HandleEq(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		SHDRInstructionOperand src1 = inst.operands[2];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		USILOperand usilSrc1 = new USILOperand();
		bool isInt = inst.opcode == Opcode.ieq;
		FillUSILOperand(dest, usilDest, dest.swizzle, isInt);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), isInt);
		FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), isInt);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Equal,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0, usilSrc1 },
			isIntVariant = isInt
		};
		Instructions.Add(usilInst);
	}

	private void HandleNeq(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		SHDRInstructionOperand src1 = inst.operands[2];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		USILOperand usilSrc1 = new USILOperand();
		bool isInt = inst.opcode == Opcode.ine;
		FillUSILOperand(dest, usilDest, dest.swizzle, isInt);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), isInt);
		FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), isInt);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.NotEqual,
			destOperand = usilDest,
			srcOperands = new List<USILOperand> { usilSrc0, usilSrc1 },
			isIntVariant = isInt
		};
		Instructions.Add(usilInst);
	}

	private void HandleLt(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		SHDRInstructionOperand src1 = inst.operands[2];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		USILOperand usilSrc1 = new USILOperand();
		Opcode opcode = inst.opcode;
		bool flag = ((opcode == Opcode.ilt || opcode == Opcode.ult) ? true : false);
		bool isInt = flag;
		FillUSILOperand(dest, usilDest, dest.swizzle, isInt);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), isInt);
		FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), isInt);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.LessThan,
			destOperand = usilDest,
			isIntVariant = isInt,
			isIntUnsigned = (inst.opcode == Opcode.ult),
			srcOperands = new List<USILOperand> { usilSrc0, usilSrc1 }
		};
		Instructions.Add(usilInst);
	}

	private void HandleGe(SHDRInstruction inst)
	{
		SHDRInstructionOperand dest = inst.operands[0];
		SHDRInstructionOperand src0 = inst.operands[1];
		SHDRInstructionOperand src1 = inst.operands[2];
		USILOperand usilDest = new USILOperand();
		USILOperand usilSrc0 = new USILOperand();
		USILOperand usilSrc1 = new USILOperand();
		Opcode opcode = inst.opcode;
		bool flag = ((opcode == Opcode.ige || opcode == Opcode.uge) ? true : false);
		bool isInt = flag;
		FillUSILOperand(dest, usilDest, dest.swizzle, isInt);
		FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), isInt);
		FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), isInt);
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.GreaterThanOrEqual,
			destOperand = usilDest,
			isIntVariant = isInt,
			isIntUnsigned = (inst.opcode == Opcode.uge),
			srcOperands = new List<USILOperand> { usilSrc0, usilSrc1 }
		};
		Instructions.Add(usilInst);
	}

	private void HandleRet(SHDRInstruction inst)
	{
		USILInstruction usilInst = new USILInstruction
		{
			instructionType = USILInstructionType.Return,
			srcOperands = new List<USILOperand>()
		};
		Instructions.Add(usilInst);
	}

	private void HandleTemps(SHDRInstruction inst)
	{
		Debug.Assert(inst.declData != null, "inst.declData is not null");
		int tempCount = inst.declData.numTemps;
		for (int i = 0; i < tempCount; i = checked(i + 1))
		{
			string tmpString = USILOperand.GetTypeShortForm(USILOperandType.TempRegister) + i;
			USILLocal local = new USILLocal("float4", tmpString, USILLocalType.Vector4);
			Locals.Add(local);
		}
	}

	private void HandleResource(SHDRInstruction inst)
	{
		Debug.Assert(inst.declData != null, "inst.declData is not null");
		ResourceDimension dimension = inst.declData.resourceDimension;
		int regIndex = inst.declData.operands[0].arraySizes[0];
		_resourceToDimension[regIndex] = dimension;
	}

	private void HandleCustomData(SHDRInstruction inst)
	{
		USILLocal local = new USILLocal("const float4", "icb", USILLocalType.Vector4, isArray: true);
		Debug.Assert(inst.declData != null, "inst.declData is not null");
		float[][] customDataArray = inst.declData.customDataArray;
		foreach (float[] vector in customDataArray)
		{
			USILOperand vectorOperand = new USILOperand();
			vectorOperand.operandType = USILOperandType.ImmediateFloat;
			vectorOperand.immValueFloat = vector;
			vectorOperand.mask = USILConstants.XYZW_MASK;
			local.defaultValues.Add(vectorOperand);
		}
		Locals.Add(local);
	}

	private void AddOutputLocal()
	{
		AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Type shaderType = _dxShader.Shdr.shaderType;
		if (1 == 0)
		{
		}
		string text = shaderType switch
		{
			AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Type.Vertex => "o", 
			AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Type.Pixel => "o", 
			_ => "o", 
		};
		if (1 == 0)
		{
		}
		string outputName = text;
		AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Type shaderType2 = _dxShader.Shdr.shaderType;
		if (1 == 0)
		{
		}
		text = shaderType2 switch
		{
			AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Type.Vertex => "v2f", 
			AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Type.Pixel => "fout", 
			_ => "o", 
		};
		if (1 == 0)
		{
		}
		string outputStructName = text;
		Locals.Add(new USILLocal(outputStructName, outputName, USILLocalType.Vector4));
	}

	private static int[] GetSampleLocationMask(ResourceDimension dimension)
	{
		switch (dimension)
		{
		case ResourceDimension.texture1d:
			return GetMaskOfSize(1);
		case ResourceDimension.texture2d:
		case ResourceDimension.texture1darray:
			return GetMaskOfSize(2);
		case ResourceDimension.texture3d:
		case ResourceDimension.texturecube:
		case ResourceDimension.texture2darray:
			return GetMaskOfSize(3);
		case ResourceDimension.texturecubearray:
			return GetMaskOfSize(4);
		default:
			return GetMaskOfSize(2);
		}
	}

	private static int[] GetSampleOffsetMask(ResourceDimension dimension)
	{
		switch (dimension)
		{
		case ResourceDimension.texture1d:
		case ResourceDimension.texture1darray:
			return GetMaskOfSize(1);
		case ResourceDimension.texture2d:
		case ResourceDimension.texture2darray:
			return GetMaskOfSize(2);
		case ResourceDimension.texture3d:
			return GetMaskOfSize(3);
		case ResourceDimension.texturecube:
		case ResourceDimension.texturecubearray:
			return GetMaskOfSize(4);
		default:
			return GetMaskOfSize(2);
		}
	}

	private static int[] GetLoadLocationMask(ResourceDimension dimension)
	{
		switch (dimension)
		{
		case ResourceDimension.buffer:
			return GetMaskOfSize(1);
		case ResourceDimension.texture1d:
		case ResourceDimension.texture2dms:
			return GetMaskOfSize(2);
		case ResourceDimension.texture2d:
		case ResourceDimension.texture1darray:
		case ResourceDimension.texture2dmsarray:
			return GetMaskOfSize(3);
		case ResourceDimension.texture3d:
		case ResourceDimension.texture2darray:
			return GetMaskOfSize(4);
		default:
			return GetMaskOfSize(2);
		}
	}

	private static int[] GetLoadSampleIndexMask(ResourceDimension dimension)
	{
		if (dimension == ResourceDimension.texture2dms || dimension == ResourceDimension.texture2dmsarray)
		{
			return GetMaskOfSize(2);
		}
		return GetMaskOfSize(2);
	}

	private static int[] GetLoadOffsetMask(ResourceDimension dimension)
	{
		switch (dimension)
		{
		case ResourceDimension.texture1d:
		case ResourceDimension.texture1darray:
			return GetMaskOfSize(1);
		case ResourceDimension.texture2d:
		case ResourceDimension.texture2dms:
		case ResourceDimension.texture2darray:
		case ResourceDimension.texture2dmsarray:
			return GetMaskOfSize(2);
		case ResourceDimension.texture3d:
			return GetMaskOfSize(3);
		default:
			return GetMaskOfSize(2);
		}
	}

	public static int[] GetMaskOfSize(int size)
	{
		if (1 == 0)
		{
		}
		int[] result = size switch
		{
			0 => Array.Empty<int>(), 
			1 => new int[1], 
			2 => new int[2] { 0, 1 }, 
			3 => new int[3] { 0, 1, 2 }, 
			4 => new int[4] { 0, 1, 2, 3 }, 
			_ => throw new ArgumentOutOfRangeException("size"), 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static int ConvertFloatToInt(float f)
	{
		return BitConverter.SingleToInt32Bits(f);
	}
}
