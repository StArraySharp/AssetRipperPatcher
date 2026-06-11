using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using AssetRipper.Import.Logging;
using DXDecompiler;
using DXDecompiler.Decompiler;
using DXDecompiler.DX9Shader;
using DXDecompiler.Util;

namespace AssetRipper.Export.Modules.Shaders.Handlers;

public static class DXDecompilerlyHandler
{
	private enum DXProgramType
	{
		Unknown,
		DX9,
		DXBC
	}

	public static bool TryDisassemble(byte[] data, int offset, [NotNullWhen(true)] out string? disassemblyText)
	{
		return TryDisassemble(GetRelevantData(data, offset), out disassemblyText);
	}

	public static bool TryDisassemble(byte[] data, [NotNullWhen(true)] out string? disassemblyText)
	{
		ArgumentNullException.ThrowIfNull(data, "data");
		if (data.Length == 0)
		{
			throw new ArgumentException("inputData cannot have zero length", "data");
		}
		try
		{
			switch (GetProgramType(data))
			{
			case DXProgramType.DXBC:
			{
				BytecodeContainer container = new BytecodeContainer(data);
				disassemblyText = container.ToString();
				return !string.IsNullOrEmpty(disassemblyText);
			}
			case DXProgramType.DX9:
				disassemblyText = AsmWriter.Disassemble(data);
				return !string.IsNullOrEmpty(disassemblyText);
			}
		}
		catch (Exception ex)
		{
			Logger.Error(LogCategory.Export, "DXDecompilerly threw an exception while attempting to disassemble a shader");
			Logger.Verbose(LogCategory.Export, ex.ToString());
		}
		disassemblyText = null;
		return false;
	}

	public static bool TryDecompile(byte[] data, int offset, [NotNullWhen(true)] out string? decompiledText)
	{
		return TryDecompile(GetRelevantData(data, offset), out decompiledText);
	}

	public static bool TryDecompile([NotNull] byte[] data, [NotNullWhen(true)] out string? decompiledText)
	{
		ArgumentNullException.ThrowIfNull(data, "data");
		if (data.Length == 0)
		{
			throw new ArgumentException("inputData cannot have zero length", "data");
		}
		try
		{
			switch (GetProgramType(data))
			{
			case DXProgramType.DXBC:
				decompiledText = HLSLDecompiler.Decompile(data);
				Logger.Info(LogCategory.Export, "DXDecompilerly successfully decompiled a DXBC shader");
				return !string.IsNullOrEmpty(decompiledText);
			case DXProgramType.DX9:
				decompiledText = HlslWriter.Decompile(data);
				Logger.Info(LogCategory.Export, "DXDecompilerly successfully decompiled a DX9 shader");
				return !string.IsNullOrEmpty(decompiledText);
			}
		}
		catch (Exception ex)
		{
			Logger.Verbose(LogCategory.Export, "DXDecompilerly threw an exception while attempting to decompile a shader");
			Logger.Verbose(LogCategory.Export, ex.ToString());
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
