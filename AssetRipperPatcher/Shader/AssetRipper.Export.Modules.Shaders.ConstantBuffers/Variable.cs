using System;
using AssetRipper.Export.Modules.Shaders.ShaderBlob.Parameters;
using AssetRipper.Export.Modules.Shaders.Types;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.GpuProgramType;

namespace AssetRipper.Export.Modules.Shaders.ConstantBuffers;

internal class Variable
{
	public AssetRipper.Export.Modules.Shaders.Types.ShaderType ShaderType { get; private set; }

	public string Name { get; private set; }

	public int NameIndex { get; private set; }

	public int Index { get; private set; }

	public int ArraySize { get; private set; }

	public ShaderParamType Type { get; private set; }

	public uint Length { get; set; }

	private Variable(string name, AssetRipper.Export.Modules.Shaders.Types.ShaderType shaderType)
	{
		Name = name;
		ShaderType = shaderType;
	}

	public Variable(MatrixParameter param, ShaderGpuProgramType programType)
	{
		ShaderType = new AssetRipper.Export.Modules.Shaders.Types.ShaderType(param, programType);
		Name = param.Name ?? throw new Exception("Variable name cannot be null");
		NameIndex = param.NameIndex;
		Index = param.Index;
		ArraySize = param.ArraySize;
		Length = checked((uint)(param.RowCount * param.ColumnCount * 4));
	}

	public Variable(VectorParameter param, ShaderGpuProgramType programType)
	{
		ShaderType = new AssetRipper.Export.Modules.Shaders.Types.ShaderType(param, programType);
		Name = param.Name ?? throw new Exception("Variable name cannot be null");
		NameIndex = param.NameIndex;
		Index = param.Index;
		ArraySize = param.ArraySize;
		Length = checked((uint)(param.Dim * 4));
	}

	public Variable(StructParameter param, ShaderGpuProgramType programType)
	{
		ShaderType = new AssetRipper.Export.Modules.Shaders.Types.ShaderType(param, programType);
		Name = param.Name ?? throw new Exception("Variable name cannot be null");
		NameIndex = param.NameIndex;
		Index = param.Index;
		ArraySize = param.ArraySize;
	}

	public static Variable CreateDummyVariable(string name, int index, int sizeToAdd, ShaderGpuProgramType programType)
	{
		if (sizeToAdd % 4 != 0 || sizeToAdd <= 0)
		{
			throw new Exception($"Invalid dummy variable size {sizeToAdd}");
		}
		int alignIndex = index % 16;
		checked
		{
			if (alignIndex != 0)
			{
				index += 16 - alignIndex;
				sizeToAdd -= 16 - alignIndex;
			}
			sizeToAdd -= unchecked(sizeToAdd % 16);
			VectorParameter param = new VectorParameter(name, ShaderParamType.Float, index, unchecked(sizeToAdd / 16), 4);
			Variable variable = new Variable(name, new AssetRipper.Export.Modules.Shaders.Types.ShaderType(param, programType));
			variable.NameIndex = -1;
			variable.Index = index;
			variable.ArraySize = param.ArraySize;
			variable.Type = ShaderParamType.Int;
			variable.Length = (uint)sizeToAdd;
			return variable;
		}
	}

	public static Variable CreateResourceBindVariable(ShaderGpuProgramType programType)
	{
		string name = "$Element";
		VectorParameter param = new VectorParameter(name, ShaderParamType.UInt, 0, 1);
		Variable variable = new Variable(name, new AssetRipper.Export.Modules.Shaders.Types.ShaderType(param, programType));
		variable.NameIndex = -1;
		variable.Index = 0;
		variable.ArraySize = param.ArraySize;
		variable.Length = 4u;
		variable.Type = ShaderParamType.UInt;
		return variable;
	}
}
