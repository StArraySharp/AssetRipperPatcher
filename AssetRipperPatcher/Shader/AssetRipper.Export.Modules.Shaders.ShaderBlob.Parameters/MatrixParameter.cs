using System.Diagnostics;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader;

namespace AssetRipper.Export.Modules.Shaders.ShaderBlob.Parameters;

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
public sealed class MatrixParameter : NumericShaderParameter
{
	public MatrixParameter()
	{
	}

	public MatrixParameter(string name, ShaderParamType type, int index, int rowCount, int columnCount)
	{
		base.Name = name;
		base.NameIndex = -1;
		base.Index = index;
		base.ArraySize = 0;
		base.Type = type;
		checked
		{
			base.RowCount = (byte)rowCount;
			base.ColumnCount = (byte)columnCount;
			base.IsMatrix = true;
		}
	}

	public MatrixParameter(string name, ShaderParamType type, int index, int arraySize, int rowCount, int columnCount)
		: this(name, type, index, rowCount, columnCount)
	{
		base.ArraySize = arraySize;
	}

	private string? GetDebuggerDisplay()
	{
		return base.Name;
	}
}
