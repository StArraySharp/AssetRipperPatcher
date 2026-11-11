using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace AssetRipperPatcher.WebUi;

/// <summary>
/// 外壳环境探测：WebView2 是否可用、当前跑在 Photino 还是普通浏览器里。
///
/// <para><b>为什么要在启动前探测。</b>Photino 依赖系统 WebView2 运行时（Win10 1803+ 通常自带，
/// 但精简版系统会缺）。缺了的话 <c>new PhotinoWindow()</c> 会直接崩到原生层，
/// 拿不到托管异常。先探测能给出可读的错误，并自动降级到「只开浏览器」。</para>
/// </summary>
internal static class HostEnvironment
{
	private const string WebView2ClientKey =
		@"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

	/// <summary>被 <c>--web</c> 或者 WebView2 缺失时置为 true。</summary>
	public static bool BrowserOnly { get; set; }

	public static string ShellName => BrowserOnly ? "browser" : "photino";

	public static string? WebView2Version { get; } = DetectWebView2();

	[SupportedOSPlatform("windows")]
	private static string? DetectWebView2()
	{
		try
		{
			if (Registry.GetValue($@"HKEY_LOCAL_MACHINE\{WebView2ClientKey}", "pv", null) is string machine)
			{
				return machine;
			}
			if (Registry.GetValue($@"HKEY_CURRENT_USER\{WebView2ClientKey}", "pv", null) is string user)
			{
				return user;
			}
		}
		catch (Exception)
		{
			// 注册表读不到不代表没有 WebView2（可能装在别处），交给 Photino 自己判断。
		}

		string[] candidates =
		[
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "EdgeWebView", "Application"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "EdgeWebView", "Application"),
		];
		foreach (string directory in candidates)
		{
			if (Directory.Exists(directory))
			{
				return Directory.EnumerateDirectories(directory).FirstOrDefault() is { } version
					? Path.GetFileName(version)
					: "installed";
			}
		}
		return null;
	}

	public static bool WebView2Available => WebView2Version is not null;
}