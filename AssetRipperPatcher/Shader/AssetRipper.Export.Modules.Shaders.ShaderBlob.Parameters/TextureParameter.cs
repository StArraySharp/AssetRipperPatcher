using System.Diagnostics;

namespace AssetRipper.Export.Modules.Shaders.ShaderBlob.Parameters;

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
public sealed class TextureParameter
{
	public string Name { get; set; } = string.Empty;

	public int NameIndex { get; set; }

	public int Index { get; set; }

	public int SamplerIndex { get; set; }

	public bool MultiSampled { get; set; }

	public byte Dim { get; set; }

	public TextureParameter()
	{
	}

	public TextureParameter(string name, int index, byte dimension, int sampler)
	{
		Name = name;
		NameIndex = -1;
		Index = index;
		Dim = dimension;
		SamplerIndex = sampler;
		MultiSampled = false;
	}

	public TextureParameter(string name, int index, byte dimension, int sampler, bool multiSampled)
		: this(name, index, dimension, sampler)
	{
		MultiSampled = multiSampled;
	}

	private string GetDebuggerDisplay()
	{
		return Name;
	}
}
