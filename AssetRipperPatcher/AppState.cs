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
    /// <c>--web</c>：不用原生窗口，直接把 UI 打到默认浏览器。
    ///
    /// <para>开发时调界面、或者 WebView2 缺失/异常时用这个。两种模式共用同一套
    /// Kestrel 端点和前端资源，所以看到的行为完全一致。</para>
    /// </summary>
    public static bool OpenUiInBrowser;

    /// <summary>
    /// <c>--legacy-ui</c>：回退到 AssetRipper 原版 Web UI。
    ///
    /// <para>新界面是增量替代，旧的那套仍完整可用；需要对照行为或新界面出问题时的
    /// 应急出口。</para>
    /// </summary>
    public static bool UseLegacyUi;

    /// <summary>
    /// <c>--port N</c>：固定 UI 监听端口（默认 0 = 随机空闲端口）。
    ///
    /// <para>调试时必须固定端口：否则每次启动地址都变，浏览器标签和 DevTools
    /// 都要重新连。</para>
    /// </summary>
    public static int UiPort = 0;

    /// <summary>
    /// <c>--no-open</c>：启动 UI 但不自动打开窗口/浏览器。
    ///
    /// <para>配合 <c>--web --port 5173</c> 可以把它当成普通 Web 服务跑在后台，
    /// 用任意浏览器调试，热刷新也不受原生窗口限制。</para>
    /// </summary>
    public static bool NoAutoOpen;

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
