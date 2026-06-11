using System;
using System.Collections.Generic;
using System.IO;
using AssetRipper.Assets.Collections;
using AssetRipper.Assets.Generics;

namespace AssetRipper.Export.Modules.Shaders.ShaderBlob;

public sealed class ShaderSubProgramBlob
{
	private AssetCollection m_shaderCollection;

	private List<byte[]> m_decompressedBlobSegments = new List<byte[]>();

	private readonly Dictionary<(uint, uint), ShaderSubProgram> m_cachedSubPrograms = new Dictionary<(uint, uint), ShaderSubProgram>();

	public const string GpuProgramIndexName = "GpuProgramIndex";

	public ShaderSubProgramEntry[] Entries { get; set; } = Array.Empty<ShaderSubProgramEntry>();

	public void Read(AssetCollection shaderCollection, byte[] compressedBlob, uint offset, uint compressedLength, uint decompressedLength)
	{
		m_shaderCollection = shaderCollection;
		ReadBlob(compressedBlob, offset, compressedLength, decompressedLength, 0);
	}

	public void Read(AssetCollection shaderCollection, byte[] compressedBlob, AssetList<uint> offsets, AssetList<uint> compressedLengths, AssetList<uint> decompressedLengths)
	{
		m_shaderCollection = shaderCollection;
		for (int i = 0; i < offsets.Count; i = checked(i + 1))
		{
			ReadBlob(compressedBlob, offsets[i], compressedLengths[i], decompressedLengths[i], i);
		}
	}

	private void ReadBlob(byte[] compressedBlob, uint offset, uint compressedLength, uint decompressedLength, int segment)
	{
		while (m_decompressedBlobSegments.Count < checked(segment + 1))
		{
			m_decompressedBlobSegments.Add(Array.Empty<byte>());
		}
		m_decompressedBlobSegments[segment] = DecompressedBlob.DecompressBlob(compressedBlob, offset, compressedLength, decompressedLength);
		if (segment != 0)
		{
			return;
		}
		using MemoryStream blobMem = new MemoryStream(m_decompressedBlobSegments[segment]);
		using AssetReader blobReader = new AssetReader(blobMem, m_shaderCollection);
		Entries = ReadAssetArray(blobReader);
		m_cachedSubPrograms.Clear();
	}

	private static ShaderSubProgramEntry[] ReadAssetArray(AssetReader reader)
	{
		int count = reader.ReadInt32();
		ShaderSubProgramEntry[] array = CreateAndInitializeArray<ShaderSubProgramEntry>(count);
		for (int i = 0; i < count; i = checked(i + 1))
		{
			array[i].Read(reader);
		}
		if (reader.IsAlignArray)
		{
			reader.AlignStream();
		}
		return array;
	}

	private static T[] CreateAndInitializeArray<T>(int length) where T : new()
	{
		ArgumentOutOfRangeException.ThrowIfNegative(length, "length");
		if (length == 0)
		{
			return Array.Empty<T>();
		}
		T[] array = new T[length];
		for (int i = 0; i < length; i = checked(i + 1))
		{
			array[i] = new T();
		}
		return array;
	}

	public ShaderSubProgram GetSubProgram(uint blobIndex)
	{
		if (m_cachedSubPrograms.TryGetValue((blobIndex, blobIndex), out ShaderSubProgram subProgram))
		{
			return subProgram;
		}
		using MemoryStream blobMem = new MemoryStream(m_decompressedBlobSegments[Entries[blobIndex].Segment]);
		using AssetReader blobReader = new AssetReader(blobMem, m_shaderCollection);
		subProgram = new ShaderSubProgram();
		ReadSubProgram(blobReader, subProgram, blobIndex, readProgramData: true, readParams: true);
		m_cachedSubPrograms.TryAdd((blobIndex, blobIndex), subProgram);
		return subProgram;
	}

	public ShaderSubProgram GetSubProgram(uint blobIndex, uint paramBlobIndex)
	{
		if (m_cachedSubPrograms.TryGetValue((blobIndex, paramBlobIndex), out ShaderSubProgram subProgram))
		{
			return subProgram;
		}
		using MemoryStream blobMem = new MemoryStream(m_decompressedBlobSegments[Entries[blobIndex].Segment]);
		using AssetReader blobReader = new AssetReader(blobMem, m_shaderCollection);
		subProgram = new ShaderSubProgram();
		ReadSubProgram(blobReader, subProgram, blobIndex, readProgramData: true, readParams: false);
		ReadSubProgram(blobReader, subProgram, paramBlobIndex, readProgramData: false, readParams: true);
		m_cachedSubPrograms.TryAdd((blobIndex, paramBlobIndex), subProgram);
		return subProgram;
	}

	private void ReadSubProgram(AssetReader reader, ShaderSubProgram subProgram, uint index, bool readProgramData, bool readParams)
	{
		ShaderSubProgramEntry entry = Entries[index];
		reader.BaseStream.Position = entry.Offset;
		subProgram.Read(reader, readProgramData, readParams);
		checked
		{
			if (reader.BaseStream.Position != entry.Offset + entry.Length)
			{
				throw new Exception($"Read {reader.BaseStream.Position - entry.Offset} less than expected {entry.Length}");
			}
		}
	}
}
