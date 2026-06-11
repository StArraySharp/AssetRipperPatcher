using System;
using System.IO;
using System.Text;
using AssetRipper.Export.Modules.Shaders.Exporters;
using AssetRipper.Export.Modules.Shaders.Extensions;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.IO.Files;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated.Classes.ClassID_48;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.GpuProgramType;

namespace AssetRipper.Export.Modules.Shaders.IO;

public sealed class ShaderWriter : InvariantStreamWriter
{
	private readonly Func<GPUPlatform, ShaderTextExporter> m_exporterInstantiator;

	public IShader Shader { get; }

	public ShaderSubProgramBlob[] Blobs { get; }

	public UnityVersion Version => Shader.Collection.Version;

	public BuildTarget Platform => Shader.Collection.Platform;

	public bool WriteQuotesAroundProgram { get; set; } = true;

	public ShaderWriter(Stream stream, IShader shader, Func<GPUPlatform, ShaderTextExporter> exporterInstantiator)
		: base(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 4096, leaveOpen: true)
	{
		Shader = shader ?? throw new ArgumentNullException("shader");
		Blobs = shader.ReadBlobs();
		m_exporterInstantiator = exporterInstantiator ?? throw new ArgumentNullException("exporterInstantiator");
	}

	public void WriteShaderData(ref ShaderSubProgram subProgram)
	{
		ShaderGpuProgramType programType = subProgram.GetProgramType(Version);
		GPUPlatform graphicApi = programType.ToGPUPlatform(Platform);
		ShaderTextExporter exporter = m_exporterInstantiator(graphicApi);
		exporter.Export(this, ref subProgram);
	}
}
