namespace AssetRipper.Export.Modules.Shaders.ShaderBlob.Parameters;

public sealed record SamplerParameter
{
	public uint Sampler { get; set; }

	public int BindPoint { get; set; }

	public SamplerParameter()
	{
	}

	public SamplerParameter(uint sampler, int bindPoint)
	{
		Sampler = sampler;
		BindPoint = bindPoint;
	}
}
