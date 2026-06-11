using System;
using System.IO;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Blocks;

public class SHDRInstructionOperand
{
	public Operand operand;

	public int indexDims;

	public int components;

	public int componentMode;

	public bool extended;

	public int extendedData;

	public int[] swizzle = Array.Empty<int>();

	public int[]? indexes;

	public double[] immValues;

	public int[] arraySizes;

	public SHDRInstructionOperand[] subOperands;

	public int registerNumber;

	public SHDRInstructionOperand(BinaryReader reader)
	{
		int operandData = reader.ReadInt32();
		operand = (Operand)((operandData & 0xFF000) >> 12);
		indexDims = (operandData & 0x300000) >> 20;
		switch (operandData & 3)
		{
		case 0:
			components = 0;
			break;
		case 1:
			components = 1;
			break;
		case 2:
			components = 4;
			break;
		}
		extended = (operandData & int.MinValue) >>> 31 == 1;
		if (extended)
		{
			extendedData = reader.ReadInt32();
		}
		checked
		{
			if (components == 4)
			{
				componentMode = (operandData & 0xC) >> 2;
				if (componentMode == 0)
				{
					int mask = (operandData & 0xF0) >> 4;
					int p = 0;
					for (int i = 0; i < 4; i++)
					{
						if (((mask >> i) & 1) == 1)
						{
							p++;
						}
					}
					swizzle = new int[p];
					p = 0;
					for (int j = 0; j < 4; j++)
					{
						if (((mask >> j) & 1) == 1)
						{
							swizzle[p] = j;
							p++;
						}
					}
				}
				else if (componentMode == 1)
				{
					swizzle = new int[4];
					for (int k = 0; k < 4; k++)
					{
						swizzle[k] = (operandData >> 4 + 2 * (k & 3)) & 3;
					}
				}
				else
				{
					if (componentMode != 2)
					{
						throw new Exception($"component mode {componentMode} invalid");
					}
					swizzle = new int[1];
					swizzle[0] = (operandData & 0x30) >> 4;
				}
			}
			immValues = new double[components];
			if (operand == Operand.Immediate32)
			{
				for (int l = 0; l < components; l++)
				{
					immValues[l] = reader.ReadSingle();
				}
			}
			else if (operand == Operand.Immediate64)
			{
				for (int m = 0; m < components; m++)
				{
					immValues[m] = reader.ReadDouble();
				}
			}
			arraySizes = new int[indexDims];
			subOperands = new SHDRInstructionOperand[indexDims];
		}
		for (int n = 0; n < indexDims; n = checked(n + 1))
		{
			switch ((OperandIndex)checked((operandData & (3 << 22 + 3 * (n & 3))) >> 22 + 3 * (n & 3)))
			{
			case OperandIndex.Immediate32:
				arraySizes[n] = reader.ReadInt32();
				break;
			case OperandIndex.IndexRelative:
				subOperands[n] = new SHDRInstructionOperand(reader);
				break;
			case OperandIndex.Immediate32Relative:
				registerNumber = reader.ReadInt32();
				subOperands[n] = new SHDRInstructionOperand(reader);
				break;
			default:
				throw new Exception("unsupported operand indexing type");
			}
		}
	}
}
