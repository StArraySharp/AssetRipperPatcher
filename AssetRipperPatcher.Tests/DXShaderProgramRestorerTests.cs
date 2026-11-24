using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using AssetRipper.Export.Modules.Shaders;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.Modules.Shaders.ShaderBlob.Parameters;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.GpuProgramType;
using NUnit.Framework;

namespace AssetRipperPatcher.Tests;

/// <summary>
/// <see cref="DXShaderProgramRestorer"/> puts back the RDEF chunk Unity strips.
/// A malformed container here is not a cosmetic issue: the size field is what
/// every reader trusts, so an overshoot makes the dumped binary unusable.
/// </summary>
public class DXShaderProgramRestorerTests
{
    [TestCase(0, "4D9ADDB518FF067DD7C1864819D73FB2")]
    [TestCase(4, "267F56E6DF82B04020822930737C5F43")]
    public void Restored_container_has_a_consistent_size_chunk_table_and_checksum(int extraBytes, string checksum)
    {
        byte[] original = BuildContainerWithoutRdef(extraBytes);
        ShaderSubProgram program = new()
        {
            ProgramType = (int)ShaderGpuProgramType55.DX11VertexSM50,
        };

        using MemoryStream stream = new(original, writable: false);
        using BinaryReader reader = new(stream);
        byte[] restored = DXShaderProgramRestorer.RestoreProgramData(
            reader, UnityVersion.Parse("2021.3.0f1"), ref program);

        uint totalSize = BinaryPrimitives.ReadUInt32LittleEndian(restored.AsSpan(24, 4));
        uint chunkCount = BinaryPrimitives.ReadUInt32LittleEndian(restored.AsSpan(28, 4));
        uint rdefOffset = BinaryPrimitives.ReadUInt32LittleEndian(restored.AsSpan(32, 4));
        uint shdrOffset = BinaryPrimitives.ReadUInt32LittleEndian(restored.AsSpan(36, 4));

        TestContext.Out.WriteLine(
            $"original={original.Length} restored={restored.Length} totalSize={totalSize} " +
            $"chunks={chunkCount} rdef={rdefOffset} shdr={shdrOffset}");

        Assert.Multiple(() =>
        {
            Assert.That(totalSize, Is.EqualTo(restored.Length));
            Assert.That(chunkCount, Is.EqualTo(2u));
            Assert.That(Encoding.ASCII.GetString(restored, (int)rdefOffset, 4), Is.EqualTo("RDEF"));
            Assert.That(Encoding.ASCII.GetString(restored, (int)shdrOffset, 4), Is.EqualTo("SHDR"));
            Assert.That(Convert.ToHexString(restored.AsSpan(4, 16)), Is.EqualTo(checksum));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Restoration_consumes_only_the_declared_Dxbc_container(bool hasRdef)
    {
        ShaderSubProgram program = new() { ProgramType = (int)ShaderGpuProgramType55.DX11VertexSM50 };
        byte[] stripped = BuildContainerWithoutRdef();
        using BinaryReader standalone = new(new MemoryStream(stripped));
        UnityVersion version = UnityVersion.Parse("2021.3.0f1");
        byte[] expected = DXShaderProgramRestorer.RestoreProgramData(standalone, version, ref program);
        byte[] container = hasRdef ? expected : stripped;
        using MemoryStream stream = new([0x11, .. container, 0x54, 0x55]);
        using BinaryReader reader = new(stream);
        stream.Position = 1;

        byte[] restored = DXShaderProgramRestorer.RestoreProgramData(reader, version, ref program);

        Assert.That(restored, Is.EqualTo(expected));
        Assert.That(reader.ReadByte(), Is.EqualTo(0x54));
    }

    [Test]
    public void Rdef_offsets_account_for_UTF8_names_and_empty_buffer_names()
    {
        ShaderSubProgram program = new()
        {
            ProgramType = (int)ShaderGpuProgramType55.DX11VertexSM50,
            ConstantBuffers =
            [
                new() { Name = "缓冲", Size = 8, VectorParams =
                [
                    new("颜色", ShaderParamType.Float, 0, 1),
                    new("alpha", ShaderParamType.Float, 4, 1),
                ] },
                new(),
            ],
            ConstantBufferBindings = [new("缓冲", 4)],
        };
        using BinaryReader reader = new(new MemoryStream(BuildContainerWithoutRdef()));
        byte[] restored = DXShaderProgramRestorer.RestoreProgramData(
            reader, UnityVersion.Parse("2021.3.0f1"), ref program);
        int payload = ValueAt(32) + 8;
        int bindings = payload + ValueAt(payload + 12);
        int buffers = payload + ValueAt(payload + 4);
        int variables = payload + ValueAt(buffers + 8);

        Assert.Multiple(() =>
        {
            Assert.That(ValueAt(24), Is.EqualTo(restored.Length));
            Assert.That(ValueAt(payload), Is.EqualTo(2));
            Assert.That(ValueAt(payload + 8), Is.EqualTo(2));
            Assert.That(NameAt(bindings), Is.EqualTo("缓冲"));
            Assert.That(ValueAt(bindings + 20), Is.EqualTo(4));
            Assert.That(NameAt(buffers), Is.EqualTo("缓冲"));
            Assert.That(NameAt(buffers + 24), Is.Empty);
            Assert.That(NameAt(variables), Is.EqualTo("颜色"));
            Assert.That(NameAt(variables + 40), Is.EqualTo("alpha"));
            Assert.That(ValueAt(payload + ValueAt(variables + 16)), Is.EqualTo(0x00030000));
            Assert.That(NameAt(payload + 24), Is.EqualTo("AssetRipper"));
        });

        int ValueAt(int offset) => BinaryPrimitives.ReadInt32LittleEndian(restored.AsSpan(offset));

        string NameAt(int offset)
        {
            int start = payload + ValueAt(offset);
            return Encoding.UTF8.GetString(restored, start, Array.IndexOf(restored, (byte)0, start) - start);
        }
    }

    private static byte[] BuildContainerWithoutRdef(int extraBytes = 0)
    {
        // 32-byte DXBC header, one 4-byte chunk offset, and one SHDR chunk.
        byte[] container = new byte[52 + extraBytes];
        Encoding.ASCII.GetBytes("DXBC").CopyTo(container, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(20, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(24, 4), (uint)container.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(28, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(32, 4), 36);
        Encoding.ASCII.GetBytes("SHDR").CopyTo(container, 36);
        BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(40, 4), (uint)(8 + extraBytes));
        return container;
    }
}
