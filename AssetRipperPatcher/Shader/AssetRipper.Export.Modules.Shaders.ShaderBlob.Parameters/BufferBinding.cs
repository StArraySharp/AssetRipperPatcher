namespace AssetRipper.Export.Modules.Shaders.ShaderBlob.Parameters;

public sealed record BufferBinding
{
	public string Name { get; set; } = string.Empty;

	public int NameIndex { get; set; }

	public int Index { get; set; }

	public int ArraySize { get; set; }

	public BufferBinding()
	{
	}

	public BufferBinding(string name, int index)
	{
		Name = name;
		NameIndex = -1;
		Index = index;
		ArraySize = 0;
	}
}
