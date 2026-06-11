using AssetRipper.Export.Modules.Shaders.ShaderBlob.Parameters;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Blocks;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.DirectX;

public static class DXShaderNamingUtils
{
	public static string GetConstantBufferParamTypeName(VectorParameter param)
	{
		return GetConstantBufferParamTypeName(param.Dim, 1, param.Type, isMatrix: false);
	}

	public static string GetConstantBufferParamTypeName(MatrixParameter param)
	{
		return GetConstantBufferParamTypeName(param.RowCount, param.ColumnCount, param.Type, isMatrix: true);
	}

	public static string GetConstantBufferParamTypeName(NumericShaderParameter param)
	{
		return GetConstantBufferParamTypeName(param.RowCount, param.ColumnCount, param.Type, isMatrix: true);
	}

	public static string GetConstantBufferParamTypeName(int rowCount, int columnCount, ShaderParamType paramType, bool isMatrix)
	{
		string name = "unknownType";
		string baseName = paramType.ToString().ToLower();
		switch (columnCount)
		{
		case 1:
			if (rowCount == 1)
			{
				name = baseName ?? "";
			}
			if (rowCount == 2)
			{
				name = baseName + "2";
			}
			if (rowCount == 3)
			{
				name = baseName + "3";
			}
			if (rowCount == 4)
			{
				name = baseName + "4";
			}
			break;
		case 4:
			if (rowCount == 4 && isMatrix)
			{
				name = baseName + "4x4";
			}
			break;
		}
		return name;
	}

	public static string GetISGNInputName(ISGN.Input input)
	{
		string type = ((input.index <= 0) ? input.name : (input.name + input.index));
		string name = input.name;
		if (1 == 0)
		{
		}
		string result = name switch
		{
			"SV_POSITION" => "position", 
			"SV_Position" => "position", 
			"SV_IsFrontFace" => "facing", 
			"POSITION" => "vertex", 
			_ => type.ToLower(), 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	public static string GetOSGNOutputName(OSGN.Output output)
	{
		string type = ((output.index <= 0) ? output.name : (output.name + output.index));
		if (HasSpecialInputOutputName(output.name))
		{
			return GetSpecialInputOutputName(output.name);
		}
		string name = output.name;
		if (1 == 0)
		{
		}
		string result = ((name == "SV_POSITION") ? "position" : ((!(name == "POSITION")) ? type.ToLower() : "vertex"));
		if (1 == 0)
		{
		}
		return result;
	}

	public static bool HasSpecialInputOutputName(string typeName)
	{
		return GetSpecialInputOutputName(typeName) != string.Empty;
	}

	public static string GetSpecialInputOutputName(string typeName)
	{
		return typeName switch
		{
			"SV_Depth" => "oDepth", 
			"SV_Coverage" => "oMask", 
			"SV_DepthGreaterEqual" => "oDepthGE", 
			"SV_DepthLessEqual" => "oDepthLE", 
			"SV_StencilRef" => "oStencilRef", 
			_ => string.Empty, 
		};
	}

	public static bool HasSpecialInputOutputName(USILOperandType operandType)
	{
		return GetSpecialInputOutputName(operandType) != string.Empty;
	}

	public static string GetSpecialInputOutputName(USILOperandType operandType)
	{
		return operandType switch
		{
			USILOperandType.InputCoverageMask => "vCoverage", 
			USILOperandType.InputThreadGroupID => "vThreadGroupID", 
			USILOperandType.InputThreadID => "vThreadID", 
			USILOperandType.InputThreadIDInGroup => "vThreadIDInGroup", 
			USILOperandType.InputThreadIDInGroupFlattened => "vThreadIDInGroupFlattened", 
			USILOperandType.InputPrimitiveID => "vPrim", 
			USILOperandType.InputForkInstanceID => "vForkInstanceID", 
			USILOperandType.InputGSInstanceID => "vGSInstanceID", 
			USILOperandType.InputDomainPoint => "vDomain", 
			USILOperandType.OutputControlPointID => "outputControlPointID", 
			USILOperandType.OutputDepth => "oDepth", 
			USILOperandType.OutputCoverageMask => "oMask", 
			USILOperandType.OutputDepthGreaterEqual => "oDepthGE", 
			USILOperandType.OutputDepthLessEqual => "oDepthLE", 
			USILOperandType.StencilRef => "oStencilRef", 
			_ => string.Empty, 
		};
	}

	public static string GetOSGNFormatName(OSGN.Output output)
	{
		int maskSize = GetMaskSize(output.mask);
		FormatType format = (FormatType)output.format;
		return format.ToString() + ((maskSize != 1) ? ((object)maskSize) : "");
	}

	public static int GetMaskSize(byte mask)
	{
		int p = 0;
		checked
		{
			for (int i = 0; i < 4; i++)
			{
				if (((mask >> i) & 1) == 1)
				{
					p++;
				}
			}
			return p;
		}
	}
}
