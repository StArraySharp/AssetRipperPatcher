using System;

namespace AssetRipper.Export.Modules.Shaders.ShaderBlob;

public sealed class ParserBindChannels
{
	public ShaderBindChannel[] Channels { get; set; } = Array.Empty<ShaderBindChannel>();

	public int SourceMap { get; set; }

	public ParserBindChannels()
	{
	}

	public ParserBindChannels(ShaderBindChannel[] channels, int sourceMap)
	{
		Channels = channels;
		SourceMap = sourceMap;
	}
}
