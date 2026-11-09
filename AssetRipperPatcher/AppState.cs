using AssetRipper.GUI.Web;
using AssetRipper.Processing;
using System.Text.RegularExpressions;

namespace AssetRipperPatcher;

/// <summary>
/// 跨补丁共享的运行时状态。
/// </summary>
internal static class AppState
{
    /// <summary>
    /// 命令行导出模式下的当前 GameData（Web 模式为 null）。
    /// </summary>
    public static GameData? CommandLineGameData;

    /// <summary>
    /// 命令行 <c>--only</c> 的过滤器。null 表示不过滤（导出全部资源）。
    /// 每个条目匹配 <c>IUnityObjectBase.ClassName</c>（不区分大小写、支持 <c>*</c> 通配），
    /// 例如 <c>Sprite</c>、<c>Texture2D</c>、<c>Sprite*</c>、<c>Mesh</c>。
    /// </summary>
    public static AssetFilter? CommandLineFilter;

    /// <summary>
    /// <c>--only-strict</c>：只导出类名匹配的资源，不沿引用做依赖闭包。
    ///
    /// <para>默认（false）会把匹配资源的可达依赖一并保留，这样导出的工程没有悬空 guid。
    /// 打开后得到最小集合，速度更快但引用会断——只适合查看单个类型的导出布局。</para>
    /// </summary>
    public static bool StrictOnly;

    /// <summary>
    /// <c>--remove-packaged-dlls</c>：删掉「其实是 UPM 包产物」的裸 DLL。
    ///
    /// <para><b>默认关闭。</b>删掉 DLL 会让工程里所有指向它的脚本引用变成悬空
    /// （AssetRipper 用 <c>MD5(程序集名)</c> 当 GUID，而包内程序集的 GUID 是
    /// Unity 导入时随机生成的）。保留 DLL 最多导致 CS0433，是可修复的编译错误。</para>
    /// </summary>
    public static bool RemovePackagedDlls;

    /// <summary>
    /// 当前游戏是否为 Unity 6+（Unity 版本主号 &gt;= 6000）。
    /// </summary>
    public static bool IsUnity6
    {
        get
        {
            if (CommandLineGameData is not null)
            {
                return CommandLineGameData.GameBundle.GetMaxUnityVersion().Major >= 6000;
            }
            if (GameFileLoader.IsLoaded)
            {
                return GameFileLoader.GameBundle.GetMaxUnityVersion().Major >= 6000;
            }
            return false;
        }
    }

    /// <summary>
    /// 类名过滤器。命令行 <c>--only</c> 用类名匹配（比 numeric ClassID 好记），
    /// 每个模式支持 <c>*</c> 通配符。
    /// </summary>
    internal sealed class AssetFilter
    {
        private readonly Regex[] patterns;

        private AssetFilter(Regex[] patterns)
        {
            this.patterns = patterns;
        }

        public string RawText { get; private init; } = string.Empty;

        public static AssetFilter Parse(string text)
        {
            Regex[] patterns = text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static pattern => new Regex(
                    "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                .ToArray();
            return new AssetFilter(patterns) { RawText = text };
        }

        public bool IsMatch(string? className)
        {
            if (patterns.Length == 0)
            {
                return true;
            }
            if (string.IsNullOrEmpty(className))
            {
                return false;
            }
            foreach (Regex pattern in patterns)
            {
                if (pattern.IsMatch(className))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 一个资源集合是否可能有匹配项：只要集合里至少有一个 asset 的类名匹配就保留。
        /// </summary>
        public bool MatchesAny(IEnumerable<AssetRipper.Assets.IUnityObjectBase> assets)
        {
            foreach (AssetRipper.Assets.IUnityObjectBase asset in assets)
            {
                if (IsMatch(asset.ClassName))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
