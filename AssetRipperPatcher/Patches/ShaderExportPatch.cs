using System.Reflection;
using AssetRipper.Assets;
using AssetRipper.Export;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Export.UnityProjects.Shaders;
using AssetRipper.IO.Files;
using AssetRipperPatcher.Shader.AssetRipper.Export.Modules.Shaders.Exporters;
using HarmonyLib;

namespace AssetRipperPatcher.Patches;

/// <summary>
/// Replaces AssetRipper's placeholder shader export (DummyShaderTextExporter)
/// with the full decompilation.
/// </summary>
internal static class ShaderExportPatch
{
    private static ShaderDisassemblyExporter _disassemblyExporter = new();
    private static ShaderRipperExporter _shaderRipExporter = new();

    public static void Register(Harmony harmony)
    {        Type projectExporterType = typeof(DummyShaderTextExporter);
        MethodInfo? exportShader = projectExporterType.GetMethod("Export",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            [typeof(IExportContainer), typeof(IUnityObjectBase), typeof(string), typeof(FileSystem)],
            null);
        if (exportShader != null)
        {
            harmony.Patch(exportShader,
                prefix: new HarmonyMethod(typeof(ShaderExportPatch).GetMethod(nameof(ExportShader))));
            Console.WriteLine("DummyShaderTextExporter.ExportShader Patched");
            LocalFeatureNotice.ShowOnce("Shader decompilation", "AssetRipperPatcher");
        }
        else
        {
            Console.WriteLine("DummyShaderTextExporter.ExportShader not found");
        }
    }

    /// <summary>
    /// Print the shader export tally once the run finishes, so a silent fallback is
    /// still visible without a per-shader log line.
    /// </summary>
    public static void ReportShaderTally() => ShaderRipperExporter.ReportTally();

    public static bool ExportShader(IExportContainer container,
        IUnityObjectBase asset, string path,
        FileSystem fileSystem, ref bool __result)
    {
        // ShaderRipper first (full decompile), GLSL wrapper as the fallback.
        try
        {
            __result = _shaderRipExporter.Export(container, asset, path, fileSystem);
            if (__result) return false;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[ShaderRipper] export failed, falling back to GLSL: {e.Message}");
        }
        __result = _disassemblyExporter.Export(container, asset, path, fileSystem);
        return false;
    }
}
