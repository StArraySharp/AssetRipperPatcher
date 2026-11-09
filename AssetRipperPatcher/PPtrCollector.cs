using AssetRipper.Assets;
using AssetRipper.Assets.Metadata;
using AssetRipper.Assets.Traversal;

namespace AssetRipperPatcher;

/// <summary>
/// 沿 PPtr 引用收集一个资产直接引用的其他资产。
///
/// <para><b>为什么需要。</b>
/// <c>--only Sprite</c> 只保留匹配类型的 collection，被剔除的 collection 里那些
/// 「Sprite 依赖的纹理 / 材质」不会导出，可 Sprite 的 <c>.meta</c> 里仍然写着
/// 指向它们的 <c>guid</c>——导出的工程里全是悬空引用。
/// 有了这个收集器，<c>--only</c> 可以先算一次闭包，把可达的依赖一并保留。</para>
///
/// <para>实现走上游自己的 <see cref="AssetWalker"/>：每种资产类型的
/// <c>WalkStandard</c> 都是生成代码，能覆盖该类型全部 PPtr 字段，
/// 比反射遍历属性可靠（属性里既有 PPtr 也有非引用的计算属性）。</para>
/// </summary>
internal sealed class PPtrCollector : AssetWalker
{
    private readonly List<PPtr> m_references = new();

    /// <summary>收集到的引用（可能含空指针）。</summary>
    public IReadOnlyList<PPtr> References => m_references;

    public override void VisitPPtr<TAsset>(PPtr<TAsset> pptr)
    {
        if (!pptr.IsNull)
        {
            m_references.Add(new PPtr(pptr.FileID, pptr.PathID));
        }
    }

    /// <summary>
    /// 收集 <paramref name="asset"/> 直接引用的资产（只返回能解析到的）。
    /// </summary>
    public static IEnumerable<IUnityObjectBase> DirectReferences(IUnityObjectBase asset)
    {
        PPtrCollector collector = new();
        asset.WalkStandard(collector);

        foreach (PPtr reference in collector.References)
        {
            IUnityObjectBase? target = asset.Collection.TryGetAsset(reference.FileID, reference.PathID);
            if (target is not null)
            {
                yield return target;
            }
        }
    }
}
