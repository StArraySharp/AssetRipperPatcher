using System.IO;
using AssetRipper.Export.Modules.Shaders.IO;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.IO.Endian;
using AssetRipper.Primitives;

namespace AssetRipper.Export.Modules.Shaders.Exporters;

public class ShaderMetalExporter : ShaderTextExporter
{
	private const uint MetalFourCC = 4027435774u;

	public override string Name => "ShaderMetalExporter";

	public string? EntryName { get; private set; }

	public static bool HasBlob(UnityVersion version)
	{
		return version.GreaterThanOrEquals(5, 3);
	}

	public override void Export(ShaderWriter writer, ref ShaderSubProgram subProgram)
	{
		using MemoryStream memStream = new MemoryStream(subProgram.ProgramData);
		using BinaryReader reader = new BinaryReader(memStream);
		if (HasBlob(writer.Version))
		{
			long position = reader.BaseStream.Position;
			uint fourCC = reader.ReadUInt32();
			if (fourCC == 4027435774u)
			{
				int offset = reader.ReadInt32();
				reader.BaseStream.Position = checked(position + offset);
			}
			using EndianReader endReader = new EndianReader(reader.BaseStream, EndianType.LittleEndian);
			EntryName = endReader.ReadStringZeroTerm();
		}
		ShaderTextExporter.ExportText(writer, reader);
	}
}
