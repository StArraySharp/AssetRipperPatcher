using AssetRipper.Assets;
using AssetRipper.Export.Modules.Shaders.Exporters;
using AssetRipper.Export.Modules.Shaders.Exporters.DirectX;
using AssetRipper.Export.Modules.Shaders.IO;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Export.UnityProjects.Shaders;
using AssetRipper.IO.Files;
using AssetRipper.SourceGenerated.Classes.ClassID_48;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader;
using System.IO;
using System.Text;

namespace AssetRipperPatcher.Shader.AssetRipper.Export.Modules.Shaders.Exporters;

public sealed class ShaderDisassemblyExporter : ShaderExporterBase
{
    public override bool Export(IExportContainer container, IUnityObjectBase asset, string path, FileSystem fileSystem)
    {
        using Stream fileStream = fileSystem.File.Create(path);
        ExportBinary((IShader)asset, fileStream, ShaderExporterInstantiator);
        return true;

    }

    private static ShaderTextExporter ShaderExporterInstantiator(GPUPlatform graphicApi)
    {
        switch (graphicApi)
        {
            case GPUPlatform.D3D9:
            case GPUPlatform.D3D11:
            case GPUPlatform.D3D11_9x:
                return new ShaderDXExporter(graphicApi);
            case GPUPlatform.Vulkan:
                return new ShaderUnknownExporter(graphicApi);
            case GPUPlatform.None:
            case GPUPlatform.Gles3x:
            case GPUPlatform.GlCore:
                return new ShaderGLESExporter();
            case GPUPlatform.Metal:
                return new ShaderMetalExporter();
            case GPUPlatform.Unknown:
                return new ShaderTextExporter();
            default:
                return new ShaderUnknownExporter(graphicApi);
        }
    }

    private static void ExportBinary(IShader shader, Stream stream, Func<GPUPlatform, ShaderTextExporter> exporterInstantiator)
    {
        Console.WriteLine($"反编译着色器: {shader.Name}");
        
        // 写入 GLSLPROGRAM ShaderLab 包装头
        using (StreamWriter wrapper = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true))
        {
            wrapper.WriteLine($"Shader \"{shader.Name}\" {{");
            wrapper.WriteLine("    Properties {");
            wrapper.WriteLine("        _MainTex (\"Texture\", 2D) = \"white\" {}");
            wrapper.WriteLine("    }");
            wrapper.WriteLine("    SubShader {");
            wrapper.WriteLine("        Tags { \"RenderType\"=\"Opaque\" }");
            wrapper.WriteLine("        Pass {");
            wrapper.WriteLine("            GLSLPROGRAM");
        }

        // 调用原始逻辑写入 GLSL 反编译内容（跳过原始 ShaderLab header）
        ExportBinary_Original(shader, stream, exporterInstantiator, skipHeader: true);

        // 写入 GLSLPROGRAM ShaderLab 包装尾
        using (StreamWriter wrapper = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true))
        {
            wrapper.WriteLine();
            wrapper.WriteLine("            ENDGLSL");
            wrapper.WriteLine("        }");
            wrapper.WriteLine("    }");
            wrapper.WriteLine("}");
        }
    }

    /// <summary>
    /// 原始导出逻辑（备份），skipHeader 为 true 时只写 sub-program 内容，不写 ShaderLab header
    /// </summary>
    private static void ExportBinary_Original(IShader shader, Stream stream, Func<GPUPlatform, ShaderTextExporter> exporterInstantiator, bool skipHeader = false)
    {
        if (shader.Has_ParsedForm())
        {
            using (ShaderWriter writer = new ShaderWriter(stream, shader, exporterInstantiator))
            {
                shader.ParsedForm.Export(writer);
                return;
            }
        }
        if (shader.Has_CompressedBlob())
        {
            using (ShaderWriter writer2 = new ShaderWriter(stream, shader, exporterInstantiator))
            {
                string header = shader.Script.String;
                if (!skipHeader)
                {
                    if (writer2.Blobs.Length == 0)
                    {
                        writer2.Write(header);
                    }
                    else
                    {
                        writer2.Blobs[0].Export(writer2, header);
                    }
                }
                else
                {
                    // 只写 sub-program 内容，不写 header，直接遍历 entry 导出
                    foreach (var blob in writer2.Blobs)
                    {
                        for (uint e = 0; e < blob.Entries.Length; e++)
                        {
                            blob.GetSubProgram(e).Export(writer2, ShaderType.Vertex);
                        }
                    }
                }
                return;
            }
        }
        
        using BinaryWriter writer3 = new BinaryWriter(stream);
        writer3.Write(shader.Script.Data);
    }
}