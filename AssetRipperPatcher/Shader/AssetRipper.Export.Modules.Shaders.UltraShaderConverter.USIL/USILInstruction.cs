using System.Collections.Generic;
using System.Text;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL;

public class USILInstruction
{
	public USILInstructionType instructionType { get; set; }

	public USILOperand? destOperand { get; set; }

	public required List<USILOperand> srcOperands { get; set; }

	public bool saturate { get; set; }

	public bool commented { get; set; }

	public bool isIntVariant { get; set; }

	public bool isIntUnsigned { get; set; }

	public override string ToString()
	{
		StringBuilder sb = new StringBuilder();
		sb.Append(instructionType.ToString());
		sb.Append(' ');
		if (saturate)
		{
			sb.Append("saturate(");
		}
		if (destOperand != null)
		{
			sb.Append(destOperand.ToString());
			if (srcOperands.Count > 0)
			{
				sb.Append(", ");
			}
		}
		sb.Append(string.Join(", ", srcOperands));
		if (saturate)
		{
			sb.Append(')');
		}
		return sb.ToString();
	}

	public bool IsComparisonType()
	{
		USILInstructionType uSILInstructionType = instructionType;
		USILInstructionType uSILInstructionType2 = uSILInstructionType;
		if ((uint)(uSILInstructionType2 - 42) <= 5u)
		{
			return true;
		}
		return false;
	}

	public bool IsSampleType()
	{
		USILInstructionType uSILInstructionType = instructionType;
		USILInstructionType uSILInstructionType2 = uSILInstructionType;
		if ((uint)(uSILInstructionType2 - 59) <= 5u)
		{
			return true;
		}
		return false;
	}
}
