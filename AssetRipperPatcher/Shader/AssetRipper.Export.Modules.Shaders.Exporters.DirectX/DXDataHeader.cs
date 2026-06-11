using System.IO;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader;

namespace AssetRipper.Export.Modules.Shaders.Exporters.DirectX;

public struct DXDataHeader
{
	public byte Version { get; set; }

	public byte Textures { get; set; }

	public byte CBs { get; set; }

	public byte Samplers { get; set; }

	public byte UAVs { get; set; }

	public DXInputPrimitive GSInputPrimitive { get; set; }

	public static bool HasHeader(GPUPlatform graphicApi)
	{
		return graphicApi != GPUPlatform.D3D9;
	}

	public static bool HasGSInputPrimitive(UnityVersion version)
	{
		return version.GreaterThanOrEquals(5, 4);
	}

	public static int GetDataOffset(UnityVersion version, GPUPlatform graphicApi, int headerVersion)
	{
		if (HasHeader(graphicApi))
		{
			int offset = (HasGSInputPrimitive(version) ? 6 : 5);
			if (headerVersion >= 2)
			{
				offset = checked(offset + 32);
			}
			return offset;
		}
		return 0;
	}

	public void Read(BinaryReader reader, UnityVersion version)
	{
		Version = reader.ReadByte();
		Textures = reader.ReadByte();
		CBs = reader.ReadByte();
		Samplers = reader.ReadByte();
		UAVs = reader.ReadByte();
		if (HasGSInputPrimitive(version))
		{
			GSInputPrimitive = (DXInputPrimitive)reader.ReadByte();
		}
	}

	public void Write(BinaryWriter writer, UnityVersion version)
	{
		writer.Write(Version);
		writer.Write(Textures);
		writer.Write(CBs);
		writer.Write(Samplers);
		writer.Write(UAVs);
		if (HasGSInputPrimitive(version))
		{
			writer.Write(checked((byte)GSInputPrimitive));
		}
	}
}
