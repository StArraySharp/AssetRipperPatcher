using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.DirectX;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL;
using AssetRipper.Import.Logging;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.GpuProgramType;
using DXDecompiler.DX9Shader;
using DXDecompiler.Util;

namespace AssetRipper.Export.Modules.Shaders.Handlers;

public static class USCDecompilerHandler
{
	private enum DXProgramType
	{
		Unknown,
		DX9,
		DXBC
	}

	public static bool TryDecompile(byte[] data, int offset, ShaderSubProgram subProgram, [NotNullWhen(true)] out string? decompiledText, out UShaderProgram? uShaderProgram)
	{
		return TryDecompile(GetRelevantData(data, offset), subProgram, out decompiledText, out uShaderProgram);
	}

	public static bool TryDecompile(byte[] data, ShaderSubProgram subProgram, [NotNullWhen(true)] out string? decompiledText, out UShaderProgram? uShaderProgram)
	{
		if (data == null)
		{
			throw new ArgumentNullException("data");
		}
		if (data.Length == 0)
		{
			throw new ArgumentException("inputData cannot have zero length");
		}
		uShaderProgram = null;
		try
		{
			DXProgramType programType = GetProgramType(data);
			DXProgramType dXProgramType = programType;
			DXProgramType dXProgramType2 = dXProgramType;
			if (dXProgramType2 == DXProgramType.DXBC)
			{
				DirectXCompiledShader dxShader = new DirectXCompiledShader(new MemoryStream(data));
				DirectXProgramToUSIL dx2UsilConverter = new DirectXProgramToUSIL(dxShader);
				dx2UsilConverter.Convert();
				ShaderGpuProgramType shaderProgramType = subProgram.GetProgramType(new UnityVersion(5, 5));
				bool isVertex = shaderProgramType == ShaderGpuProgramType.DX11VertexSM40 || shaderProgramType == ShaderGpuProgramType.DX11VertexSM50;
				dx2UsilConverter.shader.shaderFunctionType = (isVertex ? UShaderFunctionType.Vertex : UShaderFunctionType.Fragment);
				UShaderProgram uProg = dx2UsilConverter.shader;
				USILOptimizerApplier.Apply(uProg, subProgram);
				UShaderFunctionToHLSL hlslConverter = new UShaderFunctionToHLSL(uProg);
				decompiledText = hlslConverter.Convert(0);
				Logger.Info(LogCategory.Export, "USC successfully decompiled a DXBC shader");
				uShaderProgram = uProg;
				return !string.IsNullOrEmpty(decompiledText);
			}
			throw new NotSupportedException("Only DX11 shaders can be decompiled at this time.");
		}
		catch (Exception ex)
		{
			Logger.Info(LogCategory.Export, "USC threw an exception while attempting to decompile a shader");
			Logger.Info(LogCategory.Export, ex.ToString());
		}
		decompiledText = null;
		return false;
	}

	private static DXProgramType GetProgramType(ReadOnlySpan<byte> data)
	{
		if (data.Length < 4)
		{
			return DXProgramType.Unknown;
		}
		uint dxbcHeader = BinaryPrimitives.ReadUInt32LittleEndian(data);
		if (dxbcHeader == "DXBC".ToFourCc())
		{
			return DXProgramType.DXBC;
		}
		if (dxbcHeader == 4278132737u)
		{
			return DXProgramType.DXBC;
		}
		ShaderType dx9ShaderType = (ShaderType)BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(2, data.Length - 2));
		if (dx9ShaderType == ShaderType.Vertex || dx9ShaderType == ShaderType.Pixel || dx9ShaderType == ShaderType.Effect)
		{
			return DXProgramType.DX9;
		}
		return DXProgramType.Unknown;
	}

	private static byte[] GetRelevantData(ReadOnlySpan<byte> bytes, int offset)
	{
		if (offset < 0 || offset > bytes.Length)
		{
			throw new ArgumentOutOfRangeException("offset");
		}
		return bytes.Slice(offset, bytes.Length - offset).ToArray();
	}
}
