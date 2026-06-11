using System;
using System.Collections.Generic;
using System.Linq;
using AssetRipper.Export.Modules.Shaders.Extensions;
using AssetRipper.Export.Modules.Shaders.ShaderBlob.Parameters;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.GpuProgramType;

namespace AssetRipper.Export.Modules.Shaders.Types;

internal class ShaderType
{
	private readonly ShaderGpuProgramType m_programType;

	public ShaderVariableClass ShaderVariableClass { get; }

	public ShaderVariableType ShaderVariableType { get; }

	public ushort Rows { get; }

	public ushort Columns { get; }

	public ushort ElementCount { get; }

	public ushort MemberCount { get; }

	public uint MemberOffset { get; set; }

	public uint ParentTypeOffset { get; }

	public uint Unknown2 { get; }

	public uint Unknown4 { get; }

	public uint Unknown5 { get; }

	public uint ParentNameOffset { get; }

	public ShaderTypeMember[] Members { get; }

	public ShaderType(StructParameter structParameter, ShaderGpuProgramType programType)
	{
		List<ShaderTypeMember> members = new List<ShaderTypeMember>();
		members.AddRange(structParameter.VectorMembers.Select((VectorParameter p) => new ShaderTypeMember(p, programType)));
		members.AddRange(structParameter.MatrixMembers.Select((MatrixParameter p) => new ShaderTypeMember(p, programType)));
		Members = members.OrderBy((ShaderTypeMember v) => v.Index).ToArray();
		ShaderVariableClass = ShaderVariableClass.Struct;
		ShaderVariableType = ShaderVariableType.Void;
		Rows = 0;
		Columns = 0;
		ElementCount = 0;
		MemberCount = checked((ushort)Members.Length);
		MemberOffset = 0u;
		m_programType = programType;
	}

	public ShaderType(MatrixParameter matrixParam, ShaderGpuProgramType programType)
	{
		Members = Array.Empty<ShaderTypeMember>();
		ShaderVariableClass = ShaderVariableClass.MatrixColumns;
		ShaderVariableType = GetVariableType(matrixParam.Type);
		Rows = matrixParam.RowCount;
		Columns = matrixParam.ColumnCount;
		ElementCount = checked((ushort)matrixParam.ArraySize);
		MemberCount = 0;
		MemberOffset = 0u;
		m_programType = programType;
	}

	public ShaderType(VectorParameter vectorParam, ShaderGpuProgramType programType)
	{
		Members = Array.Empty<ShaderTypeMember>();
		ShaderVariableClass = ((vectorParam.Dim > 1) ? ShaderVariableClass.Vector : ShaderVariableClass.Scalar);
		ShaderVariableType = GetVariableType(vectorParam.Type);
		Rows = 1;
		Columns = vectorParam.Dim;
		ElementCount = checked((ushort)vectorParam.ArraySize);
		MemberCount = 0;
		MemberOffset = 0u;
		m_programType = programType;
	}

	private static ShaderVariableType GetVariableType(ShaderParamType paramType)
	{
		if (1 == 0)
		{
		}
		ShaderVariableType result = paramType switch
		{
			ShaderParamType.Bool => ShaderVariableType.Bool, 
			ShaderParamType.Float => ShaderVariableType.Float, 
			ShaderParamType.Half => ShaderVariableType.Float, 
			ShaderParamType.Int => ShaderVariableType.Int, 
			ShaderParamType.Short => ShaderVariableType.Int, 
			ShaderParamType.TypeCount => ShaderVariableType.Int, 
			ShaderParamType.UInt => ShaderVariableType.UInt, 
			_ => throw new Exception($"Unexpected param type {paramType}"), 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	public uint Length()
	{
		uint variableSize = 4u;
		return checked(variableSize * Rows * Columns * ElementCount);
	}

	public uint Size()
	{
		int majorVersion = m_programType.GetMajorDXVersion();
		return (majorVersion >= 5) ? 36u : 16u;
	}
}
