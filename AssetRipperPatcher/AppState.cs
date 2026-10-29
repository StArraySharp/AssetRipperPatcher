using AssetRipper.GUI.Web;
using AssetRipper.Processing;

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
}
