using AssetRipper.Export.Modules.Shaders.ShaderBlob.Parameters;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.GpuProgramType;

namespace AssetRipper.Export.Modules.Shaders.Types;

internal class ShaderTypeMember
{
	public string Name { get; }

	public ShaderType ShaderType { get; }

	public ShaderGpuProgramType ProgramType { get; }

	public uint Index { get; }

	public ShaderTypeMember(MatrixParameter param, ShaderGpuProgramType programType)
	{
		ProgramType = programType;
		Name = param.Name ?? "";
		ShaderType = new ShaderType(param, programType);
		Index = checked((uint)param.Index);
	}

	public ShaderTypeMember(VectorParameter param, ShaderGpuProgramType programType)
	{
		ProgramType = programType;
		Name = param.Name ?? "";
		ShaderType = new ShaderType(param, programType);
		Index = checked((uint)param.Index);
	}
}
