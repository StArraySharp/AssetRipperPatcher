using AssetRipper.GUI.Web;
using AssetRipper.Import.Logging;
using AssetRipperPatcher.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Photino.NET;
using System.Diagnostics;
using System.Text.Json;

namespace AssetRipperPatcher.WebUi;

/// <summary>
/// 新 Web UI 的宿主：Kestrel 提供 <c>/api</c> + 静态资源，Photino 只是套在外面的原生窗口。
///
/// <para><b>为什么 Kestrel 不做成可选。</b>窗口和浏览器共用同一套 HTTP 端点后，
/// 「在浏览器里调试界面」和「在原生窗口里跑」是同一份代码路径。
/// 出问题时用 <c>--web</c> 打到浏览器里开 DevTools 即可，不需要另外一套调试入口。</para>
/// </summary>
internal static class WebHost
{
	private const string WindowTitle = "AssetRipperPatcher";

	public static int Run(string[] args, bool browserOnly)
	{
		HostEnvironment.BrowserOnly = browserOnly;

		LogService.Instance.Attach();
		OperationService.Instance.Attach();

		WebApplication app = BuildApplication();
		app.Start();

		string address = app.Services.GetRequiredService<IServer>()
			.Features.Get<IServerAddressesFeature>()!
			.Addresses.First();
		Logger.Info(LogCategory.General, $"Web UI listening on {address}");

		string startUrl = $"{address}/";

			// 调试场景（--web --port N --no-open）下把地址打到 stdout，脚本能直接抓。
			Console.WriteLine($"WEB_UI_URL={startUrl}");

			try
			{
				if (browserOnly)
				{
					if (!AppState.NoAutoOpen)
					{
						OpenInBrowser(startUrl);
					}
					Console.WriteLine($"Web UI: {startUrl}  (Ctrl+C 结束)");
					app.WaitForShutdown();
				}
				else if (AppState.NoAutoOpen)
				{
					Console.WriteLine($"Web UI: {startUrl}  (--no-open，窗口未启动)");
					app.WaitForShutdown();
				}
				else
				{
					RunWindow(app, startUrl);
				}
			}
		finally
		{
			app.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
		}

		return 0;
	}

	private static WebApplication BuildApplication()
	{
		WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
		builder.Logging.ClearProviders();
		// 0 = 随机空闲端口。端口只绑定 loopback，不对外暴露。
				builder.WebHost.UseUrls($"http://127.0.0.1:{AppState.UiPort}");
		builder.Services.ConfigureHttpJsonOptions(options =>
		{
			options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default);
			options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
		});

		WebApplication app = builder.Build();

		// 静态资源目录：开发时用源码里的 wwwroot，发布后用 exe 旁边的。
		string webRoot = ResolveWebRoot();
		if (Directory.Exists(webRoot))
		{
			PhysicalFileProvider provider = new(webRoot);
			app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
			app.UseStaticFiles(new StaticFileOptions
			{
				FileProvider = provider,
				// 界面资源改得勤，开发时绝不能被 WebView2 缓存住。
				OnPrepareResponse = static context =>
				{
					context.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
					context.Context.Response.Headers.Pragma = "no-cache";
				},
			});
		}
		else
		{
			Logger.Warning(LogCategory.General, $"Web UI assets not found: {webRoot}");
		}

		ApiEndpoints.Map(app);
		return app;
	}

	private static string ResolveWebRoot()
	{
			// 开发期优先用源码里的 wwwroot：改 CSS/JS 后只需刷新界面，
			// 不用重新 build 让 MSBuild 把文件再拷一份到 bin。
			// Release/发布走输出目录，那里才是自包含的。
	#if DEBUG
			if (FindProjectDirectory() is { } project)
			{
				string source = Path.Combine(project, "WebUi", "wwwroot");
				if (Directory.Exists(source))
				{
					return source;
				}
			}
	#endif
			string published = Path.Combine(AppContext.BaseDirectory, "wwwroot");
			if (Directory.Exists(published))
			{
				return published;
			}

			if (FindProjectDirectory() is { } fallback)
			{
				string source = Path.Combine(fallback, "WebUi", "wwwroot");
				if (Directory.Exists(source))
				{
					return source;
				}
			}
			return published;
		}

	private static string? FindProjectDirectory()
	{
		DirectoryInfo? current = new(AppContext.BaseDirectory);
		while (current is not null)
		{
			if (File.Exists(Path.Combine(current.FullName, "AssetRipperPatcher.csproj")))
			{
				return current.FullName;
			}
			current = current.Parent;
		}
		return null;
	}

	private static void RunWindow(WebApplication app, string startUrl)
	{
		if (!HostEnvironment.WebView2Available)
		{
			Logger.Warning(LogCategory.General, "WebView2 runtime not detected; falling back to the browser.");
			OpenInBrowser(startUrl);
			app.WaitForShutdown();
			return;
		}

		PhotinoWindow window = new();
		window.SetTitle(WindowTitle)
			.SetSize(1440, 920)
			.SetMinSize(960, 640)
			
			.SetResizable(true)
			.SetContextMenuEnabled(false)
			.SetDevToolsEnabled(true)
			// 0 = 安静。Photino 默认每调一个 Set* 都打一行，对最终用户是噪音。
			.SetLogVerbosity(0)
			.RegisterWebMessageReceivedHandler((sender, message) =>
			{
				try
				{
					HandleWebMessage((PhotinoWindow)sender!, message);
				}
				catch (Exception ex)
				{
					Logger.Error(LogCategory.General, $"UI 消息处理失败：{ex.Message}");
				}
			})
			.Load(startUrl);

		window.WindowCreated += (_, _) =>
			Logger.Info(LogCategory.General, "Native window created");

		window.WaitForClose();
		Logger.Info(LogCategory.General, "Window closed");
	}

	/// <summary>
	/// 前端 → 原生。用 <c>{id, action, args}</c> 协议做请求/响应，
	/// 这样 JS 可以 <c>await</c> 原生对话框的返回值，而不用猜哪个消息是回执。
	/// </summary>
	private static void HandleWebMessage(PhotinoWindow window, string message)
	{
		IpcRequest? request;
		try
		{
			request = JsonSerializer.Deserialize<IpcRequest>(message, ApiJsonContext.Default.IpcRequest);
		}
		catch (JsonException)
		{
			// 不是我们协议的报文（例如简单字符串），忽略即可。
			return;
		}

		if (request?.Action is null)
		{
			return;
		}

		switch (request.Action)
		{
			case "window.close":
				window.Close();
				break;

			case "window.minimize":
				window.SetMinimized(true);
				break;

			case "window.maximize":
				window.SetMaximized(!window.Maximized);
				break;

			case "dialog.openFolder":
							Respond(window, request.Id, DialogService.OpenFolder(LocalizedText.Get("dialog.pickGameFolder")));
				break;

			case "dialog.openFile":
							Respond(window, request.Id, DialogService.OpenFile(LocalizedText.Get("dialog.pickGameFile")));
				break;

			case "dialog.saveFile":
							Respond(window, request.Id, DialogService.PickOutputFolder(LocalizedText.Get("dialog.pickOutput")));
				break;

			case "shell.openFolder":
				if (request.Args is { Length: > 0 } && Directory.Exists(request.Args[0]))
				{
					Process.Start(new ProcessStartInfo(request.Args[0]) { UseShellExecute = true });
				}
				break;

			case "shell.openUrl":
				if (request.Args is { Length: > 0 })
				{
					OpenInBrowser(request.Args[0]);
				}
				break;
		}
	}

	/// <summary>原生对话框现在由 <see cref="DialogService"/> 统一提供，这里只负责把结果回传。</summary>
		private static void Respond(PhotinoWindow window, string? id, DialogResultDto result)
		{
			if (string.IsNullOrEmpty(id))
			{
				return;
			}
			window.SendWebMessage(JsonSerializer.Serialize(
				new IpcResponse(id, result.Unsupported, result.Canceled, result.Paths),
				ApiJsonContext.Default.IpcResponse));
		}

	private static void OpenInBrowser(string url)
	{
		try
		{
			Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.General, $"无法打开浏览器：{ex.Message}。请手动访问 {url}");
		}
	}
}
