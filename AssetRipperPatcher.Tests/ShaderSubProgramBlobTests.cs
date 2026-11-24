using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.Assets.Generics;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.GpuProgramType;
using K4os.Compression.LZ4;
using NUnit.Framework;

namespace AssetRipperPatcher.Tests;

public class ShaderSubProgramBlobTests
{
    private const int ProgramVersion = 202012090;
    private static readonly byte[] ProgramData = [0x44, 0x58, 0x42, 0x43];

    [Test]
    public void Split_program_reads_parameters_from_their_own_segment()
    {
        byte[] code = MakeEntry(writer => WriteProgram(writer));
        byte[] parameters = MakeEntry(writer =>
        {
            writer.Write(ProgramVersion);
            writer.Write(2); // Global parameters and one named constant buffer.
            WriteBuffer(writer, "", 0);
            WriteBuffer(writer, "UnityPerMaterial", 16);
            writer.Write(0); // Resource bindings.
        });
        byte[] firstSegment = MakeEntry(writer =>
        {
            writer.Write(2); // Entry count.
            WriteDescriptor(writer, 28, code.Length, 0);
            WriteDescriptor(writer, 0, parameters.Length, 1);
            writer.Write(code);
        });
        ShaderSubProgramBlob blob = ReadSegments(firstSegment, parameters);

        ShaderSubProgram program = blob.GetSubProgram(0, 1);

        Assert.That(program.ProgramData, Is.EqualTo(ProgramData));
        Assert.That(program.ConstantBuffers.Single().Name, Is.EqualTo("UnityPerMaterial"));
    }

    [Test]
    public void Payload_only_read_ignores_broken_trailing_reflection_but_rejects_entry_overrun()
    {
        byte[] code = MakeEntry(writer =>
        {
            WriteProgram(writer);
            writer.Write(int.MinValue); // Invalid constant-buffer count.
        });
        byte[] segment = MakeEntry(writer =>
        {
            writer.Write(1);
            WriteDescriptor(writer, 16, code.Length, 0);
            writer.Write(code);
        });
        ShaderSubProgramBlob blob = ReadSegments(segment);

        Assert.That(blob.GetSubProgramData(0).ProgramData, Is.EqualTo(ProgramData));
        blob.Entries[0].Length -= 8; // The final program field now lies beyond the declared entry.
        Assert.Throws<InvalidDataException>(() => blob.GetSubProgramData(0));
    }

    private static ShaderSubProgramBlob ReadSegments(params byte[][] segments)
    {
        using MemoryStream compressed = new();
        AssetList<uint> offsets = new();
        AssetList<uint> compressedLengths = new();
        AssetList<uint> decompressedLengths = new();
        foreach (byte[] source in segments)
        {
            offsets.Add((uint)compressed.Position);
            byte[] target = new byte[LZ4Codec.MaximumOutputSize(source.Length)];
            int length = LZ4Codec.Encode(source, 0, source.Length, target, 0, target.Length);
            Assert.That(length, Is.Positive);
            compressed.Write(target.AsSpan(0, length));
            compressedLengths.Add((uint)length);
            decompressedLengths.Add((uint)source.Length);
        }

        ProcessedAssetCollection collection = new(new GameBundle());
        collection.SetLayout(UnityVersion.Parse("6000.0.0f1"));
        ShaderSubProgramBlob blob = new();
        blob.Read(collection, compressed.ToArray(), offsets, compressedLengths, decompressedLengths);
        return blob;
    }

    private static byte[] MakeEntry(Action<BinaryWriter> write)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        write(writer);
        return stream.ToArray();
    }

    private static void WriteDescriptor(BinaryWriter writer, int offset, int length, int segment)
    {
        writer.Write(offset);
        writer.Write(length);
        writer.Write(segment);
    }

    private static void WriteProgram(BinaryWriter writer)
    {
        writer.Write(ProgramVersion);
        writer.Write((int)ShaderGpuProgramType55.DX11VertexSM50);
        for (int i = 0; i < 4; i++)
        {
            writer.Write(0); // Instruction statistics.
        }
        writer.Write(0); // Keyword count.
        writer.Write(ProgramData.Length);
        writer.Write(ProgramData);
        writer.Write(0); // Source map.
        writer.Write(0); // Vertex bindings.
    }

    private static void WriteBuffer(BinaryWriter writer, string name, int size)
    {
        writer.Write(name.Length); // The fixture uses ASCII names.
        writer.Write(System.Text.Encoding.ASCII.GetBytes(name));
        while (writer.BaseStream.Position % 4 != 0)
        {
            writer.Write((byte)0);
        }
        writer.Write(size);
        writer.Write(0); // Numeric parameter count.
        writer.Write(0); // Struct parameter count.
    }
}
