using System.IO;
using System.Linq;
using System.Text;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Blocks;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler;

public class DirectXCompiledShader
{
	public byte[] hash;

	public int version;

	public int size;

	public int blockCount;

	public int[] blockOffsets;

	public byte[][] blockDatas;

	public ShaderBlock[] blocks;

	public ISGN Isgn => (ISGN)blocks.First((ShaderBlock b) => b is ISGN);

	public OSGN Osgn => (OSGN)blocks.First((ShaderBlock b) => b is OSGN);

	public SHDR Shdr => (SHDR)blocks.First((ShaderBlock b) => b is SHDR);

	public DirectXCompiledShader(Stream stream)
	{
		checked
		{
			using BinaryReader reader = new BinaryReader(stream);
			reader.BaseStream.Position += 4L;
			hash = reader.ReadBytes(16);
			version = reader.ReadInt32();
			this.size = reader.ReadInt32();
			blockCount = reader.ReadInt32();
			blockOffsets = new int[blockCount];
			for (int i = 0; i < blockCount; i++)
			{
				blockOffsets[i] = reader.ReadInt32();
			}
			blockDatas = new byte[blockCount][];
			blocks = new ShaderBlock[blockCount];
			for (int j = 0; j < blockCount; j++)
			{
				reader.BaseStream.Position = blockOffsets[j];
				string fourCc = Encoding.ASCII.GetString(reader.ReadBytes(4));
				int size = reader.ReadInt32();
				byte[] blockData = reader.ReadBytes(size);
				blockDatas[j] = blockData;
				using MemoryStream ms = new MemoryStream(blockData);
				switch (fourCc)
				{
				case "ISGN":
					blocks[j] = new ISGN(ms);
					break;
				case "OSGN":
					blocks[j] = new OSGN(ms);
					break;
				case "SHDR":
				case "SHEX":
					blocks[j] = new SHDR(ms, blocks);
					break;
				}
			}
		}
	}
}
