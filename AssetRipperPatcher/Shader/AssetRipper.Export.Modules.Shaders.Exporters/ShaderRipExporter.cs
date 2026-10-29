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
        if (asset is not IShader shader)
            return Fallback(container, asset, path, fileSystem);

        Interlocked.Increment(ref _calls);
        ShaderResult? result = null;
        try
        {
            ShaderModel? model = ShaderModelBridge.From(shader, PlatformExporter);
            if (model != null) result = ShaderDecompiler.Translate(model);
            else Interlocked.Increment(ref _nullModel);
        }
        catch (Exception e)
        {
            Interlocked.Increment(ref _threw);
            Console.WriteLine($"[ShaderRipper] {shader.Name}: {e.GetType().Name}: {e.Message}");
        }

        if (result is { Ok: true })
        {
            using Stream stream = fileSystem.File.Create(path);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(result.ShaderLab);
            Interlocked.Increment(ref _ok);
            return true;
        }

        Interlocked.Increment(ref _fallback);
        if (result != null && result.Notes.Count > 0 && _reported++ < 10)
            Console.WriteLine($"[ShaderRipper] {shader.Name}: {string.Join("; ", result.Notes)}");

        return Fallback(container, asset, path, fileSystem);
    }

    private static int _calls, _ok, _fallback, _nullModel, _threw;

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
