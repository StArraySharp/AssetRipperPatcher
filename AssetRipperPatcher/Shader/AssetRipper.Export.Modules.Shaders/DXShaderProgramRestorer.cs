using System.Collections.Generic;
using System.IO;
using AssetRipper.Export.Modules.Shaders.Resources;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.IO.Endian;
using AssetRipper.Primitives;
using ShaderRipper.Data;

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
			const uint headerSize = 32;
			uint magic = reader.ReadUInt32();
			if (magic != 0x43425844) // DXBC
			{
				throw new InvalidDataException("Invalid DXBC signature.");
			}
			reader.BaseStream.Position += 16; // The checksum is recomputed after writing.
			uint unknown0 = reader.ReadUInt32();
			uint totalSize = reader.ReadUInt32();
			uint chunkCount = reader.ReadUInt32();
			if (totalSize < headerSize || totalSize > reader.BaseStream.Length - baseOffset
				|| chunkCount > (totalSize - headerSize) / 4)
			{
				throw new InvalidDataException("Invalid DXBC size or chunk count.");
			}
			uint bodyStart = headerSize + 4 * chunkCount;
			List<uint> chunkOffsets = new List<uint>();
			for (int i = 0; i < chunkCount; i++)
			{
				chunkOffsets.Add(reader.ReadUInt32());
			}
			bool hasRdef = false;
			foreach (uint chunkOffset in chunkOffsets)
			{
				if (chunkOffset < bodyStart || chunkOffset > totalSize - 8)
				{
					throw new InvalidDataException("Invalid DXBC chunk offset.");
				}
				reader.BaseStream.Position = (long)baseOffset + chunkOffset;
				uint fourCc = reader.ReadUInt32();
				uint chunkSize = reader.ReadUInt32();
				if (chunkSize > totalSize - chunkOffset - 8)
				{
					throw new InvalidDataException("Invalid DXBC chunk size.");
				}
				hasRdef |= fourCc == RDEFFourCC;
			}
			if (hasRdef)
			{
				reader.BaseStream.Position = baseOffset;
				byte[] original = new byte[totalSize];
				reader.BaseStream.ReadExactly(original);
				return original;
			}

			reader.BaseStream.Position = (long)baseOffset + bodyStart;
			byte[] rest = new byte[totalSize - bodyStart];
			reader.BaseStream.ReadExactly(rest);
			byte[] resourceChunkData = GetResourceChunk(version, ref shaderSubProgram);
			// The container grows by the new chunk plus one 4-byte entry in the chunk
			// offset table.  Counting the chunk twice here (the old code did) leaves
			// every reader a totalSize that overshoots the real file by almost the
			// whole RDEF chunk.
			totalSize += (uint)resourceChunkData.Length + 4;
			for (int j = 0; j < chunkCount; j++)
			{
				chunkOffsets[j] += (uint)resourceChunkData.Length + 4;
			}
			chunkOffsets.Insert(0, bodyStart + 4);
			chunkCount++;
			writer.Write(magic);
			writer.Write(0UL);
			writer.Write(0UL);
			writer.Write(unknown0);
			writer.Write(totalSize);
			writer.Write(chunkCount);
			foreach (uint chunkOffset2 in chunkOffsets)
			{
				writer.Write(chunkOffset2);
			}
			writer.Write(resourceChunkData);
			writer.Write(rest);
			byte[] restored = dest.ToArray();
			DxbcChecksum.Update(restored);
			return restored;
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
