using System.Collections.Generic;
using System.IO;
using AssetRipper.Export.Modules.Shaders.Resources;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.IO.Endian;
using AssetRipper.Primitives;

namespace AssetRipper.Export.Modules.Shaders;

public static class DXShaderProgramRestorer
{
	public const uint RDEFFourCC = 1178944594u;

	public static byte[] RestoreProgramData(BinaryReader reader, UnityVersion version, ref ShaderSubProgram shaderSubProgram)
	{
		checked
		{
			using MemoryStream dest = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(dest);
			uint baseOffset = (uint)reader.BaseStream.Position;
			byte[] magicBytes = reader.ReadBytes(4);
			byte[] checksum = reader.ReadBytes(16);
			uint unknown0 = reader.ReadUInt32();
			uint totalSize = reader.ReadUInt32();
			uint chunkCount = reader.ReadUInt32();
			List<uint> chunkOffsets = new List<uint>();
			for (int i = 0; i < chunkCount; i++)
			{
				chunkOffsets.Add(reader.ReadUInt32());
			}
			uint bodyOffset = (uint)reader.BaseStream.Position;
			foreach (uint chunkOffset in chunkOffsets)
			{
				reader.BaseStream.Position = chunkOffset + baseOffset;
				uint fourCc = reader.ReadUInt32();
				if (fourCc == 1178944594)
				{
					reader.BaseStream.Position = baseOffset;
					return reader.ReadBytes((int)reader.BaseStream.Length);
				}
			}
			reader.BaseStream.Position = bodyOffset;
			byte[] resourceChunkData = GetResourceChunk(version, ref shaderSubProgram);
			totalSize += (uint)resourceChunkData.Length;
			for (int j = 0; j < chunkCount; j++)
			{
				chunkOffsets[j] += (uint)resourceChunkData.Length + 4;
			}
			chunkOffsets.Insert(0, bodyOffset - baseOffset + 4);
			chunkCount++;
			totalSize += (uint)resourceChunkData.Length;
			writer.Write(magicBytes);
			writer.Write(checksum);
			writer.Write(unknown0);
			writer.Write(totalSize);
			writer.Write(chunkCount);
			foreach (uint chunkOffset2 in chunkOffsets)
			{
				writer.Write(chunkOffset2);
			}
			writer.Write(resourceChunkData);
			byte[] rest = reader.ReadBytes((int)reader.BaseStream.Length - (int)reader.BaseStream.Position);
			writer.Write(rest);
			return dest.ToArray();
		}
	}

	private static byte[] GetResourceChunk(UnityVersion version, ref ShaderSubProgram shaderSubprogram)
	{
		using MemoryStream memoryStream = new MemoryStream();
		using EndianWriter writer = new EndianWriter(memoryStream, EndianType.LittleEndian);
		ResourceChunk resourceChunk = new ResourceChunk(version, ref shaderSubprogram);
		resourceChunk.Write(writer);
		return memoryStream.ToArray();
	}
}
