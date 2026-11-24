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
		subProgram = new ShaderSubProgram();
		ReadSubProgram(subProgram, blobIndex, readProgramData: true, readParams: true);
		m_cachedSubPrograms.TryAdd((blobIndex, blobIndex), subProgram);
		return subProgram;
	}

	/// <summary>
	/// Read only the program payload, without touching the parameter table.
	///
	/// <para>Unity 6 stores a variant's reflection in a separate blob entry, so the
	/// combined read (<see cref="GetSubProgram(uint)"/>) can fail even though the
	/// DXBC/GLSL bytes are perfectly readable.  The binary export only needs the
	/// payload in that case; the reflection is best-effort.</para>
	/// </summary>
	public ShaderSubProgram GetSubProgramData(uint blobIndex)
	{
		ShaderSubProgram subProgram = new ShaderSubProgram();
		ReadSubProgram(subProgram, blobIndex, readProgramData: true, readParams: false, allowTrailingParameters: true);
		return subProgram;
	}

	public ShaderSubProgram GetSubProgram(uint blobIndex, uint paramBlobIndex)
	{
		if (m_cachedSubPrograms.TryGetValue((blobIndex, paramBlobIndex), out ShaderSubProgram subProgram))
		{
			return subProgram;
		}
		subProgram = new ShaderSubProgram();
		ReadSubProgram(subProgram, blobIndex, readProgramData: true, readParams: false);
		ReadSubProgram(subProgram, paramBlobIndex, readProgramData: false, readParams: true);
		m_cachedSubPrograms.TryAdd((blobIndex, paramBlobIndex), subProgram);
		return subProgram;
	}

	private void ReadSubProgram(ShaderSubProgram subProgram, uint index, bool readProgramData, bool readParams, bool allowTrailingParameters = false)
	{
		ShaderSubProgramEntry entry = Entries[index];
		using MemoryStream blobMem = new MemoryStream(m_decompressedBlobSegments[entry.Segment], writable: false);
		long end = (long)entry.Offset + entry.Length;
		if (entry.Offset < 0 || entry.Length < 0 || end > blobMem.Length)
		{
			throw new InvalidDataException($"Shader blob entry {index} lies outside segment {entry.Segment}.");
		}

		using AssetReader reader = new AssetReader(blobMem, m_shaderCollection);
		blobMem.Position = entry.Offset;
		subProgram.Read(reader, readProgramData, readParams);
		if (blobMem.Position > end || (!allowTrailingParameters && blobMem.Position != end))
		{
			throw new InvalidDataException($"Shader blob entry {index}: read {blobMem.Position - entry.Offset} bytes, expected {entry.Length}.");
		}
	}
}
