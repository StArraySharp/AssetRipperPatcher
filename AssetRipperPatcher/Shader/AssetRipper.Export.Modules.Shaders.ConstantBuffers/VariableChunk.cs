using System;
using System.Collections.Generic;
using System.Linq;
using AssetRipper.Export.Modules.Shaders.Extensions;
using AssetRipper.Export.Modules.Shaders.ShaderBlob.Parameters;
using AssetRipper.Export.Modules.Shaders.Types;
using AssetRipper.IO.Endian;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.GpuProgramType;

namespace AssetRipper.Export.Modules.Shaders.ConstantBuffers;

internal class VariableChunk
{
	private readonly List<VariableHeader> m_variableHeaders = new List<VariableHeader>();

	private readonly Dictionary<string, uint> m_variableNameLookup = new Dictionary<string, uint>();

	private readonly Dictionary<ShaderType, uint> m_typeLookup = new Dictionary<ShaderType, uint>();

	private readonly Variable[] m_variables;

	private readonly int m_constantBufferIndex;

	private readonly ShaderGpuProgramType m_programType;

	private readonly int majorVersion;

	internal uint Count => checked((uint)m_variables.Length);

	internal uint Size { get; private set; }

	public VariableChunk(ref BufferBinding bufferBinding, int constantBufferIndex, uint variableOffset, ShaderGpuProgramType programType)
	{
		m_constantBufferIndex = constantBufferIndex;
		m_programType = programType;
		majorVersion = programType.GetMajorDXVersion();
		m_variables = new Variable[1] { Variable.CreateResourceBindVariable(programType) };
		BuildVariableHeaders(variableOffset);
	}

	public VariableChunk(ref ConstantBuffer constantBuffer, int constantBufferIndex, uint variableOffset, ShaderGpuProgramType programType)
	{
		m_constantBufferIndex = constantBufferIndex;
		m_programType = programType;
		majorVersion = programType.GetMajorDXVersion();
		m_variables = BuildVariables(ref constantBuffer);
		BuildVariableHeaders(variableOffset);
	}

	private void BuildVariableHeaders(uint variableOffset)
	{
		uint variableSize = ((majorVersion >= 5) ? 40u : 24u);
		checked
		{
			uint variableCount = (uint)m_variables.Length;
			uint dataOffset = variableOffset + variableCount * variableSize;
			Variable[] variables = m_variables;
			foreach (Variable variable in variables)
			{
				m_variableNameLookup[variable.Name] = dataOffset;
				VariableHeader header = new VariableHeader
				{
					NameOffset = dataOffset,
					StartOffset = (uint)variable.Index,
					Variable = variable
				};
				dataOffset += (uint)variable.Name.Length + 1;
				m_typeLookup[variable.ShaderType] = dataOffset;
				dataOffset += variable.ShaderType.Size();
				variable.ShaderType.MemberOffset = ((variable.ShaderType.Members.Length != 0) ? dataOffset : 0u);
				dataOffset += (uint)variable.ShaderType.Members.Length * 12;
				ShaderTypeMember[] members = variable.ShaderType.Members;
				foreach (ShaderTypeMember member in members)
				{
					m_variableNameLookup[member.Name] = dataOffset;
					dataOffset += (uint)member.Name.Length + 1;
					m_typeLookup[member.ShaderType] = dataOffset;
					dataOffset += member.ShaderType.Size();
				}
				m_variableHeaders.Add(header);
			}
			Size = dataOffset - variableOffset;
		}
	}

	internal void Write(EndianWriter writer)
	{
		foreach (VariableHeader header in m_variableHeaders)
		{
			WriteVariableHeader(writer, header);
		}
		Variable[] variables = m_variables;
		foreach (Variable variable in variables)
		{
			writer.WriteStringZeroTerm(variable.Name);
			WriteShaderType(writer, variable.ShaderType);
			ShaderTypeMember[] members = variable.ShaderType.Members;
			foreach (ShaderTypeMember member in members)
			{
				uint nameOffset = m_variableNameLookup[member.Name];
				writer.Write(nameOffset);
				uint memberOffset = m_typeLookup[member.ShaderType];
				writer.Write(memberOffset);
				writer.Write(member.Index);
			}
			ShaderTypeMember[] members2 = variable.ShaderType.Members;
			foreach (ShaderTypeMember member2 in members2)
			{
				writer.WriteStringZeroTerm(member2.Name);
				WriteShaderType(writer, member2.ShaderType);
			}
		}
	}

	private Variable[] BuildVariables(ref ConstantBuffer constantBuffer)
	{
		List<Variable> variables = new List<Variable>();
		MatrixParameter[] matrixParams = constantBuffer.MatrixParams;
		foreach (MatrixParameter param in matrixParams)
		{
			variables.Add(new Variable(param, m_programType));
		}
		VectorParameter[] vectorParams = constantBuffer.VectorParams;
		foreach (VectorParameter param2 in vectorParams)
		{
			variables.Add(new Variable(param2, m_programType));
		}
		StructParameter[] structParams = constantBuffer.StructParams;
		foreach (StructParameter param3 in structParams)
		{
			variables.Add(new Variable(param3, m_programType));
		}
		variables = variables.OrderBy((Variable v) => v.Index).ToList();
		checked
		{
			if (true)
			{
				List<Variable> allVariables = new List<Variable>();
				uint currentSize = 0u;
				for (int i2 = 0; i2 < variables.Count; i2++)
				{
					Variable variable = variables[i2];
					if (variable.Index - currentSize >= 16)
					{
						long sizeToAdd = variable.Index - currentSize;
						int id1 = m_constantBufferIndex;
						int id2 = allVariables.Count;
						allVariables.Add(Variable.CreateDummyVariable($"unused_{id1}_{id2}", (int)currentSize, (int)sizeToAdd, m_programType));
					}
					allVariables.Add(variable);
					currentSize = (uint)variable.Index + variable.Length;
				}
				if (constantBuffer.Size - currentSize >= 16)
				{
					long sizeToAdd2 = constantBuffer.Size - currentSize;
					int id3 = m_constantBufferIndex;
					int id4 = allVariables.Count;
					allVariables.Add(Variable.CreateDummyVariable($"unused_{id3}_{id4}", (int)currentSize, (int)sizeToAdd2, m_programType));
				}
				variables = allVariables;
			}
			else
			{
				for (int i3 = 0; i3 < variables.Count; i3++)
				{
					if (i3 < variables.Count - 1)
					{
						variables[i3].Length = (uint)variables[i3 + 1].Index - (uint)variables[i3].Index;
					}
					else
					{
						variables[i3].Length = (uint)constantBuffer.Size - (uint)variables[i3].Index;
					}
				}
			}
			return variables.ToArray();
		}
	}

	private void WriteVariableHeader(EndianWriter writer, VariableHeader header)
	{
		writer.Write(header.NameOffset);
		writer.Write(header.StartOffset);
		writer.Write(header.Variable.Length);
		writer.Write(2u);
		uint typeOffset = m_typeLookup[header.Variable.ShaderType];
		writer.Write(typeOffset);
		writer.Write(0u);
		if (majorVersion >= 5)
		{
			writer.Write(0u);
			writer.Write(0u);
			writer.Write(0u);
			writer.Write(0u);
		}
	}

	private void WriteShaderType(EndianWriter writer, ShaderType shaderType)
	{
		checked
		{
			writer.Write((ushort)shaderType.ShaderVariableClass);
			writer.Write((ushort)shaderType.ShaderVariableType);
			writer.Write(shaderType.Rows);
			writer.Write(shaderType.Columns);
			writer.Write(shaderType.ElementCount);
			writer.Write(shaderType.MemberCount);
			writer.Write(shaderType.MemberOffset);
			if (majorVersion >= 5)
			{
				if (shaderType.ParentTypeOffset != 0 || shaderType.Unknown2 != 0 || shaderType.Unknown5 != 0 || shaderType.ParentNameOffset != 0)
				{
					throw new Exception("Shader variable type has invalid value");
				}
				writer.Write(shaderType.ParentTypeOffset);
				writer.Write(shaderType.Unknown2);
				writer.Write(shaderType.Unknown4);
				writer.Write(shaderType.Unknown5);
				writer.Write(shaderType.ParentNameOffset);
			}
		}
	}
}
