using System.Diagnostics;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader;

namespace AssetRipper.Export.Modules.Shaders.ShaderBlob.Parameters;

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
public sealed class VectorParameter : NumericShaderParameter
{
	public byte Dim
	{
		get
		{
			return base.RowCount;
		}
		set
		{
			base.RowCount = value;
		}
	}

	public VectorParameter()
	{
	}

	public VectorParameter(string name, ShaderParamType type, int index, int columns)
	{
		base.Name = name;
		base.NameIndex = -1;
		base.Index = index;
		base.ArraySize = 0;
		base.Type = type;
		Dim = checked((byte)columns);
		base.ColumnCount = 1;
		base.IsMatrix = false;
	}

	public VectorParameter(string name, ShaderParamType type, int index, int arraySize, int columns)
		: this(name, type, index, columns)
	{
		base.ArraySize = arraySize;
	}

	private string? GetDebuggerDisplay()
	{
		return base.Name;
	}
}
