using System.Linq;
using System.Reflection;
using AssetRipper.Assets.Generics;
using AssetRipper.Export.Modules.Shaders;
using AssetRipper.Export.Modules.Shaders.Exporters;
using AssetRipper.Export.Modules.Shaders.IO;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.Modules.Shaders.ShaderBlob.Parameters;
using AssetRipper.SourceGenerated.Classes.ClassID_48;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader;
using AssetRipper.SourceGenerated.Subclasses.SerializedPass;
using AssetRipper.SourceGenerated.Subclasses.SerializedProgram;
using AssetRipper.SourceGenerated.Subclasses.SerializedProgramParameters;
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

        ReadStructure(shader, model, blobs);
                MergeGlobalsReflection(model);

                Diag($"{model.Name}: blobs={blobs.Length} payloads={model.Payloads.Count} " +
                     $"props={model.Properties.Count} subShaders={model.SubShaders.Count} " +
                     $"passes={model.SubShaders.Sum(s => s.Passes.Count)}");

        // A shader with no program still has a structure worth writing, so the
        // model only fails when there is neither.
        return model.Payloads.Count > 0 || model.SubShaders.Count > 0 ? model : null;
    }

    /// <summary>
            /// Give every sub-program the union of the <c>$Globals</c> reflection that
            /// shares its own block size.
            ///
            /// <para>Unity records the constant-buffer table per compiled variant, and a
            /// variant only lists the members it references. Merging across variants is
            /// only sound within one block size: <c>$Globals</c> is packed per variant,
            /// so a shader whose variants keep 176-byte and 320-byte blocks puts the
            /// same property at different offsets in each. Merging those would name a
            /// row after a property that lives at a different offset in this variant --
            /// a silently wrong uniform name, worse than a placeholder.</para>
            /// </summary>
            private static void MergeGlobalsReflection(ShaderModel model)
            {
                var programs = model.SubShaders
                    .SelectMany(s => s.Passes)
                    .SelectMany(p => p.SubPrograms)
                    .ToList();
                if (programs.Count == 0) return;

                // 按 block size 分桶，桶内才能安全求并集。
                var buckets = new Dictionary<int, List<ShaderConstantBufferMember>>();
                var contested = new Dictionary<int, HashSet<int>>();
                foreach (var sp in programs)
                    foreach (var cb in sp.ConstantBuffers)
                    {
                        if (cb.Name != "$Globals" || cb.Size <= 0) continue;
                        if (!buckets.TryGetValue(cb.Size, out var bucket))
                        {
                            buckets[cb.Size] = bucket = new List<ShaderConstantBufferMember>();
                            contested[cb.Size] = new HashSet<int>();
                        }
                        foreach (var m in cb.Members)
                        {
                            if (string.IsNullOrEmpty(m.Name)) continue;
                            var have = bucket.FirstOrDefault(x => x.Offset == m.Offset);
                            if (have is null) bucket.Add(m);
                            // 同桶同偏移两个名字：该属性在同一布局里被重用了，不可信。
                            else if (!string.Equals(have.Name, m.Name, StringComparison.Ordinal))
                                contested[cb.Size].Add(m.Offset);
                        }
                    }

                foreach (var (size, bucket) in buckets)
                    bucket.RemoveAll(m => contested[size].Contains(m.Offset));

                foreach (var sp in programs)
                {
                    var globals = sp.ConstantBuffers.FirstOrDefault(c => c.Name == "$Globals");
                    if (globals is null) continue;
                    if (!buckets.TryGetValue(globals.Size, out var bucket) || bucket.Count == 0) continue;
                    foreach (var m in bucket)
                        if (!globals.Members.Any(x => x.Offset == m.Offset)) globals.Members.Add(m);
                }

                foreach (var (size, bucket) in buckets.OrderBy(kv => kv.Key))
                    Diag($"$Globals size {size}: {bucket.Count} member(s), " +
                         $"{contested[size].Count} contested offset(s) dropped");
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
    private static void ReadStructure(IShader shader, ShaderModel model, ShaderSubProgramBlob[] blobs)
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
                            ReadSubPrograms(p, pass, blobs);
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
    private static void ReadSubPrograms(ISerializedPass pass, ShaderPass into, ShaderSubProgramBlob[] blobs)
    {
            // 名字表是 pass 级的一张 名 -> 索引 字典。参数反射只存索引，
            // 没有这张反查表就永远拿不到名字（这正是 $Globals 成员缺名的根因）。
            var nameByIndex = BuildNameTable(pass);

            foreach (var program in Programs(pass))
            {
                if (program == null) continue;
                try
                {
                    // shader 级的完整 $Globals 表：只有一个变体引用到的成员才会出现在
                    // 该变体自己的反射里，而这张表列的是整块布局。
                    ShaderConstantBufferInfo? common = CommonGlobals(program, nameByIndex);

                    // Has_PlayerSubPrograms() 只看版本字段是否存在，不看有没有数据。
                    // 空表时必须退回 SubPrograms，否则整个程序表丢失、反射全空。
                    var players = program.GetPlayerSubPrograms();
                    if (players.Count > 0)
                    {
                        var paramIndices = program.GetParameterBlobIndices();
                        for (int i = 0; i < players.Count; i++)
                        {
                            var sp = players[i];
                            // 参数块索引与 blob 索引不是同一个数：校验和只由 program 的字节决定，
                            // 参数表是另一段。用错索引会读到别的块的中间，反射全废。
                            int paramIndex = i < paramIndices.Count ? (int)paramIndices[i] : -1;
                            AddSubProgram(into, sp.BlobIndex, paramIndex, sp.KeywordIndices,
                                Reflection(blobs, sp.BlobIndex, paramIndex), common);
                        }
                    }
                    else
                    {
                        foreach (var sp in program.SubPrograms)
                            AddSubProgram(into, sp.BlobIndex, -1, sp.KeywordIndices,
                                Reflection(blobs, sp.BlobIndex, -1), common);
                    }
                }
                catch (Exception e)
                {
                    Diag($"{pass.Name}: sub-program table unreadable: {e.Message}");
                }
            }
        }

        /// <summary>The pass's <c>m_NameIndices</c> inverted: index to name.</summary>
        private static Dictionary<int, string> BuildNameTable(ISerializedPass pass)
        {
            var map = new Dictionary<int, string>();
            try
            {
                foreach (var entry in pass.NameIndices)
                {
                    string name = entry.Key.ToString();
                    if (!string.IsNullOrEmpty(name)) map[entry.Value] = name;
                }
            }
            catch (Exception e)
            {
                Diag($"name table unreadable: {e.Message}");
            }
            return map;
        }

        /// <summary>
        /// The program-wide <c>$Globals</c> table (<c>m_CommonParameters</c>).
        ///
        /// <para>Unity records the constant-buffer layout twice: once per compiled
        /// variant, listing only the members that variant references, and once per
        /// program in <c>m_CommonParameters</c>, listing the block as the shader
        /// declared it. A row only the second table knows about otherwise has no name
        /// to be found under.</para>
        ///
        /// <para>Its members carry only a <see cref="IConstantBuffer.NameIndex"/>, so
        /// the pass's own name table has to be inverted to read them.</para>
        /// </summary>
        private static ShaderConstantBufferInfo? CommonGlobals(ISerializedProgram program,
            IReadOnlyDictionary<int, string> nameByIndex)
        {
            if (nameByIndex.Count == 0 || !program.Has_CommonParameters()) return null;
            try
            {
                var parameters = program.CommonParameters;
                var buffer = new ShaderConstantBufferInfo { Name = "$Globals" };

                void Add(string name, int offset, int type, int dim, int arraySize, bool matrix, int rows)
                {
                    if (string.IsNullOrEmpty(name) || offset < 0) return;
                    buffer.Members.Add(new ShaderConstantBufferMember
                    {
                        Name = name,
                        Offset = offset,
                        Type = type,
                        Dim = dim,
                        ArraySize = Math.Max(1, arraySize),
                        RowCount = matrix ? Math.Max(1, rows) : 1,
                        IsMatrix = matrix,
                    });
                }

                void AddVectors(IEnumerable<global::AssetRipper.SourceGenerated.Subclasses.VectorParameter.IVectorParameter> vectors)
                {
                    foreach (var v in vectors)
                        Add(Resolve(nameByIndex, v.NameIndex), v.OffsetInConstantBuffer,
                            v.Type, v.Dim > 0 ? v.Dim : 1, v.ArraySize, false, 1);
                }

                void AddMatrices(IEnumerable<global::AssetRipper.SourceGenerated.Subclasses.MatrixParameter.IMatrixParameter> matrices)
                {
                    foreach (var m in matrices)
                        Add(Resolve(nameByIndex, m.NameIndex), m.OffsetInConstantBuffer,
                            m.Type, 4, m.ArraySize, true, m.RowCount);
                }

                foreach (var cb in parameters.ConstantBuffers)
                {
                    string bufferName = Resolve(nameByIndex, cb.NameIndex);
                    bool isGlobals = bufferName == "$Globals";
                    // A named Unity buffer describes a different block; only $Globals
                    // and the nameless top-level group belong here.
                    if (!isGlobals && !string.IsNullOrEmpty(bufferName)) continue;
                    if (isGlobals) buffer.Size = Math.Max(buffer.Size, cb.Size);
                    AddVectors(cb.VectorParams);
                    AddMatrices(cb.MatrixParams);
                }

                // 顶层那张表是 program 级的全集（旧布局里就是唯一的表）。
                // 它与上面按块分组的表是互补的：分组只列出该块保留的成员，
                // 顶层则列出整个程序用到的全部成员，所以两张都要收。
                AddVectors(parameters.VectorParams);
                AddMatrices(parameters.MatrixParams);

                if (buffer.Members.Count == 0) return null;

                                // 同一个字节偏移可能对应多个不同名字：Unity 的不同变体按各自动用的
                                // 属性重新打包 $Globals，于是 _LightColor0 和 _Color 都会出现在 32。
                                // 这种偏移的名字是无法确定的，取任何一个都可能写错，所以整段丢弃，
                                // 让它继续以 _cbN_rowK 出现——那至少是诚实的。
                                var contested = buffer.Members
                                    .GroupBy(m => m.Offset)
                                    .Where(g => g.Select(m => m.Name).Distinct(StringComparer.Ordinal).Count() > 1)
                                    .Select(g => g.Key)
                                    .ToHashSet();
                                if (contested.Count > 0)
                                {
                                    Diag($"common $Globals: dropping {contested.Count} contested offset(s): " +
                                         string.Join(",", contested.OrderBy(x => x)));
                                    buffer.Members.RemoveAll(m => contested.Contains(m.Offset));
                                    if (buffer.Members.Count == 0) return null;
                                }

                                if (buffer.Size <= 0)
                                    buffer.Size = buffer.Members.Max(m => m.Offset) + 16;

                                Diag($"common $Globals: {buffer.Members.Count} member(s), {buffer.Size} byte(s): " +
                                 string.Join(" ", buffer.Members.OrderBy(m => m.Offset)
                                     .Select(m => $"{m.Name}@{m.Offset}")));
                                return buffer;
            }
            catch (Exception e)
            {
                Diag($"common parameters unreadable: {e.Message}");
                return null;
            }

            static string Resolve(IReadOnlyDictionary<int, string> table, int index) =>
                table.TryGetValue(index, out string? n) ? n : "";
        }

        /// <summary>
        /// The parsed reflection for one blob slot, or null when the table cannot be read.
        ///
        /// <para>Blob index 0 is shared between two stages in some GLSL builds, and every
        /// platform's blob is scanned in turn, so the first table that yields a program
        /// with any reflection wins. A slot the platform does not carry simply returns
        /// null and the decompiler falls back to inference.</para>
        /// </summary>
        private static ShaderSubProgram? Reflection(ShaderSubProgramBlob[] blobs, uint blobIndex, int paramIndex)
                {
                    foreach (ShaderSubProgramBlob blob in blobs)
                    {
                        if (blob is null || blobIndex >= blob.Entries.Length) continue;
                        try
                        {
                            ShaderSubProgram sub = paramIndex >= 0 && paramIndex < blob.Entries.Length
                                ? blob.GetSubProgram(blobIndex, (uint)paramIndex)
                                : blob.GetSubProgram(blobIndex);
                            if (sub.ConstantBuffers.Length > 0 || sub.TextureParameters.Length > 0)
                                return sub;
                        }
                        catch (Exception e)
                        {
                            Diag($"blob {blobIndex}/{paramIndex} reflection unreadable ({blob.Entries.Length} entries): {e.Message}");
                        }
                    }
                    return null;
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

    private static void AddSubProgram(ShaderPass into, uint blobIndex, int paramIndex,
            IEnumerable<ushort> keywords, ShaderSubProgram? reflection = null,
            ShaderConstantBufferInfo? common = null)
        {
            var info = new ShaderSubProgramInfo
            {
                BlobIndex = (int)blobIndex,
                ParameterBlobIndex = paramIndex,
            };
            if (keywords != null)
                foreach (ushort k in keywords) info.KeywordIndices.Add(k);
            if (reflection != null) FillReflection(info, reflection);
            if (common != null) MergeGlobals(info, common);

            // Kept in the asset's own order, one entry per stage slot.  Two stages can
            // name the same blob slot with *different* keyword sets (a GLSL pair shares
            // an index), so merging them would invent a combined set that the build
            // never made and drop two real ones.  The reader of this table resolves a
            // slot to its last entry, exactly as the serialized-data path does.
            into.SubPrograms.Add(info);
        }

        /// <summary>
        /// Fold the program-wide <c>$Globals</c> table into one sub-program's own.
        ///
        /// <para>Per-variant reflection lists only the members that variant reads, so a
        /// row another stage happens to use has no name here. The common table
        /// describes the whole block, and both tables index the same offsets, so the
        /// two are complementary rather than alternatives.</para>
        /// </summary>
        private static void MergeGlobals(ShaderSubProgramInfo info, ShaderConstantBufferInfo common)
        {
            var globals = info.ConstantBuffers.Find(c => c.Name == "$Globals");
            if (globals is null)
            {
                globals = new ShaderConstantBufferInfo { Name = "$Globals", Size = common.Size };
                info.ConstantBuffers.Add(globals);
            }
            globals.Size = Math.Max(globals.Size, common.Size);
            foreach (var m in common.Members)
            {
                if (globals.Members.Any(x => x.Name == m.Name && x.Offset == m.Offset)) continue;
                globals.Members.Add(m);
            }
        }

        /// <summary>
        /// Hand the library the program's own constant-buffer reflection.
        ///
        /// <para><b>Why this matters.</b>Without it the decompiler has to infer which
        /// member a cbuffer row belongs to from the code that reads it, and a row it
        /// cannot pin is emitted as <c>_cb0_row5</c> -- a placeholder that compiles but
        /// says nothing. AssetRipper has already parsed the same table
        /// (<c>m_Parameters.m_ConstantBuffers</c>): every member's name, byte offset, type
        /// and dimensions. Passing it across means the emitted ShaderLab carries the
        /// shader's real uniform names even where a variant only touches part of a
        /// matrix.</para>
        /// </summary>
        private static void FillReflection(ShaderSubProgramInfo info, ShaderSubProgram sub)
        {
            try
            {
                foreach (ConstantBuffer cb in sub.ConstantBuffers)
                {
                    var buffer = new ShaderConstantBufferInfo
                    {
                        Name = cb.Name ?? "",
                        Size = cb.Size,
                    };

                    foreach (MatrixParameter m in cb.MatrixParams)
                        AddMember(buffer, m, m.Name, m.Index, (int)m.RowCount);
                    foreach (VectorParameter v in cb.VectorParams)
                        AddMember(buffer, v, v.Name, v.Index, 1);
                    foreach (StructParameter s in cb.StructParams)
                    {
                        foreach (NumericShaderParameter member in s.AllNumericMembers)
                            AddMember(buffer, member, $"{s.Name}.{member.Name}",
                                member.Index, member.IsMatrix ? (int)member.RowCount : 1);
                    }

                    if (!string.IsNullOrEmpty(buffer.Name) || buffer.Members.Count > 0)
                        info.ConstantBuffers.Add(buffer);
                }

                Diag($"blob {info.BlobIndex}: cbuffers={info.ConstantBuffers.Count} " +
                                    $"vectors={sub.VectorParameters.Length} matrices={sub.MatrixParameters.Length} " + string.Join(" | ",
                                    info.ConstantBuffers.Select(c => $"{c.Name}[{string.Join(",", c.Members.Select(m => $"{m.Name}@{m.Offset}"))}]")));

                foreach (TextureParameter t in sub.TextureParameters)
                {
                    if (string.IsNullOrEmpty(t.Name)) continue;
                    info.Textures.Add(new ShaderTextureParam
                    {
                        Name = t.Name,
                        Index = t.Index,
                        Sampler = t.SamplerIndex,
                    });
                }
            }
            catch (Exception e)
            {
                Diag($"constant-buffer reflection unreadable: {e.Message}");
            }
        }

        /// <summary>
        /// One reflected member.  <paramref name="rowCount"/> is the number of 16-byte
        /// rows it occupies: 1 for scalars and vectors, N for an N-row matrix. The
        /// library derives <c>packoffset(cN.x)</c> from <see cref="ShaderConstantBufferMember.Offset"/>.
        /// </summary>
        private static void AddMember(ShaderConstantBufferInfo buffer, NumericShaderParameter parameter,
            string? name, int offset, int rowCount)
        {
            if (string.IsNullOrEmpty(name)) return;
                        // 一个 16 字节寄存器里的分量数。矩阵存的是列（ColumnCount 个 float），
                        // 向量参数则是 RowCount —— VectorParameter 的 ColumnCount 恒为 1，
                        // 用它会算成 1，整行就只命名 lane x，其余 lane 全成 _cbN_rowK_z。
                        int dim = parameter.IsMatrix
                            ? parameter.ColumnCount > 0 ? parameter.ColumnCount : 4
                            : parameter.RowCount > 0 ? parameter.RowCount : 1;
                        buffer.Members.Add(new ShaderConstantBufferMember
                        {
                            Name = name,
                            Offset = offset,
                            Type = (int)parameter.Type,
                            Dim = dim,
                            ArraySize = parameter.ArraySize,
                            RowCount = rowCount < 1 ? 1 : rowCount,
                            IsMatrix = parameter.IsMatrix,
                        });
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
