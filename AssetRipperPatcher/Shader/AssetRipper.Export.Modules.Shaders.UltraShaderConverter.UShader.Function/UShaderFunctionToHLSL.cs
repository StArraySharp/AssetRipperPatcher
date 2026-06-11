using System.Collections.Generic;
using System.Text;
using AssetRipper.Export.Modules.Shaders.Extensions;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL;
using AssetRipper.SourceGenerated.Extensions;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;

public class UShaderFunctionToHLSL
{
	private delegate void InstHandler(USILInstruction inst);

	private readonly UShaderProgram _shader;

	private readonly StringBuilder _stringBuilder = new StringBuilder();

	private string _baseIndent = "";

	private const string _indent = "    ";

	private int _indentLevel;

	private readonly Dictionary<USILInstructionType, InstHandler> _instructionHandlers;

	public UShaderFunctionToHLSL(UShaderProgram shader)
	{
		_shader = shader;
		_instructionHandlers = new Dictionary<USILInstructionType, InstHandler>
		{
			{
				USILInstructionType.Move,
				HandleMove
			},
			{
				USILInstructionType.MoveConditional,
				HandleMoveConditional
			},
			{
				USILInstructionType.Add,
				HandleAdd
			},
			{
				USILInstructionType.Subtract,
				HandleSubtract
			},
			{
				USILInstructionType.Multiply,
				HandleMultiply
			},
			{
				USILInstructionType.Divide,
				HandleDivide
			},
			{
				USILInstructionType.MultiplyAdd,
				HandleMultiplyAdd
			},
			{
				USILInstructionType.And,
				HandleAnd
			},
			{
				USILInstructionType.Or,
				HandleOr
			},
			{
				USILInstructionType.Not,
				HandleNot
			},
			{
				USILInstructionType.Minimum,
				HandleMinimum
			},
			{
				USILInstructionType.Maximum,
				HandleMaximum
			},
			{
				USILInstructionType.SquareRoot,
				HandleSquareRoot
			},
			{
				USILInstructionType.SquareRootReciprocal,
				HandleSquareRootReciprocal
			},
			{
				USILInstructionType.Logarithm2,
				HandleLogarithm2
			},
			{
				USILInstructionType.Exponential,
				HandleExponential
			},
			{
				USILInstructionType.Reciprocal,
				HandleReciprocal
			},
			{
				USILInstructionType.Fractional,
				HandleFractional
			},
			{
				USILInstructionType.Floor,
				HandleFloor
			},
			{
				USILInstructionType.Ceiling,
				HandleCeiling
			},
			{
				USILInstructionType.Round,
				HandleRound
			},
			{
				USILInstructionType.Truncate,
				HandleTruncate
			},
			{
				USILInstructionType.IntToFloat,
				HandleIntToFloat
			},
			{
				USILInstructionType.FloatToInt,
				HandleFloatToInt
			},
			{
				USILInstructionType.Sine,
				HandleSine
			},
			{
				USILInstructionType.Cosine,
				HandleCosine
			},
			{
				USILInstructionType.ShiftLeft,
				HandleShiftLeft
			},
			{
				USILInstructionType.ShiftRight,
				HandleShiftRight
			},
			{
				USILInstructionType.DotProduct2,
				HandleDotProduct
			},
			{
				USILInstructionType.DotProduct3,
				HandleDotProduct
			},
			{
				USILInstructionType.DotProduct4,
				HandleDotProduct
			},
			{
				USILInstructionType.Sample,
				HandleSample
			},
			{
				USILInstructionType.SampleComparison,
				HandleSample
			},
			{
				USILInstructionType.SampleComparisonLODZero,
				HandleSample
			},
			{
				USILInstructionType.SampleLOD,
				HandleSampleLOD
			},
			{
				USILInstructionType.SampleDerivative,
				HandleSampleDerivative
			},
			{
				USILInstructionType.LoadResource,
				HandleLoadResource
			},
			{
				USILInstructionType.LoadResourceMultisampled,
				HandleLoadResource
			},
			{
				USILInstructionType.LoadResourceStructured,
				HandleLoadResourceStructured
			},
			{
				USILInstructionType.Discard,
				HandleDiscard
			},
			{
				USILInstructionType.ResourceDimensionInfo,
				HandleResourceDimensionInfo
			},
			{
				USILInstructionType.SampleCountInfo,
				HandleSampleCountInfo
			},
			{
				USILInstructionType.GetDimensions,
				HandleResourceDimensionInfo
			},
			{
				USILInstructionType.DerivativeRenderTargetX,
				HandleDerivativeRenderTarget
			},
			{
				USILInstructionType.DerivativeRenderTargetY,
				HandleDerivativeRenderTarget
			},
			{
				USILInstructionType.DerivativeRenderTargetXCoarse,
				HandleDerivativeRenderTarget
			},
			{
				USILInstructionType.DerivativeRenderTargetYCoarse,
				HandleDerivativeRenderTarget
			},
			{
				USILInstructionType.DerivativeRenderTargetXFine,
				HandleDerivativeRenderTarget
			},
			{
				USILInstructionType.DerivativeRenderTargetYFine,
				HandleDerivativeRenderTarget
			},
			{
				USILInstructionType.IfFalse,
				HandleIf
			},
			{
				USILInstructionType.IfTrue,
				HandleIf
			},
			{
				USILInstructionType.Else,
				HandleElse
			},
			{
				USILInstructionType.EndIf,
				HandleEndIf
			},
			{
				USILInstructionType.Loop,
				HandleLoop
			},
			{
				USILInstructionType.EndLoop,
				HandleEndLoop
			},
			{
				USILInstructionType.Break,
				HandleBreak
			},
			{
				USILInstructionType.Continue,
				HandleContinue
			},
			{
				USILInstructionType.ForLoop,
				HandleForLoop
			},
			{
				USILInstructionType.Equal,
				HandleEqual
			},
			{
				USILInstructionType.NotEqual,
				HandleNotEqual
			},
			{
				USILInstructionType.LessThan,
				HandleLessThan
			},
			{
				USILInstructionType.LessThanOrEqual,
				HandleLessThanOrEqual
			},
			{
				USILInstructionType.GreaterThan,
				HandleGreaterThan
			},
			{
				USILInstructionType.GreaterThanOrEqual,
				HandleGreaterThanOrEqual
			},
			{
				USILInstructionType.Return,
				HandleReturn
			},
			{
				USILInstructionType.MultiplyMatrixByVector,
				MultiplyMatrixByVector
			},
			{
				USILInstructionType.Comment,
				HandleComment
			}
		};
	}

	public string Convert(int indentDepth)
	{
		_baseIndent = new string(' ', checked(indentDepth * "    ".Length));
		_stringBuilder.Clear();
		WriteLocals();
		foreach (USILInstruction inst in _shader.instructions)
		{
			if (_instructionHandlers.ContainsKey(inst.instructionType))
			{
				_instructionHandlers[inst.instructionType](inst);
			}
		}
		return _stringBuilder.ToString();
	}

	private void WriteLocals()
	{
		checked
		{
			foreach (USILLocal local in _shader.locals)
			{
				if (local.defaultValues.Count > 0 && local.isArray)
				{
					AppendLine($"{local.type} {local.name}[{local.defaultValues.Count}] = {{");
					if (local.defaultValues.Count > 0)
					{
						_indentLevel++;
						for (int i = 0; i < local.defaultValues.Count; i++)
						{
							USILOperand operand = local.defaultValues[i];
							string comma = ((i != local.defaultValues.Count - 1) ? "," : "");
							AppendLine($"{operand}{comma}");
						}
						_indentLevel--;
					}
					AppendLine("};");
				}
				else
				{
					AppendLine(local.type + " " + local.name + ";");
				}
			}
		}
	}

	private void HandleMove(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = WrapSaturate(inst, $"{srcOps[0]}");
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleMoveConditional(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = WrapSaturate(inst, $"{srcOps[0]} ? {srcOps[1]} : {srcOps[2]}");
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleAdd(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = WrapSaturate(inst, $"{srcOps[0]} + {srcOps[1]}");
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleSubtract(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = WrapSaturate(inst, $"{srcOps[0]} - {srcOps[1]}");
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleMultiply(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = WrapSaturate(inst, $"{srcOps[0]} * {srcOps[1]}");
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleDivide(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = WrapSaturate(inst, $"{srcOps[0]} / {srcOps[1]}");
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleMultiplyAdd(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = WrapSaturate(inst, $"{srcOps[0]} * {srcOps[1]} + {srcOps[2]}");
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleAnd(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		int op0UintSize = srcOps[0].GetValueCount();
		int op1UintSize = srcOps[1].GetValueCount();
		string value = $"uint{op0UintSize}({srcOps[0]}) & uint{op1UintSize}({srcOps[1]})";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleOr(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		int op0UintSize = srcOps[0].GetValueCount();
		int op1UintSize = srcOps[1].GetValueCount();
		string value = $"uint{op0UintSize}({srcOps[0]}) | uint{op1UintSize}({srcOps[1]})";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleNot(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		int op0UintSize = srcOps[0].GetValueCount();
		string value = $"~uint{op0UintSize}({srcOps[0]})";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleMinimum(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"min({srcOps[0]}, {srcOps[1]})";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleMaximum(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"max({srcOps[0]}, {srcOps[1]})";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleSquareRoot(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"sqrt({srcOps[0]})";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleSquareRootReciprocal(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"rsqrt({srcOps[0]})";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleLogarithm2(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"log({srcOps[0]})";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleExponential(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"exp({srcOps[0]})";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleReciprocal(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"rcp({srcOps[0]})";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleFractional(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"frac({srcOps[0]})";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleFloor(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"floor({srcOps[0]})";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleCeiling(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"ceil({srcOps[0]})";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleRound(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"round({srcOps[0]})";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleTruncate(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"trunc({srcOps[0]})";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleIntToFloat(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"floor({srcOps[0]})";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleFloatToInt(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"asint({srcOps[0]})";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleSine(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"sin({srcOps[0]})";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleCosine(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"cos({srcOps[0]})";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleShiftLeft(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		USILOperand srcOp0 = srcOps[0];
		USILOperand srcOp1 = srcOps[1];
		int op0IntSize = srcOp0.GetValueCount();
		int op1IntSize = srcOp1.GetValueCount();
		string op0Text = ((srcOp0.operandType != USILOperandType.ImmediateInt) ? $"int{op0IntSize}({srcOp0})" : $"{srcOp0}");
		string op1Text = ((srcOp1.operandType != USILOperandType.ImmediateInt) ? $"int{op1IntSize}({srcOp1})" : $"{srcOp1}");
		string value = $"float{op0IntSize}({op0Text} << {op1Text})";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleShiftRight(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		USILOperand srcOp0 = srcOps[0];
		USILOperand srcOp1 = srcOps[1];
		int op0IntSize = srcOp0.GetValueCount();
		int op1IntSize = srcOp1.GetValueCount();
		string op0Text = ((srcOp0.operandType != USILOperandType.ImmediateInt) ? $"int{op0IntSize}({srcOp0})" : $"{srcOp0}");
		string op1Text = ((srcOp1.operandType != USILOperandType.ImmediateInt) ? $"int{op1IntSize}({srcOp1})" : $"{srcOp1}");
		string value = $"float{op0IntSize}({op0Text} >> {op1Text})";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleDotProduct(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = WrapSaturate(inst, $"dot({srcOps[0]}, {srcOps[1]})");
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleSample(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		USILOperand textureOperand = srcOps[2];
		int samplerTypeIdx = ((inst.instructionType == USILInstructionType.Sample) ? 3 : 4);
		bool samplerType = srcOps[samplerTypeIdx].immValueInt[0] == 1;
		string args = $"{srcOps[2]}, {srcOps[0]}";
		string value;
		if (!samplerType)
		{
			USILOperandType operandType = textureOperand.operandType;
			if (1 == 0)
			{
			}
			string text = operandType switch
			{
				USILOperandType.Sampler2D => "tex2D(" + args + ")", 
				USILOperandType.Sampler3D => "tex3D(" + args + ")", 
				USILOperandType.SamplerCube => "texCUBE(" + args + ")", 
				USILOperandType.Sampler2DArray => "UNITY_SAMPLE_TEX2DARRAY(" + args + ")", 
				USILOperandType.SamplerCubeArray => "UNITY_SAMPLE_TEXCUBEARRAY(" + args + ")", 
				_ => "texND(" + args + ")", 
			};
			if (1 == 0)
			{
			}
			value = text;
		}
		else
		{
			args = $"{srcOps[2]}, {args}";
			USILOperandType operandType2 = textureOperand.operandType;
			if (1 == 0)
			{
			}
			string text = operandType2 switch
			{
				USILOperandType.Sampler2D => "UNITY_SAMPLE_TEX2D_SAMPLER(" + args + ")", 
				USILOperandType.Sampler3D => "UNITY_SAMPLE_TEX3D_SAMPLER(" + args + ")", 
				USILOperandType.SamplerCube => "UNITY_SAMPLE_TEXCUBE_SAMPLER(" + args + ")", 
				USILOperandType.Sampler2DArray => "UNITY_SAMPLE_TEX2DARRAY_SAMPLER(" + args + ")", 
				USILOperandType.SamplerCubeArray => "UNITY_SAMPLE_TEXCUBEARRAY_SAMPLER(" + args + ")", 
				_ => "texND(" + args + ")", 
			};
			if (1 == 0)
			{
			}
			value = text;
		}
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleSampleLOD(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		USILOperand textureOperand = srcOps[2];
		bool samplerType = srcOps[4].immValueInt[0] == 1;
		string args = ((srcOps[0].mask.Length != 2) ? $"{srcOps[2]}, float4({srcOps[0]}, {srcOps[3]})" : $"{srcOps[2]}, float4({srcOps[0]}, 0, {srcOps[3]})");
		string value;
		if (!samplerType)
		{
			USILOperandType operandType = textureOperand.operandType;
			if (1 == 0)
			{
			}
			string text = operandType switch
			{
				USILOperandType.Sampler2D => "tex2Dlod(" + args + ")", 
				USILOperandType.Sampler3D => "tex3Dlod(" + args + ")", 
				USILOperandType.SamplerCube => "texCUBElod(" + args + ")", 
				USILOperandType.Sampler2DArray => "UNITY_SAMPLE_TEX2DARRAY_LOD(" + args + ")", 
				USILOperandType.SamplerCubeArray => "UNITY_SAMPLE_TEXCUBEARRAY_LOD(" + args + ")", 
				_ => "texNDlod(" + args + ")", 
			};
			if (1 == 0)
			{
			}
			value = text;
		}
		else
		{
			args = $"{srcOps[2]}, {args}";
			USILOperandType operandType2 = textureOperand.operandType;
			if (1 == 0)
			{
			}
			string text = operandType2 switch
			{
				USILOperandType.Sampler2D => "UNITY_SAMPLE_TEX2D_SAMPLER(" + args + ")", 
				USILOperandType.Sampler3D => "UNITY_SAMPLE_TEX3D_SAMPLER(" + args + ")", 
				USILOperandType.SamplerCube => "UNITY_SAMPLE_TEXCUBE_SAMPLER(" + args + ")", 
				USILOperandType.Sampler2DArray => "UNITY_SAMPLE_TEX2DARRAY_SAMPLER(" + args + ")", 
				USILOperandType.SamplerCubeArray => "UNITY_SAMPLE_TEXCUBEARRAY_SAMPLER(" + args + ")", 
				_ => "texND(" + args + ")", 
			};
			if (1 == 0)
			{
			}
			value = text;
		}
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleSampleDerivative(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		USILOperand textureOperand = srcOps[2];
		string args = $"{srcOps[2]}, {srcOps[0]}, {srcOps[3]}, {srcOps[4]}";
		USILOperandType operandType = textureOperand.operandType;
		if (1 == 0)
		{
		}
		string text = operandType switch
		{
			USILOperandType.Sampler2D => "tex2Dgrad(" + args + ")", 
			USILOperandType.Sampler3D => "tex3Dgrad(" + args + ")", 
			USILOperandType.SamplerCube => "texCUBEgrad(" + args + ")", 
			_ => "texNDgrad(" + args + ")", 
		};
		if (1 == 0)
		{
		}
		string value = text;
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleLoadResource(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string args = $"{srcOps[1]}, {srcOps[0]}";
		string value = "Load(" + args + ")";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleLoadResourceStructured(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"((float4[1]){srcOps[2]}.Load({srcOps[0]}))[{srcOps[1].immValueInt[0] / 16}]";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleDiscard(USILInstruction inst)
	{
		string comment = CommentString(inst);
		AppendLine(comment + "discard;");
	}

	private void HandleResourceDimensionInfo(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		USILOperand usilResource = srcOps[0];
		USILOperand usilMipLevel = srcOps[1];
		USILOperand usilWidth = srcOps[2];
		USILOperand usilHeight = srcOps[3];
		USILOperand usilDepthOrArraySize = srcOps[4];
		USILOperand usilMipCount = srcOps[5];
		List<string> args = new List<string>();
		if (usilMipLevel.immValueFloat[0] == 0f && usilMipCount.operandType == USILOperandType.Null)
		{
			args.Add(usilWidth.ToString());
			if (usilHeight.operandType != USILOperandType.Null)
			{
				args.Add(usilHeight.ToString());
			}
			if (usilDepthOrArraySize.operandType != USILOperandType.Null)
			{
				args.Add(usilDepthOrArraySize.ToString());
			}
		}
		else
		{
			args.Add(usilMipLevel.ToString());
			args.Add(usilWidth.ToString());
			if (usilHeight.operandType != USILOperandType.Null)
			{
				args.Add(usilHeight.ToString());
			}
			if (usilDepthOrArraySize.operandType != USILOperandType.Null)
			{
				args.Add(usilDepthOrArraySize.ToString());
			}
			if (usilMipCount.operandType != USILOperandType.Null)
			{
				args.Add(usilMipCount.ToString());
			}
			else
			{
				args.Add("resinfo_extra");
			}
		}
		string call = "GetDimensions(" + string.Join(", ", args) + ")";
		string comment = CommentString(inst);
		AppendLine($"{comment}{usilResource}.{call};");
	}

	private void HandleSampleCountInfo(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"{srcOps[0]} = GetRenderTargetSampleCount()";
		string comment = CommentString(inst);
		AppendLine(comment + value + ";");
	}

	private void HandleDerivativeRenderTarget(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		USILInstructionType instructionType = inst.instructionType;
		if (1 == 0)
		{
		}
		string text = instructionType switch
		{
			USILInstructionType.DerivativeRenderTargetX => "ddx", 
			USILInstructionType.DerivativeRenderTargetY => "ddy", 
			USILInstructionType.DerivativeRenderTargetXCoarse => "ddx_coarse", 
			USILInstructionType.DerivativeRenderTargetYCoarse => "ddy_coarse", 
			USILInstructionType.DerivativeRenderTargetXFine => "ddx_fine", 
			USILInstructionType.DerivativeRenderTargetYFine => "ddy_fine", 
			_ => "dd?", 
		};
		if (1 == 0)
		{
		}
		string fun = text;
		string value = $"{fun}({srcOps[0]})";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleIf(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string comment = CommentString(inst);
		if (inst.instructionType == USILInstructionType.IfTrue)
		{
			AppendLine($"{comment}if ({srcOps[0]}) {{");
		}
		else
		{
			AppendLine($"{comment}if (!({srcOps[0]})) {{");
		}
		checked
		{
			_indentLevel++;
		}
	}

	private void HandleElse(USILInstruction inst)
	{
		checked
		{
			_indentLevel--;
			string comment = CommentString(inst);
			AppendLine(comment + "} else {");
			_indentLevel++;
		}
	}

	private void HandleEndIf(USILInstruction inst)
	{
		checked
		{
			_indentLevel--;
			string comment = CommentString(inst);
			AppendLine(comment + "}");
		}
	}

	private void HandleLoop(USILInstruction inst)
	{
		string comment = CommentString(inst);
		AppendLine(comment + "while (true) {");
		checked
		{
			_indentLevel++;
		}
	}

	private void HandleEndLoop(USILInstruction inst)
	{
		checked
		{
			_indentLevel--;
			string comment = CommentString(inst);
			AppendLine(comment + "}");
		}
	}

	private void HandleBreak(USILInstruction inst)
	{
		string comment = CommentString(inst);
		AppendLine(comment + "break;");
	}

	private void HandleContinue(USILInstruction inst)
	{
		string comment = CommentString(inst);
		AppendLine(comment + "continue;");
	}

	private void HandleForLoop(USILInstruction inst)
	{
		string comment = CommentString(inst);
		USILOperand iterRegOp = inst.srcOperands[0];
		USILOperand compOp = inst.srcOperands[1];
		USILInstructionType compType = (USILInstructionType)inst.srcOperands[2].immValueInt[0];
		USILNumberType numberType = (USILNumberType)inst.srcOperands[3].immValueInt[0];
		float addCount = inst.srcOperands[4].immValueFloat[0];
		int depth = inst.srcOperands[5].immValueInt[0];
		if (1 == 0)
		{
		}
		string text = numberType switch
		{
			USILNumberType.Float => "float", 
			USILNumberType.Int => "int", 
			USILNumberType.UnsignedInt => "unsigned int", 
			_ => "?", 
		};
		if (1 == 0)
		{
		}
		string numberTypeName = text;
		string iterName = USILConstants.ITER_CHARS[depth].ToString();
		if (1 == 0)
		{
		}
		text = compType switch
		{
			USILInstructionType.Equal => "==", 
			USILInstructionType.NotEqual => "!=", 
			USILInstructionType.GreaterThan => ">", 
			USILInstructionType.GreaterThanOrEqual => ">=", 
			USILInstructionType.LessThan => "<", 
			USILInstructionType.LessThanOrEqual => "<=", 
			_ => "?", 
		};
		if (1 == 0)
		{
		}
		string compText = text;
		AppendLine($"{comment}for ({numberTypeName} {iterName} = {iterRegOp}; {iterName} {compText} {compOp}; {iterName} += {addCount.ToStringInvariant()}) {{");
		checked
		{
			_indentLevel++;
		}
	}

	private void HandleEqual(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"{srcOps[0]} == {srcOps[1]}";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleNotEqual(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"{srcOps[0]} != {srcOps[1]}";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleLessThan(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"{srcOps[0]} < {srcOps[1]}";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleLessThanOrEqual(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"{srcOps[0]} <= {srcOps[1]}";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleGreaterThan(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"{srcOps[0]} > {srcOps[1]}";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleGreaterThanOrEqual(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"{srcOps[0]} >= {srcOps[1]}";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleReturn(USILInstruction inst)
	{
		UShaderFunctionType shaderFunctionType = _shader.shaderFunctionType;
		if (1 == 0)
		{
		}
		string text = shaderFunctionType switch
		{
			UShaderFunctionType.Vertex => "o", 
			UShaderFunctionType.Fragment => "o", 
			_ => "o", 
		};
		if (1 == 0)
		{
		}
		string outputName = text;
		string value = "return " + outputName;
		string comment = CommentString(inst);
		AppendLine(comment + value + ";");
	}

	private void MultiplyMatrixByVector(USILInstruction inst)
	{
		List<USILOperand> srcOps = inst.srcOperands;
		string value = $"mul({srcOps[0]}, {srcOps[1]})";
		string comment = CommentString(inst);
		AppendLine($"{comment}{inst.destOperand} = {value};");
	}

	private void HandleComment(USILInstruction inst)
	{
		AppendLine("//" + inst.destOperand?.comment + ";");
	}

	private static string WrapSaturate(USILInstruction inst, string str)
	{
		if (inst.saturate)
		{
			str = "saturate(" + str + ")";
		}
		return str;
	}

	private void AppendLine(string line)
	{
		_stringBuilder.Append(_baseIndent);
		for (int i = 0; i < _indentLevel; i = checked(i + 1))
		{
			_stringBuilder.Append("    ");
		}
		_stringBuilder.AppendLine(line);
	}

	private static string CommentString(USILInstruction inst)
	{
		return inst.commented ? "//" : "";
	}
}
