using System.Collections.Generic;
using System.IO;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Blocks;

public sealed class SHDR : ShaderBlock
{
	public int majorVersion;

	public int minorVersion;

	public Type shaderType;

	public int instructionSize;

	public List<SHDRInstruction> shaderInstructions;

	private readonly ShaderBlock[] blocks;

	public override string FourCC => "SHDR";

	public SHDR(Stream stream, ShaderBlock[] blocks)
	{
		this.blocks = blocks;
		using BinaryReader reader = new BinaryReader(stream);
		short version = reader.ReadInt16();
		majorVersion = (version & 0xF0) >> 4;
		minorVersion = version & 0xF;
		shaderType = (Type)reader.ReadInt16();
		instructionSize = reader.ReadInt32();
		shaderInstructions = new List<SHDRInstruction>();
		while (reader.BaseStream.Position < checked(instructionSize * 4))
		{
			SHDRInstruction inst = new SHDRInstruction(reader, this);
			shaderInstructions.Add(inst);
		}
	}
}
