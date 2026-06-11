using System;

namespace AssetRipper.Export.Modules.Shaders.Resources;

[Flags]
internal enum ShaderInputFlags
{
	None = 0,
	UserPacked = 1,
	ComparisonSampler = 2,
	TextureComponent0 = 4,
	TextureComponent1 = 8,
	TextureComponents = 0xC,
	Unused = 0x10
}
