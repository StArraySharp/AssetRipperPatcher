namespace AssetRipper.Export.Modules.Shaders.Resources;

internal enum ShaderInputType
{
	CBuffer,
	TBuffer,
	Texture,
	Sampler,
	UavRwTyped,
	Structured,
	UavRwStructured,
	ByteAddress,
	UavRwByteAddress,
	UavAppendStructured,
	UavConsumeStructured,
	UavRwStructuredWithCounter
}
