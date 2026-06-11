#define DEBUG
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL.Fixers;

public class USILGetDimensionsFixer : IUSILOptimizer
{
	public bool Run(UShaderProgram shader, ShaderSubProgram shaderData)
	{
		bool changes = false;
		List<USILInstruction> instructions = shader.instructions;
		checked
		{
			for (int i = 0; i < instructions.Count; i++)
			{
				if (!USILOptimizerUtil.DoOpcodesMatch(instructions, i, new USILInstructionType[2]
				{
					USILInstructionType.ResourceDimensionInfo,
					USILInstructionType.SampleCountInfo
				}))
				{
					if (instructions[i].instructionType == USILInstructionType.ResourceDimensionInfo && !shader.locals.Any((USILLocal l) => l.name == "resinfo_extra"))
					{
						shader.locals.Add(new USILLocal("float", "resinfo_extra", USILLocalType.Scalar));
						changes = true;
					}
					continue;
				}
				USILInstruction resinfoInst = instructions[0];
				USILInstruction sampleinfoInst = instructions[1];
				if (resinfoInst.srcOperands[1].registerIndex == sampleinfoInst.srcOperands[0].registerIndex)
				{
					Debug.Assert(sampleinfoInst.destOperand != null, "sampleinfoInst.destOperand != null");
					resinfoInst.srcOperands[5] = sampleinfoInst.destOperand;
					instructions.RemoveAt(i + 1);
					changes = true;
				}
			}
			return changes;
		}
	}
}
