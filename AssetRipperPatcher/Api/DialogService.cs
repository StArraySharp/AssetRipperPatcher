using AssetRipper.Import.Logging;
using NativeFileDialog;

namespace AssetRipperPatcher.Api;

/// <summary>
/// 系统原生文件对话框。
///
/// <para><b>为什么不用 Photino 自带的对话框。</b>Photino 的
/// <c>ShowOpenFolder</c> 必须在窗口的 UI 线程上跑，而我们的 HTTP 请求线程拿不到那个线程，
/// 只能靠 <c>PhotinoWindow.Invoke</c> 绕一圈，取消/多选的行为也不一致。
/// <see cref="NativeFileDialog"/> 直接在调用线程弹出系统对话框，Web 模式和原生窗口模式
/// 走同一条代码路径，行为一致。</para>
/// </summary>
internal static class DialogService
{
	private static IFileDialog? _dialog;

	private static IFileDialog? Instance
	{
		get
		{
			if (_dialog is null)
			{
				try
				{
					_dialog = NativeFileDialog.NativeFileDialog.GetFileDialog();
				}
				catch (Exception ex)
				{
					Logger.Warning($"Native file dialog unavailable: {ex.Message}");
					_dialog = null;
				}
			}
			return _dialog;
		}
	}

	/// <summary>当前平台是否支持原生对话框。</summary>
	public static bool IsSupported => Instance is not null;

	public static DialogResultDto OpenFolder(string title, string? basePath = null)
	{
		IFileDialog? dialog = Instance;
		if (dialog is null)
		{
			return new DialogResultDto(Unsupported: true, Canceled: true, Paths: []);
		}

		try
		{
			string? path = dialog.OpenFolder(title, ResolveBasePath(basePath));
			return path is null
				? new DialogResultDto(false, true, [])
				: new DialogResultDto(false, false, [path]);
		}
		catch (Exception ex)
		{
			Logger.Warning($"OpenFolder failed: {ex.Message}");
			return new DialogResultDto(Unsupported: false, Canceled: true, Paths: []);
		}
	}

	public static DialogResultDto OpenFile(string title, string? basePath = null, bool multiSelect = false)
	{
		IFileDialog? dialog = Instance;
		if (dialog is null)
		{
			return new DialogResultDto(Unsupported: true, Canceled: true, Paths: []);
		}

		try
				{
					string[]? paths = dialog.OpenFile(title, ResolveBasePath(basePath), BuildOpenFileFilter(), multiSelect);
			return paths is null or { Length: 0 }
				? new DialogResultDto(false, true, [])
				: new DialogResultDto(false, false, paths);
		}
		catch (Exception ex)
		{
			Logger.Warning($"OpenFile failed: {ex.Message}");
			return new DialogResultDto(false, true, []);
		}
	}

			/// <summary>选择一个目录作为输出位置。</summary>
			public static DialogResultDto PickOutputFolder(string title, string? basePath = null)
			{
				// 输出目标是目录，OpenFolder 更贴切；标题由调用方给出。
				return OpenFolder(title, basePath);
	}

			/// <summary>
			/// 单文件选择的过滤器：只保留一个「所有文件」入口。
			///
			/// <para>游戏目录里的输入形态太多（exe / assets / bundle / apk / zip / 散落的
			/// resource 文件），列出具体扩展名反而会让用户以为只能选那几种。
			/// 过滤器里显式写 <c>*</c> 而不是只给 <c>IncludeAllFiles</c>：
			/// 后者是把兜底项<b>追加到末尾</b>，首选项仍是第一个具体扩展名，
			/// 对话框默认会按它过滤，用户看不到别的文件。</para>
			/// </summary>
			internal static OpenFileFilter BuildOpenFileFilter() => new()
			{
				Filter = new Dictionary<string, List<string>>
				{
					[LocalizedText.Get("dialog.allFiles")] = ["*"],
				},
				IncludeAllFiles = true,
			};

			private static string ResolveBasePath(string? basePath)
	{
		if (!string.IsNullOrEmpty(basePath) && Directory.Exists(basePath))
		{
			return basePath;
		}
		return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
	}
}