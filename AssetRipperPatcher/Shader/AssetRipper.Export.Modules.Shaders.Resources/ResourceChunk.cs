using System.Collections.Generic;
using System.Text;
using AssetRipper.Export.Modules.Shaders.ConstantBuffers;
using AssetRipper.Export.Modules.Shaders.Extensions;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.IO.Endian;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.GpuProgramType;

namespace AssetRipper.Export.Modules.Shaders.Resources;

internal class ResourceChunk
{
	private readonly Dictionary<string, uint> m_nameLookup = new Dictionary<string, uint>();

	private readonly uint m_chunkSize;

	private readonly uint m_constantBufferOffset;

	private readonly uint m_resourceBindingOffset;

	private readonly DXProgramType m_programType;

	private readonly uint m_creatorStringOffset;

	private readonly ConstantBufferChunk m_constantBuffers;

	private readonly ResourceBindingChunk m_resourceBindings;

	private readonly string m_creatorString;

	private readonly byte m_majorVersion;

	public uint Size => checked(m_chunkSize + 8);

	public ResourceChunk(UnityVersion version, ref ShaderSubProgram shaderSubprogram)
	{
		ShaderGpuProgramType programType = shaderSubprogram.GetProgramType(version);
		checked
		{
			m_majorVersion = (byte)programType.GetMajorDXVersion();
			m_resourceBindingOffset = ((m_majorVersion >= 5) ? 60u : 28u);
			m_resourceBindings = new ResourceBindingChunk(ref shaderSubprogram, m_resourceBindingOffset, m_nameLookup);
			m_constantBufferOffset = m_resourceBindingOffset + m_resourceBindings.Size;
			m_constantBuffers = new ConstantBufferChunk(version, ref shaderSubprogram, m_constantBufferOffset, m_nameLookup);
			m_creatorStringOffset = m_constantBufferOffset + m_constantBuffers.Size;
			m_creatorString = "AssetRipper";
			m_chunkSize = m_creatorStringOffset + (uint)m_creatorString.Length + 1;
			m_programType = programType.ToDXProgramType();
		}
	}

	public void Write(EndianWriter writer)
	{
		writer.Write(Encoding.ASCII.GetBytes("RDEF"));
		writer.Write(m_chunkSize);
		writer.Write(m_constantBuffers.Count);
		writer.Write(m_constantBufferOffset);
		writer.Write(m_resourceBindings.Count);
		writer.Write(m_resourceBindingOffset);
		byte minorVersion = 0;
		writer.Write(minorVersion);
		writer.Write(m_majorVersion);
		checked
		{
			writer.Write((ushort)m_programType);
			ShaderFlags flags = ShaderFlags.NoPreshader;
			writer.Write((uint)flags);
			writer.Write(m_creatorStringOffset);
			if (m_majorVersion >= 5)
			{
				writer.Write(Encoding.ASCII.GetBytes("RD11"));
				writer.Write(60u);
				writer.Write(24u);
				writer.Write(32u);
				writer.Write(40u);
				writer.Write(36u);
				writer.Write(12u);
				writer.Write(0u);
			}
			m_resourceBindings.Write(writer);
			m_constantBuffers.Write(writer);
			writer.WriteStringZeroTerm(m_creatorString);
		}
	}
}
