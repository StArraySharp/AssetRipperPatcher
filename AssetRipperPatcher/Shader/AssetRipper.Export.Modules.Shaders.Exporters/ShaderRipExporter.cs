using System.Text;
using AssetRipper.Assets;
using AssetRipper.Export.Modules.Shaders.Exporters;
using AssetRipper.Export.Modules.Shaders.Exporters.DirectX;
using AssetRipper.Export.Modules.Shaders.Exporters.USCDirectX;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Export.UnityProjects.Shaders;
using AssetRipper.IO.Files;
using AssetRipper.SourceGenerated.Classes.ClassID_48;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader;
using ShaderRipper;

namespace AssetRipperPatcher.Shader.AssetRipper.Export.Modules.Shaders.Exporters;

/// <summary>
/// Bridges AssetRipper to the ShaderRipper decompiler, in process.
/// </summary>
/// <remarks>
/// The library is referenced directly (see the ProjectReference in the csproj), so
/// nothing is spawned and no files are staged: the asset's own blob goes to
/// <see cref="ShaderDecompiler.Translate"/> and the returned ShaderLab text is
/// written straight to the export stream.
///
/// If the library cannot produce a file -- an unsupported backend, a payload that
/// does not decode, or a ShaderLab body the grammar rejects -- the export falls
/// back to <see cref="ShaderDisassemblyExporter"/>, which at least yields the raw
/// GLSL in a GLSLPROGRAM wrapper.
/// </remarks>
public sealed class ShaderRipperExporter : ShaderExporterBase
{
    private static ShaderDisassemblyExporter? _fallbackExporter;
    private static int _reported;

    public override bool Export(IExportContainer container, IUnityObjectBase asset,
                                string path, FileSystem fileSystem)
    {
            Interlocked.Increment(ref _calls);
            if (TryProduce(asset, out string? text))
            {
                using Stream stream = fileSystem.File.Create(path);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(text);
                Interlocked.Increment(ref _ok);
                return true;
            }

            Interlocked.Increment(ref _fallback);
            return Fallback(container, asset, path, fileSystem);
        }

        private static int _calls, _ok, _fallback, _nullModel, _threw;

        /// <summary>
        /// 把 shader 反编译成 ShaderLab 文本，供 UI 预览使用（不落盘）。
        /// 与 <see cref="Export"/> 走同一个 <see cref="TryProduce"/>，
        /// 所以预览看到的内容与导出到磁盘的文件逐字节一致。
        /// </summary>
        /// <returns>成功时返回文本；两条路径都失败时返回 null。</returns>
        public static string? TryDecompile(IUnityObjectBase asset)
        {
            return TryProduce(asset, out string? text) ? text : null;
        }

        /// <summary>
        /// The one place the shader text is produced, shared by export and preview.
        /// ShaderRipper first (full decompile); on failure the GLSL disassembly is
        /// rendered into memory as the fallback, matching what
        /// <see cref="Fallback"/> would have written to disk.
        /// </summary>
        private static bool TryProduce(IUnityObjectBase asset, out string? text)
        {
            text = null;
            if (asset is not IShader shader)
                return false;

            try
            {
                ShaderModel? model = ShaderModelBridge.From(shader, PlatformExporter);
                if (model == null)
                {
                    Interlocked.Increment(ref _nullModel);
                }
                else
                {
                    ShaderResult result = ShaderDecompiler.Translate(model);
                    if (result.Ok && !string.IsNullOrEmpty(result.ShaderLab))
                    {
                        text = result.ShaderLab;
                        return true;
                    }
                    if (result.Notes.Count > 0 && _reported++ < 10)
                        Console.WriteLine($"[ShaderRipper] {shader.Name}: {string.Join("; ", result.Notes)}");
                }
        }
            catch (Exception e)
            {
                Interlocked.Increment(ref _threw);
                Console.WriteLine($"[ShaderRipper] {shader.Name}: {e.GetType().Name}: {e.Message}");
            }

            // 退回反汇编：把 GLSL 包在 GLSLPROGRAM 里，至少能看到真实程序。
            try
            {
                using MemoryStream stream = new();
                new ShaderDisassemblyExporter().ExportRawText(shader, stream);
                string raw = Encoding.UTF8.GetString(stream.ToArray());
                if (string.IsNullOrWhiteSpace(raw)) return false;
                text = raw;
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ShaderRipper] disassembly failed for {shader.Name}: {e.GetType().Name}: {e.Message}");
                return false;
            }
        }

            /// <summary>Called once the export finishes, so a silent failure is still visible.</summary>
            public static void ReportTally()
            {
                Console.WriteLine($"[ShaderRipper] export tally: calls={_calls} ok={_ok} " +
                                  $"fallback={_fallback} nullModel={_nullModel} threw={_threw}");
            }

    /// <summary>
    /// The text exporters <see cref="ShaderWriter"/> needs.  They are never used to
    /// emit anything here -- this class reads only the decompressed program bytes --
    /// but the writer requires a factory, and reusing the standard one keeps the
    /// blob layout identical to AssetRipper's own path.
    /// </summary>
    private static ShaderTextExporter PlatformExporter(GPUPlatform platform) => platform switch
    {
        GPUPlatform.D3D9 or GPUPlatform.D3D11 or GPUPlatform.D3D11_9x =>
            new ShaderDXExporter(platform),
        GPUPlatform.Vulkan => new ShaderUnknownExporter(platform),
        GPUPlatform.Gles3x or GPUPlatform.GlCore or GPUPlatform.None =>
            new ShaderGLESExporter(),
        GPUPlatform.Metal => new ShaderMetalExporter(),
        _ => new ShaderUnknownExporter(platform),
    };

    private static bool Fallback(IExportContainer container, IUnityObjectBase asset,
                                 string path, FileSystem fileSystem)
    {
        _fallbackExporter ??= new ShaderDisassemblyExporter();
        return _fallbackExporter.Export(container, asset, path, fileSystem);
    }
}
