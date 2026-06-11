using System.Collections.Generic;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.Modules.Shaders.ShaderBlob.Parameters;
using AssetRipper.IO.Endian;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.GpuProgramType;

namespace AssetRipper.Export.Modules.Shaders.ConstantBuffers;

internal class ConstantBufferChunk
{
	private readonly IReadOnlyList<VariableChunk> m_variables;

	private readonly Dictionary<string, uint> m_nameLookup;

	private readonly uint m_contantBufferOffset;

	private ShaderSubProgram m_shaderSubprogram;

	internal uint Count => checked((uint)(m_shaderSubprogram.ConstantBuffers.Length + m_shaderSubprogram.BufferParameters.Length));

	internal uint Size { get; }

	public ConstantBufferChunk(UnityVersion version, ref ShaderSubProgram shaderSubprogram, uint contantBufferOffset, Dictionary<string, uint> nameLookup)
	{
		m_shaderSubprogram = shaderSubprogram;
		m_contantBufferOffset = contantBufferOffset;
		m_nameLookup = nameLookup;
		ShaderGpuProgramType programType = shaderSubprogram.GetProgramType(version);
		checked
		{
			uint headerSize = Count * 24;
			uint variableOffset = contantBufferOffset + headerSize;
			int constantBufferIndex = 0;
			List<VariableChunk> variables = new List<VariableChunk>();
			for (int i = 0; i < shaderSubprogram.ConstantBuffers.Length; i++)
			{
				VariableChunk variableChunk = new VariableChunk(ref shaderSubprogram.ConstantBuffers[i], constantBufferIndex++, variableOffset, programType);
				variables.Add(variableChunk);
				variableOffset += variableChunk.Size;
			}
			for (int j = 0; j < shaderSubprogram.BufferParameters.Length; j++)
			{
				VariableChunk variableChunk2 = new VariableChunk(ref shaderSubprogram.BufferParameters[j], constantBufferIndex++, variableOffset, programType);
				variables.Add(variableChunk2);
				variableOffset += variableChunk2.Size;
			}
			m_variables = variables;
			Size = variableOffset - contantBufferOffset;
		}
	}

	public void Write(EndianWriter writer)
	{
		checked
		{
			uint headerSize = Count * 24;
			uint variableOffset = m_contantBufferOffset + headerSize;
			int variableIndex = 0;
			int i = 0;
			while (i < m_shaderSubprogram.ConstantBuffers.Length)
			{
				ConstantBuffer constantBuffer = m_shaderSubprogram.ConstantBuffers[i];
				VariableChunk variableChunk = m_variables[variableIndex];
				uint nameOffset = m_nameLookup[constantBuffer.Name];
				writer.Write(nameOffset);
				writer.Write(variableChunk.Count);
				writer.Write(variableOffset);
				writer.Write((uint)constantBuffer.Size);
				writer.Write(0u);
				writer.Write(0u);
				variableOffset += variableChunk.Size;
				i++;
				variableIndex++;
			}
			int i2 = 0;
			while (i2 < m_shaderSubprogram.BufferParameters.Length)
			{
				BufferBinding bufferParamater = m_shaderSubprogram.BufferParameters[i2];
				VariableChunk variableChunk2 = m_variables[variableIndex];
				uint nameOffset2 = m_nameLookup[bufferParamater.Name];
				writer.Write(nameOffset2);
				writer.Write(variableChunk2.Count);
				writer.Write(variableOffset);
				writer.Write(4u);
				writer.Write(0u);
				writer.Write(3u);
				variableOffset += variableChunk2.Size;
				i2++;
				variableIndex++;
			}
			foreach (VariableChunk variableChunk3 in m_variables)
			{
				variableChunk3.Write(writer);
			}
		}
	}
}
