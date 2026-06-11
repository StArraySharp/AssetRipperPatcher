namespace AssetRipper.Export.Modules.Shaders.ShaderBlob.Parameters;

public sealed record UAVParameter
{
	public string Name { get; set; } = string.Empty;

	public int NameIndex { get; set; }

	public int Index { get; set; }

	public int OriginalIndex { get; set; }

	public UAVParameter()
	{
	}

	public UAVParameter(string name, int index, int originalIndex)
	{
		Name = name;
		NameIndex = -1;
		Index = index;
		OriginalIndex = originalIndex;
	}
}
