using System.Buffers.Binary;
using System.Security.Cryptography;
using AssetRipper.Assets;
using AssetRipper.Assets.Metadata;
using AssetRipper.Assets.Traversal;
using AssetRipper.Export.UnityProjects;

namespace AssetRipperPatcher.Patches;

/// <summary>
/// 资源去重的核心：给一条资源算出"内容指纹"。
///
/// <para><b>指纹不含什么。</b>名字、PathID、所属文件都不参与——两条名字不同但
/// 数据完全一样的纹理就是同一份资源，正是要去掉的那种重复。</para>
///
/// <para><b>指纹含什么。</b>沿 <c>WalkStandard</c> 走一遍所有字段：基本值按键值
/// 原样喂进哈希，引用（PPtr）只记"这里有个引用"而不记它指向谁。原因有两层：
/// 指向的 PathID 在每个文件里独立编号，天然不同；而引用目标本身是否相同，
/// 由调用方按「已经确认合并的等价类」去比较，不能塞进哈希（会形成递归）。</para>
/// </summary>
internal sealed class AssetFingerprintWalker : AssetWalker
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly byte[] _scratch = new byte[8];

    /// <summary>走完一遍后的指纹；<see cref="Finish"/> 之前为空。</summary>
    public string Value { get; private set; } = "";

    /// <summary>记录到的引用（FileID + PathID），用于后续的一致性校验。</summary>
    public List<(int FileID, long PathID)> References { get; } = new();

    public override bool EnterAsset(IUnityAssetBase asset)
    {
        WriteTag(1);
        WriteString(asset.GetType().Name);
        return true;
    }

    public override void ExitAsset(IUnityAssetBase asset) => WriteTag(2);

    public override bool EnterField(IUnityAssetBase asset, string name)
    {
        WriteTag(3);
        WriteString(name);
        return true;
    }

    public override void ExitField(IUnityAssetBase asset, string name) => WriteTag(4);

    public override bool EnterList<T>(IReadOnlyList<T> list)
    {
        WriteTag(5);
        WriteInt(list.Count);
        return true;
    }

    public override void ExitList<T>(IReadOnlyList<T> list) => WriteTag(6);

    public override bool EnterDictionary<TKey, TValue>(IReadOnlyCollection<KeyValuePair<TKey, TValue>> dictionary)
    {
        WriteTag(7);
        WriteInt(dictionary.Count);
        return true;
    }

    public override void ExitDictionary<TKey, TValue>(IReadOnlyCollection<KeyValuePair<TKey, TValue>> dictionary)
        => WriteTag(8);

    public override bool EnterPair<TKey, TValue>(KeyValuePair<TKey, TValue> pair)
    {
        WriteTag(9);
        return true;
    }

    public override void ExitPair<TKey, TValue>(KeyValuePair<TKey, TValue> pair) => WriteTag(10);

    public override void VisitPrimitive<T>(T value)
    {
        WriteTag(11);
        switch (value)
        {
            case null:
                WriteTag(0);
                break;
            case string s:
                WriteString(s);
                break;
            case bool b:
                WriteTag(b ? (byte)1 : (byte)0);
                break;
            case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or char:
                WriteNumber(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            default:
                // 其它类型（UnityGuid、Hash128 等）走 ToString：它们都是值语义。
                WriteString(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "");
                break;
        }
    }

    /// <summary>
    /// A reference contributes only its presence, plus a record for the caller.
    ///
    /// <para>它的 FileID 是「当前文件内的序号」，同一份资源换一个文件就变；PathID 同理。
    /// 所以两者都不能进哈希，否则内容相同的两条资源永远算不到一起。</para>
    /// </summary>
    public override void VisitPPtr<TAsset>(PPtr<TAsset> pptr)
    {
        WriteTag(12);
        References.Add((pptr.FileID, pptr.PathID));
    }

    /// <summary>收尾并取出指纹。</summary>
    public string Finish()
    {
        Value = Convert.ToHexString(_hash.GetHashAndReset());
        return Value;
    }

    private void WriteTag(byte tag) => _hash.AppendData([tag]);

    private void WriteInt(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(_scratch, value);
        _hash.AppendData(_scratch, 0, 4);
    }

    private void WriteNumber(double value)
    {
        BinaryPrimitives.WriteDoubleLittleEndian(_scratch, value);
        _hash.AppendData(_scratch, 0, 8);
    }

    private void WriteString(string value)
    {
        WriteInt(value.Length);
        _hash.AppendData(System.Text.Encoding.UTF8.GetBytes(value));
    }
}