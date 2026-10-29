using System.Linq;
using System.Reflection;
using AssetRipper.Assets.Generics;
using AssetRipper.Export.Modules.Shaders;
using AssetRipper.Export.Modules.Shaders.Exporters;
using AssetRipper.Export.Modules.Shaders.IO;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.SourceGenerated.Classes.ClassID_48;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader;
using AssetRipper.SourceGenerated.Subclasses.SerializedPass;
using AssetRipper.SourceGenerated.Subclasses.SerializedProgram;
using AssetRipper.SourceGenerated.Subclasses.SerializedTagMap;
using ShaderRipper;

namespace AssetRipperPatcher.Shader.AssetRipper.Export.Modules.Shaders.Exporters;

/// <summary>
/// Maps an AssetRipper <see cref="IShader"/> onto the decompiler's public model.
/// </summary>
/// <remarks>
/// The data comes from where AssetRipper's own shader exporters read it:
/// <see cref="ShaderWriter"/>'s blobs, which decompress the payload and parse the
/// per-program reflection.  That yields <see cref="ShaderSubProgram.ProgramData"/> --
/// the raw DXBC or GLSL -- plus the constant-buffer and parameter tables, which is
/// exactly what the translator needs and all the reflection it would otherwise have
/// to guess.
/// </remarks>
internal static class ShaderModelBridge
{
    /// <summary>
    /// One payload per platform, in the shader's own platform order.  Returns null
    /// when there is nothing to translate.
    /// </summary>
    public static ShaderModel? From(IShader shader, Func<GPUPlatform, ShaderTextExporter> exporters)
    {
        if (string.IsNullOrEmpty(shader.Name)) return null;

        var model = new ShaderModel { Name = shader.Name };
        ReadHeader(shader, model);

        ShaderSubProgramBlob[] blobs;
        try
        {
            using var stream = new MemoryStream();
            var writer = new ShaderWriter(stream, shader, exporters);
            blobs = writer.Blobs;
        }
        catch (Exception e)
        {
            Diag($"{shader.Name}: blob read failed: {e.Message}");
            return null;
        }

        for (int i = 0; i < blobs.Length && i < shader.Platforms.Count; i++)
        {
            ShaderSubProgramBlob blob = blobs[i];
            if (blob.Entries.Length == 0) continue;

            uint[] offsets = PlatformTable(shader, i, "Offsets");
            if (offsets.Length == 0)
            {
                // No offset table: the programs are already decompressed and each is a
                // complete container.  Concatenate them into one payload per platform
                // and let the library's DXBC scanner carve them back out -- that path
                // works on any stream of containers, which is exactly what this is.
                var joined = new MemoryStream();
                for (uint e = 0; e < blob.Entries.Length; e++)
                {
                    ShaderSubProgram sub = blob.GetSubProgram(e);
                    if (sub?.ProgramData is not { Length: > 0 } program) continue;
                    joined.Write(program, 0, program.Length);
                }
                if (joined.Length == 0) continue;

                model.Payloads.Add(new ShaderPayload
                {
                    Platform = (int)shader.Platforms[i],
                    Blob = joined.ToArray(),
                    RawPrograms = true,
                });
                continue;
            }

            // Hand over Unity's own layout rather than the decompressed programs:
            // the library's slot decoder expects `m_CompressedBlob` plus the
            // per-platform offset/length tables, and it re-derives each program's
            // header from that stream.  Concatenating ProgramData would lose the
            // slot table and every program would fail to parse.
            model.Payloads.Add(new ShaderPayload
            {
                Platform = (int)shader.Platforms[i],
                Blob = shader.CompressedBlob,
                Offsets = offsets,
                CompressedLengths = PlatformTable(shader, i, "CompressedLengths"),
                DecompressedLengths = PlatformTable(shader, i, "DecompressedLengths"),
            });
        }

        ReadStructure(shader, model);

        Diag($"{model.Name}: blobs={blobs.Length} payloads={model.Payloads.Count} " +
             $"props={model.Properties.Count} subShaders={model.SubShaders.Count} " +
             $"passes={model.SubShaders.Sum(s => s.Passes.Count)}");

        // A shader with no program still has a structure worth writing, so the
        // model only fails when there is neither.
        return model.Payloads.Count > 0 || model.SubShaders.Count > 0 ? model : null;
    }

    /// <summary>
    /// The ShaderLab structure: sub-shaders, their tags, and each pass with its
    /// name, tags, render state and sub-program keyword sets.
    ///
    /// The state is rendered by AssetRipper's own writer instead of being mapped
    /// field by field.  That writer already reproduces every block shape
    /// (<c>Stencil { }</c>, the eight <c>Blend</c> slots, property references like
    /// <c>Blend [_SrcBlend] [_DstBlend]</c>), so reading its output keeps this
    /// bridge from having to mirror the whole serialized state -- and from silently
    /// dropping a state the day AssetRipper adds one.
    /// </summary>
    private static void ReadStructure(IShader shader, ShaderModel model)
    {
        if (!shader.Has_ParsedForm()) return;

        foreach (var ss in shader.ParsedForm.SubShaders)
        {
            var sub = new ShaderSubShader { LOD = ss.LOD };
            ReadTags(ss.Tags, sub.Tags);

            foreach (var p in ss.Passes)
            {
                var pass = new ShaderPass { Name = p.Name ?? "" };
                ReadTags(p.Tags, pass.Tags);
                ReadNameIndices(p, pass);
                ReadState(p, pass, model.Name);
                ReadSubPrograms(p, pass);
                sub.Passes.Add(pass);
            }

            model.SubShaders.Add(sub);
        }
    }

    /// <summary>
    /// The pass's own sub-program table: which blob slot each program uses, and the
    /// keywords it was compiled with.  Those indices index the shader's keyword
    /// table, which is what the variant planner reads.
    /// </summary>
    private static void ReadSubPrograms(ISerializedPass pass, ShaderPass into)
    {
        foreach (var program in Programs(pass))
        {
            if (program == null) continue;
            try
            {
                if (program.Has_PlayerSubPrograms())
                {
                    var players = program.GetPlayerSubPrograms();
                    Diag($"{pass.Name}: stage players={players.Count} " + string.Join(" ", players.Select(x =>
                        x.BlobIndex + "[" + string.Join("/", x.KeywordIndices) + "]")));
                    foreach (var sp in players)
                        AddSubProgram(into, sp.BlobIndex, sp.KeywordIndices);
                }
                else
                {
                    foreach (var sp in program.SubPrograms)
                        AddSubProgram(into, sp.BlobIndex, sp.KeywordIndices);
                }
            }
            catch (Exception e)
            {
                Diag($"{pass.Name}: sub-program table unreadable: {e.Message}");
            }
        }
    }

    private static IEnumerable<ISerializedProgram> Programs(ISerializedPass pass)
    {
        yield return pass.ProgVertex;
        yield return pass.ProgFragment;
        yield return pass.ProgGeometry;
        yield return pass.ProgHull;
        yield return pass.ProgDomain;
        yield return pass.ProgRayTracing;
    }

    private static void AddSubProgram(ShaderPass into, uint blobIndex, IEnumerable<ushort> keywords)
    {
        var info = new ShaderSubProgramInfo { BlobIndex = (int)blobIndex };
        if (keywords != null)
            foreach (ushort k in keywords) info.KeywordIndices.Add(k);

        // Kept in the asset's own order, one entry per stage slot.  Two stages can
        // name the same blob slot with *different* keyword sets (a GLSL pair shares
        // an index), so merging them would invent a combined set that the build
        // never made and drop two real ones.  The reader of this table resolves a
        // slot to its last entry, exactly as the serialized-data path does.
        into.SubPrograms.Add(info);
    }

    /// <summary>
    /// The pass render state, read as number-plus-property-name.
    ///
    /// AssetRipper's own state writer is not usable here: it prints the numeric
    /// value, so a state driven by a material property (<c>Cull [_CullMode]</c>,
    /// <c>ZTest [unity_GUIZTestMode]</c>) comes out as a frozen number, which is a
    /// different shader.  Every state entry is stored as <c>{val, name}</c>, so both
    /// halves are read and the library emits the name whenever there is one.
    /// </summary>
    private static void ReadState(ISerializedPass pass, ShaderPass into, string shaderName)
    {
        object? state = pass.State;
        if (state == null) return;
        try
        {
            if (string.IsNullOrEmpty(into.Name) && Child(state, "name")?.ToString() is { Length: > 0 } stateName)
                into.Name = stateName;
            if (into.Tags.Count == 0 && Child(state, "tags") is SerializedTagMap stateTags)
                ReadTags(stateTags, into.Tags);

            Take(state, "zClip", (v, n) => { into.ZClip = v; into.ZClipName = n; });
            Take(state, "zTest", (v, n) => { into.ZTest = v; into.ZTestName = n; });
            Take(state, "zWrite", (v, n) => { into.ZWrite = v; into.ZWriteName = n; });
            Take(state, "culling", (v, n) => { into.Culling = v; into.CullingName = n; });
            Take(state, "offsetFactor", (v, n) => { into.OffsetFactor = v; into.OffsetFactorName = n; });
            Take(state, "offsetUnits", (v, n) => { into.OffsetUnits = v; into.OffsetUnitsName = n; });
            Take(state, "alphaToMask", (v, n) => { into.AlphaToMask = v; into.AlphaToMaskName = n; });
            Take(state, "stencilRef", (v, n) => { into.StencilRef = v; into.StencilRefName = n; });
            Take(state, "stencilReadMask", (v, n) => { into.StencilReadMask = v; into.StencilReadMaskName = n; });
            Take(state, "stencilWriteMask", (v, n) => { into.StencilWriteMask = v; into.StencilWriteMaskName = n; });
            Take(state, "fogMode", (v, _) => into.FogMode = (int)v);
            Take(state, "fogDensity", (v, _) => into.FogDensity = v);
            Take(state, "fogStart", (v, _) => into.FogStart = v);
            Take(state, "fogEnd", (v, _) => into.FogEnd = v);
            Take(state, "lighting", (v, _) => into.Lighting = v != 0);
            Take(state, "rtSeparateBlend", (v, _) => into.RtSeparateBlend = v != 0);

            for (int i = 0; i < 8; i++)
            {
                object? rt = Child(state, "rtBlend" + i);
                if (rt == null) continue;
                var b = new ShaderBlendState();
                Take(rt, "sourceBlend", (v, n) => { b.SrcBlend = v; b.SrcBlendName = n; });
                Take(rt, "destinationBlend", (v, n) => { b.DestBlend = v; b.DestBlendName = n; });
                Take(rt, "sourceBlendAlpha", (v, n) => { b.SrcBlendAlpha = v; b.SrcBlendAlphaName = n; });
                Take(rt, "destinationBlendAlpha", (v, n) => { b.DestBlendAlpha = v; b.DestBlendAlphaName = n; });
                Take(rt, "blendOp", (v, n) => { b.BlendOp = v; b.BlendOpName = n; });
                Take(rt, "blendOpAlpha", (v, n) => { b.BlendOpAlpha = v; b.BlendOpAlphaName = n; });
                Take(rt, "colMask", (v, n) => { b.ColMask = v; b.ColMaskName = n; });
                into.Blends.Add(b);
            }

            // stencilOp is the combined set; the front and back sets only exist when
            // the shader splits them, and the emitter indexes by position, so the
            // combined slot is always filled.
            var combined = new ShaderStencilOp();
            object? op = Child(state, "stencilOp");
            if (op != null) ReadStencilOp(op, combined);
            object? front = Child(state, "stencilOpFront");
            object? back = Child(state, "stencilOpBack");
            if (op != null || front != null || back != null)
            {
                into.StencilOps.Add(combined);
                if (front != null) { var f = new ShaderStencilOp(); ReadStencilOp(front, f); into.StencilOps.Add(f); }
                if (back != null) { var k = new ShaderStencilOp(); ReadStencilOp(back, k); into.StencilOps.Add(k); }
            }
        }
        catch (Exception e)
        {
            Diag(shaderName + ": pass state unreadable: " + e.Message);
        }
    }

    private static void ReadStencilOp(object source, ShaderStencilOp into)
    {
        Take(source, "comp", (v, n) => { into.Comp = v; into.CompName = n; });
        Take(source, "pass", (v, n) => { into.PassOp = v; into.PassName = n; });
        Take(source, "fail", (v, n) => { into.Fail = v; into.FailName = n; });
        Take(source, "zFail", (v, n) => { into.ZFail = v; into.ZFailName = n; });
    }

    private static void Take(object holder, string field, Action<float, string> assign)
    {
        if (TryNum(holder, field, out float value, out string name)) assign(value, name);
    }

    private static PropertyInfo? Prop(object holder, string name) =>
        holder.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
              .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

    private static object? Child(object holder, string name) => Prop(holder, name)?.GetValue(holder);

    /// <summary>
    /// Read one state entry.  A shader-authored state is <c>{val, name}</c>; a few are
    /// plain numbers or bools, and both shapes are accepted.  A field the asset does
    /// not carry is reported as absent so the caller keeps its ShaderLab default.
    /// </summary>
    private static bool TryNum(object holder, string field, out float value, out string name)
    {
        value = 0;
        name = "";

        object? raw = Child(holder, field);
        if (raw == null) return false;

        if (raw is bool flag) { value = flag ? 1 : 0; return true; }
        if (raw.GetType().IsPrimitive || raw is decimal)
        {
            value = Convert.ToSingle(raw);
            return true;
        }

        PropertyInfo? valProp = Prop(raw, "Val") ?? Prop(raw, "Value");
        if (valProp == null) return false;
        object? v = valProp.GetValue(raw);
        if (v != null) value = Convert.ToSingle(v);

        string? text = Prop(raw, "Name")?.GetValue(raw)?.ToString();
        if (!string.IsNullOrEmpty(text) && text != "<noninit>") name = text;
        return true;
    }

    private static void ReadTags(SerializedTagMap map, List<(string k, string v)> into)
    {
        if (map?.Tags == null) return;
        foreach (var kvp in map.Tags)
        {
            string k = kvp.Key.ToString();
            if (string.IsNullOrEmpty(k)) continue;
            into.Add((k, kvp.Value.ToString()));
        }
    }

    /// <summary>
    /// The pass's <c>m_NameIndices</c> keys: every buffer, uniform and texture name
    /// the pass references.  The constant-buffer analysis uses them to give a
    /// <c>cbN</c> its real name.
    /// </summary>
    private static void ReadNameIndices(ISerializedPass pass, ShaderPass into)
    {
        try
        {
            foreach (var entry in pass.NameIndices)
            {
                string name = entry.Key.ToString();
                if (!string.IsNullOrEmpty(name)) into.NameIndices.Add(name);
            }
        }
        catch (Exception e)
        {
            Diag($"name table unreadable: {e.Message}");
        }
    }

    private static uint[] PlatformTable(IShader shader, int platform, string field)
    {
        bool nested = shader.Has_CompressedLengths_AssetList_AssetList_UInt32();
        switch (field)
        {
            case "Offsets":
                if (nested) return Inner(shader.Offsets_AssetList_AssetList_UInt32, platform);
                return Flat(shader.Offsets_AssetList_UInt32);
            case "CompressedLengths":
                if (nested) return Inner(shader.CompressedLengths_AssetList_AssetList_UInt32, platform);
                return Flat(shader.CompressedLengths_AssetList_UInt32);
            default:
                if (nested) return Inner(shader.DecompressedLengths_AssetList_AssetList_UInt32, platform);
                return Flat(shader.DecompressedLengths_AssetList_UInt32);
        }
    }

    private static uint[] Inner(AssetList<AssetList<uint>> table, int platform) =>
        platform >= 0 && platform < table.Count ? table[platform].ToArray() : Array.Empty<uint>();

    private static uint[] Flat(AssetList<uint> table) => table.ToArray();

    private static void ReadHeader(IShader shader, ShaderModel model)    {
        if (!shader.Has_ParsedForm()) return;

        var form = shader.ParsedForm;
        model.Fallback = form.FallbackName ?? "";
        model.CustomEditor = form.CustomEditorName ?? "";

        var keywords = form.KeywordNames;
        if (keywords is not null)
            foreach (var keyword in keywords)
                model.KeywordNames.Add(keyword ?? "");

        var props = form.PropInfo?.Props;
        if (props is null) return;
        foreach (var p in props)
        {
            if (p is null || string.IsNullOrEmpty(p.Name)) continue;
            var prop = new ShaderProperty
            {
                Name = p.Name,
                Description = p.Description ?? "",
                Flags = (int)p.Flags,
                Type = (int)p.Type,
                DefaultValue =
                [
                    p.DefValue_0_, p.DefValue_1_, p.DefValue_2_, p.DefValue_3_,
                ],
                DefaultTexture = p.DefTexture?.DefaultName ?? "",
                DefaultTextureDim = p.DefTexture?.TexDim ?? 0,
            };
            model.Properties.Add(prop);
        }
    }

    private static void Diag(string message)
    {
        if (Environment.GetEnvironmentVariable("SHADERRIP_TRACE") == "1")
            Console.WriteLine($"[ShaderBridge] {message}");
    }
}
