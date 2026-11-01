using AssetRipper.Assets;
using AssetRipper.Export;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Export.UnityProjects.Shaders;
using AssetRipper.IO.Files;
using AssetRipperPatcher.Patching;
using AssetRipperPatcher.Shader.AssetRipper.Export.Modules.Shaders.Exporters;

namespace AssetRipperPatcher.Patches;

/// <summary>
/// Replaces AssetRipper's placeholder shader export (DummyShaderTextExporter)
/// with the full decompilation.
/// </summary>
internal static class ShaderExportPatch
{
    private static ShaderDisassemblyExporter _disassemblyExporter = new();
    private static ShaderRipperExporter _shaderRipExporter = new();

    /// <summary>
    /// Print the shader export tally once the run finishes, so a silent fallback is
    /// still visible without a per-shader log line.
    /// </summary>
    public static void ReportShaderTally() => ShaderRipperExporter.ReportTally();

    // 候选集含 BinaryAssetExporter 基类链上的同名 Export 重载。4 参 bool 组中，
    // 按 declaringType canonical 排序 BinaryAssetExporter（基类）在前，
    // DummyShaderTextExporter（override）是 index 1。
    [PatchMethod(typeof(DummyShaderTextExporter), "Export", OverrideIndex = 1, PatchType = PatchType.Prefix)]
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
