using AssetRipper.Export.Configuration;
using AssetRipper.GUI.Web;
using AssetRipper.Import.Logging;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AssetRipperPatcher.Api;

/// <summary>
/// 长任务（导入 / 导出）的后台执行器。
///
/// <para><b>为什么要自己管线程。</b>AssetRipper 的导出是同步阻塞调用，且进度只通过
/// <see cref="LogCategory.ExportProgress"/> 的日志行体现（形如 <c>(12/340) Exporting 'xxx'</c>）。
/// 这里把调用放到后台任务，同时解析那些日志行折算成百分比，前端就能画出真正的进度条，
/// 而不是一个转圈。</para>
/// </summary>
internal sealed partial class OperationService : ILogger
{
	public static OperationService Instance { get; } = new();

	private readonly object _lock = new();
	private OperationDto? _current;
	private CancellationTokenSource? _cts;

	[GeneratedRegex(@"\((\d+)/(\d+)\)", RegexOptions.CultureInvariant)]
	private static partial Regex ProgressPattern();

	public void Attach() => Logger.Add(this);
	public void Detach() => Logger.Remove(this);

	public OperationDto? Current
	{
		get
		{
			lock (_lock)
			{
				return _current;
			}
		}
	}

	public bool IsBusy => Current is { State: "running" };

	/// <summary>启动导入。返回 false 表示已有任务在跑。</summary>
	public bool StartLoad(string[] paths, out string? error)
	{
		error = null;
		if (paths.Length == 0)
		{
			error = "没有提供路径。";
			return false;
		}
		foreach (string path in paths)
		{
			if (!Directory.Exists(path) && !File.Exists(path))
			{
				error = $"路径不存在：{path}";
				return false;
			}
		}

		return Start("load", "正在导入", () =>
		{
			GameFileLoader.Headless = false;
			GameFileLoader.LoadAndProcess(paths);
			AssetCatalog.Invalidate();
			return null;
		});
	}

	public bool StartExport(string outputPath, bool createSubfolder, out string? error)
	{
		error = null;
		if (string.IsNullOrWhiteSpace(outputPath))
		{
			error = "没有提供输出目录。";
			return false;
		}
		if (!GameFileLoader.IsLoaded)
		{
			error = "还没有导入任何文件。";
			return false;
		}

		string target = createSubfolder
			? Path.Combine(outputPath, $"AssetRipper_export_{DateTime.Now:yyyyMMdd_HHmmss}")
			: outputPath;

		return Start("export", "正在导出", () => { GameFileLoader.ExportUnityProject(target).GetAwaiter().GetResult(); return target; });
	}

	private bool Start(string kind, string title, Func<string?> body)
	{
		lock (_lock)
		{
			if (_current is { State: "running" })
			{
				return false;
			}

			_cts = new CancellationTokenSource();
			_current = new OperationDto(
				Id: Guid.NewGuid().ToString("N"),
				Kind: kind,
				State: "running",
				Percent: 0,
				Message: title,
				OutputPath: null,
				Error: null,
				StartedAt: DateTime.Now.ToString("O"),
				FinishedAt: null);
		}

		_ = Task.Run(() =>
		{
			try
			{
				string? output = body();
				SetFinished("done", null, output);
			}
			catch (Exception ex)
			{
				Logger.Error(LogCategory.General, $"{title}失败：{ex.Message}");
				SetFinished("failed", ex.ToString(), null);
			}
		});

		return true;
	}

	private void SetFinished(string state, string? error, string? outputPath)
	{
		lock (_lock)
		{
			if (_current is not null)
			{
				_current = _current with
				{
					State = state,
					Percent = state == "done" ? 100 : _current.Percent,
					Error = error,
					OutputPath = outputPath ?? _current.OutputPath,
					FinishedAt = DateTime.Now.ToString("O"),
				};
			}
		}
	}

	/// <summary>进度日志转百分比。只在有任务运行时才解析。</summary>
	public void Log(LogType type, LogCategory category, string message)
	{
			if (message is null)
		{
			return;
		}

			// 导出进度带 (done/total)，直接换算。
			if (category == LogCategory.ExportProgress)
		{
				Match match = ProgressPattern().Match(message);
				if (!match.Success)
				{
					return;
				}
				if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int done)
					|| !int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int total)
					|| total <= 0)
				{
					return;
				}

				lock (_lock)
				{
					if (_current is { Kind: "export", State: "running" })
					{
						_current = _current with
						{
							Percent = Math.Clamp(done * 100.0 / total, 0, 99),
							Message = message,
						};
					}
				}
				return;
			}

			// 导入没有自带的百分比，只能按阶段折算：上游会按顺序打印
			// 「Collecting game files… → Finished reading files → Processing… →
			// Finished processing assets」这几行，各阶段的耗时分布很不均匀。
			if (category == LogCategory.Import || category == LogCategory.Processing)
			{
				double? stage = ImportStagePercent(category, message);
				if (stage is null)
				{
					return;
				}
				lock (_lock)
				{
					if (_current is { Kind: "load", State: "running" })
					{
						// 进度只增不减，避免某个阶段的日志顺序抖动让进度条倒退。
						double next = Math.Clamp(stage.Value, _current.Percent, 99);
						_current = _current with { Percent = next, Message = message };
					}
				}
			}
		}

		/// <summary>
		/// 把导入阶段的日志行折算成百分比。
		///
		/// <para>权重来自实测：读取与解包通常占大头（尤其 IL2Cpp 游戏），处理阶段次之，
		/// 收尾很快。日志匹配不到就返回 null，让上一次的进度保持不变。</para>
		/// </summary>
		private static double? ImportStagePercent(LogCategory category, string message)
		{
			if (category == LogCategory.Import)
			{
				if (message.Contains("Collecting game files", StringComparison.Ordinal))
				{
					return 2;
				}
				if (message.Contains("Collecting Streaming Assets", StringComparison.Ordinal))
				{
					return 6;
				}
				if (message.Contains("Attempting to read files", StringComparison.Ordinal)
					|| message.Contains("game structure has been found", StringComparison.Ordinal))
				{
					return 10;
				}
				if (message.Contains("scripting backend", StringComparison.Ordinal)
					|| message.Contains("found Unity version", StringComparison.Ordinal)
					|| message.Contains("assemblies", StringComparison.Ordinal))
				{
					return 25;
				}
				if (message.Contains("Finished reading files", StringComparison.Ordinal))
				{
					return 55;
				}
				return null;
			}

			// Processing：这些行是固定顺序的，逐个抬一档。
			if (message.Contains("Processing loaded assets", StringComparison.Ordinal)) return 58;
			if (message.Contains("Main Asset Pairing", StringComparison.Ordinal)) return 66;
			if (message.Contains("Scene Definitions", StringComparison.Ordinal)) return 72;
			if (message.Contains("AnimatorController Assets", StringComparison.Ordinal)) return 78;
			if (message.Contains("AudioMixer Assets", StringComparison.Ordinal)) return 83;
			if (message.Contains("Scriptable Object Groups", StringComparison.Ordinal)) return 87;
			if (message.Contains("Editor Format Conversion", StringComparison.Ordinal)) return 91;
			if (message.Contains("Lighting Data Assets", StringComparison.Ordinal)) return 95;
			if (message.Contains("Finished processing assets", StringComparison.Ordinal)) return 99;
			return null;
		}

	public void BlankLine(int numLines)
	{
	}
}