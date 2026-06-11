using System.IO;
using AssetRipper.Assets.Collections;
using AssetRipper.IO.Endian;

namespace AssetRipper.Export.Modules.Shaders;

public sealed class AssetReader : EndianReader
{
	public AssetCollection AssetCollection { get; }

	public AssetReader(Stream stream, AssetCollection assetCollection)
		: base(stream, assetCollection.EndianType, alignArray: false)
	{
		AssetCollection = assetCollection;
	}

	public override string ReadString()
	{
		int length = ReadInt32();
		if (length == 0)
		{
			return string.Empty;
		}
		string ret = ReadString(length);
		AlignStream();
		return ret;
	}
}
