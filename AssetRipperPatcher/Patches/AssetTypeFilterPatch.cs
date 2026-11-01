using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Export.UnityProjects;
using AssetRipperPatcher.Patching;
using HarmonyLib;

namespace AssetRipperPatcher.Patches;

/// <summary>
/// <c>--only &lt;类型[,类型...]&gt;</c>：只导出指定类型的资源。
///
/// AssetRipper 没有原生的资源类型过滤（它总是遍历所有 collection），
/// 而全量导出一次动辄几分钟、几 GB，调 bug 时很难受。
/// 这里在 <see cref="ProjectExporter.CreateCollections(GameBundle)"/> 上挂钩：
/// 把不含目标类型的 collection 从导出列表里剔掉。
///
/// 匹配依据是 <see cref="IUnityObjectBase.ClassName"/>（不区分大小写，支持 <c>*</c> 通配），
/// 例如 <c>--only Sprite</c>、<c>--only Sprite,Texture2D</c>、<c>--only Sprite*</c>。
///
/// 注意：被剔除的 collection 只是不导出，不会影响被保留资源之间的引用解析——因为引用解析
/// 依赖 <see cref="ProjectAssetContainer"/> 里的 asset 集合，而我们过滤的是导出列表。
/// 所以 <c>--only Sprite</c> 得到的 .asset 里 texture 引用仍指向纹理的 guid，
/// 只是那个 .png 没有一起导出。
/// </summary>
internal static class AssetTypeFilterPatch
{
    /// <summary>
    /// 过滤 <see cref="ProjectExporter.CreateCollections(GameBundle)"/> 的返回值。
    /// 补丁挂在 private 方法上，用 <c>__result</c> 改写结果集。
    /// </summary>
    [PatchMethod(typeof(ProjectExporter), "CreateCollections", PatchType = PatchType.Postfix)]
    public static void FilterCollections(GameBundle fileCollection, ref List<IExportCollection> __result)
    {
        AppState.AssetFilter? filter = AppState.CommandLineFilter;
        if (filter is null)
        {
            return;
        }

        int before = __result.Count;
        __result = __result
            .Where(static collection => collection.Assets.Count() > 0)
            .Where(collection => HasMatchingAsset(collection, filter))
            .ToList();
        int removed = before - __result.Count;
        Console.WriteLine(
            $"[AssetFilter] --only '{filter.RawText}': kept {__result.Count}/{before} collection(s), removed {removed}");
    }

    /// <summary>
    /// 集合里是否有匹配的资源。一个 collection 可能含多个 asset（例如纹理 + 它上面的 Sprite），
    /// 只要有一个匹配就整体保留，避免把 Sprite 的依赖纹理一起砍掉。
    /// </summary>
    private static bool HasMatchingAsset(IExportCollection collection, AppState.AssetFilter filter)
    {
        foreach (IUnityObjectBase asset in collection.Assets)
        {
            if (filter.IsMatch(asset.ClassName))
            {
                return true;
            }
        }
        return false;
    }}
