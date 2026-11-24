using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using AssetRipper.Export.Modules.Shaders;
using AssetRipper.Export.Modules.Shaders.Exporters.DirectX;
using AssetRipper.Export.Modules.Shaders.Extensions;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.IO.Files;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated.Classes.ClassID_48;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.GpuProgramType;
using ShaderRipper.Data;

namespace AssetRipperPatcher.Shader.AssetRipper.Export.Modules.Shaders.Exporters;

/// <summary>
/// Writes the original compiled programs beside the decompiled <c>.shader</c>.
/// </summary>
/// <remarks>
/// <para>This is the in-process equivalent of ShaderRipper's <c>--orig-asm</c>:
/// the <c>.shader</c> stays the deliverable, and every payload the build actually
/// shipped is kept next to it for comparison or for tools that need the bytecode.</para>
///
/// <para>DXBC is written as a binary container with its <c>RDEF</c> reflection
/// chunk restored. Unity strips RDEF when it builds the game, which is why a raw
/// dump cannot tell a reader that <c>cb1</c> is <c>UnityPerMaterial</c>; the
/// ShaderRipper resolves bindings while translating the program. AssetRipper's
/// parameter-blob reflection is the fallback when translation does not cover a slot.</para>
///
/// <para>GL-family payloads are text already, so they are decoded and written as
/// <c>.glsl</c>. Metal, Vulkan and console payloads have no readable form on this
/// path and are skipped.</para>
/// </remarks>
internal static class ShaderAsmExporter
{
    private static int _dxbc, _glsl, _failed, _shaders;

    /// <summary>
    /// Write every program of <paramref name="shader"/> under
    /// <c>&lt;shader directory&gt;/&lt;shader name&gt;_asm/&lt;platform&gt;/</c>.
    /// </summary>
    /// <returns>How many DXBC and GLSL files were written.</returns>
    public static (int Dxbc, int Glsl) Export(IShader shader, string shaderPath, FileSystem fileSystem,
        IReadOnlyDictionary<(int Platform, int BlobIndex), byte[]>? dxbcPrograms)
    {
        if (string.IsNullOrEmpty(shader.Name))
        {
            return (0, 0);
        }

        ShaderSubProgramBlob[] blobs;
        try
        {
            blobs = shader.ReadBlobs();
        }
        catch (Exception e)
        {
            Fail(shader.Name, "blob read", e);
            return (0, 0);
        }
        if (blobs.Length == 0)
        {
            return (0, 0);
        }

        string? directory = fileSystem.Path.GetDirectoryName(shaderPath);
        if (string.IsNullOrEmpty(directory))
        {
            return (0, 0);
        }

        string stem = fileSystem.Path.GetFileNameWithoutExtension(shaderPath);
        string root = fileSystem.Path.Join(directory, stem + "_asm");
        UnityVersion version = shader.Collection.Version;
        var platforms = shader.Platforms;
        if (platforms is null)
        {
            return (0, 0);
        }
        var paramIndices = ShaderModelBridge.GetParameterBlobIndices(shader);
        int platformCount = Math.Min(blobs.Length, platforms.Count);
        int dxbc = 0;
        int glsl = 0;

        for (int i = 0; i < platformCount; i++)
        {
            ShaderSubProgramBlob blob = blobs[i];
            if (blob is null || blob.Entries.Length == 0)
            {
                continue;
            }

            GPUPlatform platform = (GPUPlatform)platforms[i];
            string platformDirectory = fileSystem.Path.Join(root, PlatformName(platform));

            for (uint entry = 0; entry < blob.Entries.Length; entry++)
            {
                ShaderSubProgram subProgram;
                try
                {
                    subProgram = blob.GetSubProgramData(entry);
                }
                catch
                {
                    continue; // Parameter-only entries do not have a program header.
                }

                byte[] program = subProgram.ProgramData;
                if (program.Length == 0)
                {
                    continue;
                }

                ShaderGpuProgramType programType;
                try
                {
                    programType = subProgram.GetProgramType(version);
                }
                catch
                {
                    continue;
                }

                if (!programType.IsDX9()
                    && TryGetDxbcContainer(program, platform, version, out byte[] container))
                {
                    byte[] output = dxbcPrograms != null
                        && dxbcPrograms.TryGetValue(((int)platform, (int)entry), out byte[]? resolved)
                        && DxbcContainer.HasSameProgram(container, resolved)
                        ? resolved
                        : RestoreRdef(container, version, blob, entry,
                            paramIndices.GetValueOrDefault((subProgram.ProgramType, entry), -1), shader.Name);
                    string file = fileSystem.Path.Join(platformDirectory,
                        $"{entry}_{StageName(programType)}.dxbc");
                    if (TryWrite(fileSystem, file, output, shader.Name))
                    {
                        dxbc++;
                    }
                }
                else if (IsGlsl(platform, programType))
                {
                    string text = DecodeText(program);
                    if (text.Length == 0)
                    {
                        continue;
                    }

                    string file = fileSystem.Path.Join(platformDirectory, $"{entry}_glsl.glsl");
                    if (TryWrite(fileSystem, file, Encoding.UTF8.GetBytes(text), shader.Name))
                    {
                        glsl++;
                    }
                }
            }
        }

        Interlocked.Increment(ref _shaders);
        Interlocked.Add(ref _dxbc, dxbc);
        Interlocked.Add(ref _glsl, glsl);
        return (dxbc, glsl);
    }

    /// <summary>Called once when the export finishes, so the flag leaves a trace.</summary>
    public static void ReportTally()
    {
        Console.WriteLine($"[ShaderAsm] export tally: shaders={_shaders} dxbc={_dxbc} " +
                          $"glsl={_glsl} failed={_failed}");
    }

    /// <summary>
    /// The DXBC container inside a program payload.  Unity prefixes D3D11 programs
    /// with a small per-program header; D3D9 has no container at all.
    /// </summary>
    private static bool TryGetDxbcContainer(byte[] program, GPUPlatform platform,
        UnityVersion version, out byte[] container)
    {
        container = Array.Empty<byte>();
        int offset = 0;
        if (DXDataHeader.HasHeader(platform) && program.Length > 0)
        {
            offset = DXDataHeader.GetDataOffset(version, platform, program[0]);
        }

        if (!StartsWithDxbc(program, offset))
        {
            // Unity has shipped a few header layouts over the years, and console
            // slots borrow the same DXBC container.  The magic is the one stable
            // marker, so fall back to a short scan before rejecting the payload.
            const int ScanLength = 64;
            int end = Math.Min(program.Length, ScanLength);
            offset = -1;
            for (int i = 0; i + 4 <= end; i++)
            {
                if (StartsWithDxbc(program, i))
                {
                    offset = i;
                    break;
                }
            }
            if (offset < 0)
            {
                return false;
            }
        }

        if (program.Length - offset < 32)
            return false;
        int length = BinaryPrimitives.ReadInt32LittleEndian(program.AsSpan(offset + 24));
        if (length < 32 || length > program.Length - offset)
            return false;
        container = program.AsSpan(offset, length).ToArray();
        return true;
    }

    private static bool StartsWithDxbc(byte[] data, int offset)
    {
        return offset >= 0
            && offset + 4 <= data.Length
            && data[offset] == (byte)'D'
            && data[offset + 1] == (byte)'X'
            && data[offset + 2] == (byte)'B'
            && data[offset + 3] == (byte)'C';
    }

    private static byte[] RestoreRdef(byte[] container, UnityVersion version,
        ShaderSubProgramBlob blob, uint entry, int parameterIndex, string shaderName)
    {
        if (DxbcContainer.HasChunk(container, "RDEF"))
            return container;

        ShaderSubProgram subProgram;
        try
        {
            subProgram = parameterIndex >= 0
                ? blob.GetSubProgram(entry, (uint)parameterIndex)
                : blob.GetSubProgram(entry);
        }
        catch
        {
            // Without readable asset reflection, keep the original container rather
            // than adding an empty RDEF that masks a reader's bytecode fallback.
            return container;
        }

        try
        {
            using MemoryStream stream = new(container, writable: false);
            using BinaryReader reader = new(stream);
            return DXShaderProgramRestorer.RestoreProgramData(reader, version, ref subProgram);
        }
        catch (Exception e)
        {
            // A payload we cannot reflect (a DXIL container with no SM4/5 program
            // type, or one whose parameter table is incomplete) is still worth
            // keeping: the binary itself is readable even without the chunk.
            Fail(shaderName, "RDEF restore", e);
            return container;
        }
    }

    private static bool IsGlsl(GPUPlatform platform, ShaderGpuProgramType programType)
    {
        return (platform.IsOpenGL() || programType.IsGL())
            && programType is not (ShaderGpuProgramType.MetalVS or ShaderGpuProgramType.MetalFS);
    }

    /// <summary>
    /// Decode a text payload, dropping the NUL/0xFF padding Unity leaves at the end.
    /// Only the tail is trimmed; the middle is kept byte-for-byte, because a comment
    /// in the original source can contain anything.
    /// </summary>
    private static string DecodeText(byte[] data)
    {
        int start = 0;
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            start = 3;
        }

        int end = data.Length;
        while (end > start)
        {
            byte b = data[end - 1];
            if (b == 0 || b == 0xFF || (b < 0x20 && b != (byte)'\n' && b != (byte)'\r' && b != (byte)'\t'))
            {
                end--;
            }
            else
            {
                break;
            }
        }

        return end > start ? Encoding.UTF8.GetString(data, start, end - start) : string.Empty;
    }

    private static string StageName(ShaderGpuProgramType programType) => programType switch
    {
        ShaderGpuProgramType.DX9VertexSM20 or ShaderGpuProgramType.DX9VertexSM30
            or ShaderGpuProgramType.DX10Level9Vertex
            or ShaderGpuProgramType.DX11VertexSM40 or ShaderGpuProgramType.DX11VertexSM50
            or ShaderGpuProgramType.MetalVS or ShaderGpuProgramType.ConsoleVS => "vs",

        ShaderGpuProgramType.DX9PixelSM20 or ShaderGpuProgramType.DX9PixelSM30
            or ShaderGpuProgramType.DX10Level9Pixel
            or ShaderGpuProgramType.DX11PixelSM40 or ShaderGpuProgramType.DX11PixelSM50
            or ShaderGpuProgramType.MetalFS or ShaderGpuProgramType.ConsoleFS => "ps",

        ShaderGpuProgramType.DX11GeometrySM40 or ShaderGpuProgramType.DX11GeometrySM50
            or ShaderGpuProgramType.ConsoleGS => "gs",

        ShaderGpuProgramType.DX11HullSM50 or ShaderGpuProgramType.ConsoleHS => "hs",
        ShaderGpuProgramType.DX11DomainSM50 or ShaderGpuProgramType.ConsoleDS => "ds",
        ShaderGpuProgramType.RayTracing => "rt",
        _ => "program",
    };

    private static string PlatformName(GPUPlatform platform)
    {
        return platform.ToString().ToLowerInvariant();
    }

    private static bool TryWrite(FileSystem fileSystem, string path, byte[] data, string shaderName)
    {
        try
        {
            fileSystem.Directory.Create(fileSystem.Path.GetDirectoryName(path));
            using Stream stream = fileSystem.File.Create(path);
            stream.Write(data);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Fail(shaderName, $"sidecar write ({path})", e);
            return false;
        }
    }

    internal static void Fail(string shader, string stage, Exception e)
    {
        if (Interlocked.Increment(ref _failed) <= 10)
        {
            Console.WriteLine($"[ShaderAsm] {shader}: {stage} failed: {e.GetType().Name}: {e.Message}");
        }
    }
}
