using System.Collections.Generic;
using System.IO;
using AssetRipper.Export.Modules.Shaders.IO;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.SourceGenerated.Extensions;

namespace AssetRipper.Export.Modules.Shaders.Exporters;

public class ShaderTextExporter
{
	protected const int ExpectedIndent = 5;

	public virtual string Name => "ShaderTextExporter";

	public virtual void Export(ShaderWriter writer, ref ShaderSubProgram subProgram)
	{
		byte[] exportData = subProgram.ProgramData;
		using MemoryStream memStream = new MemoryStream(exportData);
		using BinaryReader reader = new BinaryReader(memStream);
		ExportText(writer, reader, Name);
	}

	protected static void ExportText(TextWriter writer, BinaryReader reader)
	{
		ExportText(writer, reader, null);
	}

	protected static void ExportText(TextWriter writer, BinaryReader reader, string? name)
	{
		List<char> characters = new List<char>();
		if (!string.IsNullOrEmpty(name))
		{
			characters.Add('/');
			characters.Add('/');
			char[] array = name.ToCharArray();
			foreach (char c in array)
			{
				characters.Add(c);
			}
			characters.Add('\n');
		}
		while (reader.BaseStream.Position != reader.BaseStream.Length)
		{
			characters.Add(reader.ReadChar());
		}
		ExportText(writer, characters.ToArray());
	}

	protected static void ExportText(TextWriter writer, char[] array)
	{
		checked
		{
			for (int i = 0; i < array.Length; i++)
			{
				char c = array[i];
				if (c == '\n')
				{
					if (i == array.Length - 1)
					{
						break;
					}
					writer.Write(c);
					writer.WriteIndent(5);
				}
				else
				{
					writer.Write(c);
				}
			}
		}
	}

	protected static void ExportListing(TextWriter writer, string listing)
	{
		writer.Write('\n');
		writer.WriteIndent(5);
		int i = 0;
		while (i < listing.Length)
		{
			char c = listing[checked(i++)];
			bool newLine = false;
			switch (c)
			{
			case '\r':
			{
				if (i == listing.Length)
				{
					newLine = true;
					break;
				}
				char nc = listing[i];
				if (nc != '\n')
				{
					newLine = true;
				}
				break;
			}
			case '\n':
				newLine = true;
				break;
			}
			if (newLine)
			{
				if (i == listing.Length)
				{
					break;
				}
				writer.Write(c);
				writer.WriteIndent(5);
			}
			else
			{
				writer.Write(c);
			}
		}
	}
}
