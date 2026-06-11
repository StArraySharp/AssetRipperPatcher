using System.IO;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler;

public static class DirectXExtensions
{
	public static long Align(this BinaryReader reader, int bytes)
	{
		long position = reader.BaseStream.Position;
		checked
		{
			return position + unchecked(checked(bytes - unchecked(position % bytes)) % bytes);
		}
	}

	public static string ReadNullString(this BinaryReader reader)
	{
		string result = "";
		char character;
		while ((character = reader.ReadChar()) != 0 && reader.BaseStream.Position < reader.BaseStream.Length)
		{
			result += character;
		}
		return result;
	}
}
