using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Export.UnityProjects;
using AssetRipper.GUI.Web;
using AssetRipperPatcher.Patching;

namespace AssetRipperPatcher.Patches;

/// <summary>
/// 资源去重：内容完全相同的资源只导出一份，其余改写成指向它的引用。
///
/// <para><b>为什么必须在 collection 层做。</b>
/// 一个资源在 Unity 里的身份是「GUID + fileID」这一对：GUID 来自导出时随机生成的
/// <c>.meta</c>，fileID 是类 ID 派生出来的稳定值。要让两条资源真正合成一条，
/// 就不能只把文件删掉——那样所有指向被删资源的引用都会变成 Missing。
/// 必须让那些资源<b>指向同一个 GUID 和同一个 fileID</b>，
/// 而这两者恰好都由 collection 提供（<see cref="IExportCollection.CreateExportPointer"/>）。
/// 所以这里把重复资源的 collection 换成
/// <see cref="RedirectExportCollection"/>——它不写任何文件，
/// 只回答"我等于那条规范资源"。</para>
///
/// <para><b>为什么不会误合并。</b>指纹（<see cref="AssetFingerprintWalker"/>）
/// 只对白名单里的类型计算，且要求两边记录的引用条数一致。
/// 名字、PathID、所在文件全部不参与——名字不同而内容一致的纹理，
/// 正是应该合并的那一类。</para>
/// </summary>
internal static class AssetDeduplicationPatch
{
    /// <summary>
    /// 可以安全合并的资源类型。
    ///
    /// <para>只收"纯数据、内容相等即等价"的类型。像 <c>MonoBehaviour</c> 这种
    /// 带脚本字段、可能被 <c>FindObjectsOfType</c> 之类按实例区分的资源不在此列：
    /// 合并它们会改变运行时行为，而不只是省磁盘。</para>
    /// </summary>
    private static readonly HashSet<string> DeduplicableTypes = new(StringComparer.Ordinal)
    {
        "Texture2D", "Texture", "Cubemap", "RenderTexture",
        "Mesh", "AudioClip", "Shader", "Material",
        "TextAsset", "Font", "AnimationClip", "AnimatorController",
        "PhysicMaterial", "PhysicsMaterial2D",
    };

    private static int s_groups;
    private static int s_removed;

    public static void ReportStats()
    {
        if (s_groups == 0)
        {
            return;
        }
        Console.WriteLine($"[Dedup] merged {s_removed} duplicate asset(s) into {s_groups} canonical one(s)");
    }

    /// <summary>
    /// 在 collection 列表建好之后合并重复项。
    ///
    /// <para>挂在 <see cref="ProjectExporter.CreateCollections(GameBundle)"/> 的 postfix 上，
    /// 因为这是"全部资源已就绪、导出尚未开始"的唯一时刻：
    /// 早于此时资源还没分组，晚于此时 <c>ProjectAssetContainer</c> 已经按 collection
    /// 建好了 asset → collection 的映射，换 collection 就来不及了。</para>
    /// </summary>
    [PatchMethod(typeof(ProjectExporter), "CreateCollections", PatchType = PatchType.Postfix,
        Priority = HarmonyLib.Priority.LowerThanNormal)]
    public static void Deduplicate(GameBundle fileCollection, ref List<IExportCollection> __result)
    {
        ReportStats();
        s_groups = 0;
        s_removed = 0;

        if (!GameFileLoader.Settings.ProcessingSettings.EnableAssetDeduplication)
        {
            return;
        }

        // 只有「一条资源 = 一个 collection」的形态才能安全替换：
        // 纹理 + Sprite 那种多资源集合换掉会连 Sprite 一起断掉。
        var candidates = new List<(int Index, IExportCollection Collection, ExportCollection Concrete, IUnityObjectBase Asset)>();
        for (int i = 0; i < __result.Count; i++)
        {
            IExportCollection collection = __result[i];
            if (collection is not ExportCollection concrete) continue;
            if (!collection.Exportable) continue;

            IUnityObjectBase[] assets = collection.Assets.ToArray();
            if (assets.Length != 1) continue;
            if (!DeduplicableTypes.Contains(assets[0].ClassName)) continue;

            candidates.Add((i, collection, concrete, assets[0]));
        }

        if (candidates.Count < 2)
        {
            return;
        }

        // 指纹 → 规范 collection。同一指纹的第一条留作规范，其余改写成重定向。
        var canonicalByFingerprint = new Dictionary<string, (ExportCollection Concrete, IUnityObjectBase Asset)>(StringComparer.Ordinal);
        var fingerprints = new string?[__result.Count];

        foreach (var (index, _, concrete, asset) in candidates)
        {
            string fingerprint;
            int referenceCount;
            try
            {
                var walker = new AssetFingerprintWalker();
                asset.WalkStandard(walker);
                fingerprint = walker.Finish();
                referenceCount = walker.References.Count;
            }
            catch (Exception ex)
            {
                // 指纹算不出来就当它不可去重——宁可多留一份文件，也不能误合并。
                Console.WriteLine($"[Dedup] '{asset.GetBestName()}' not fingerprinted: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            fingerprints[index] = $"{asset.ClassName}:{referenceCount}:{fingerprint}";
        }

        for (int i = 0; i < __result.Count; i++)
        {
            string? key = fingerprints[i];
            if (key is null) continue;
            if (__result[i] is not ExportCollection concrete) continue;
            IUnityObjectBase asset = concrete.Assets.First();

            if (canonicalByFingerprint.TryGetValue(key, out var canonical))
            {
                // 规范资源必须已经有一条稳定的 GUID 才能被引用。
                var redirect = new RedirectExportCollection();
                long exportId = canonical.Concrete.GetExportID(container: null!, canonical.Asset);
                try
                {
                    redirect.Add(asset, exportId, canonical.Concrete.GUID,
                        concrete.AssetExporter.ToExportType(canonical.Asset));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Dedup] '{asset.GetBestName()}' not redirected: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }
                __result[i] = redirect;
                s_removed++;
                Console.WriteLine($"[Dedup] '{asset.GetBestName()}' -> '{canonical.Asset.GetBestName()}' " +
                                  $"(fileID {exportId}, guid {canonical.Concrete.GUID})");
            }
            else
            {
                canonicalByFingerprint[key] = (concrete, asset);
                s_groups++;
            }
        }

        if (s_removed > 0)
        {
            Console.WriteLine($"[Dedup] {s_removed} duplicate(s) merged into {s_groups} canonical asset(s)");
        }
    }
}