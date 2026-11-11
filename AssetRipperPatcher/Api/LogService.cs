using AssetRipper.Import.Logging;

namespace AssetRipperPatcher.Api;

/// <summary>
/// 进程内日志环形缓冲 + 增量订阅。
///
/// <para><b>为什么不用 Harmony 挂钩。</b><see cref="Logger.Add"/> 是 public 的，
/// 注册一个 <see cref="ILogger"/> 就能拿到全部输出，比自己拦方法稳。
/// 注意 <see cref="Logger.AllowVerbose"/> 默认 false，Verbose 条目根本不会走到这里。</para>
/// </summary>
internal sealed class LogService : ILogger
{
	public static LogService Instance { get; } = new();

	private const int Capacity = 5000;
	private readonly object _lock = new();
	private readonly Queue<LogEntryDto> _entries = new(Capacity + 1);
	private long _seq;
	private long _dropped;

	private LogService()
	{
	}

	/// <summary>把自身挂到全局日志管道上（重复调用安全）。</summary>
	public void Attach()
	{
		Logger.Add(this);
	}

	public void Detach()
	{
		Logger.Remove(this);
	}

	public void Log(LogType type, LogCategory category, string message)
	{
		if (string.IsNullOrEmpty(message))
		{
			return;
		}

		lock (_lock)
		{
			// 导出日志里常有整段带换行的异常堆栈，压成单行更好看也更容易筛。
			string text = message.ReplaceLineEndings(" ⏎ ").TrimEnd();
			_entries.Enqueue(new LogEntryDto(
				Seq: ++_seq,
				Time: DateTime.Now.ToString("HH:mm:ss"),
				Level: type switch
				{
					LogType.Error => "error",
					LogType.Warning => "warning",
					LogType.Debug => "debug",
					LogType.Verbose => "verbose",
					_ => "info",
				},
				Category: category.ToString(),
				Message: text));

			while (_entries.Count > Capacity)
			{
				_entries.Dequeue();
				_dropped++;
			}
		}
	}

	public void BlankLine(int numLines)
	{
	}

	/// <summary>取 seq 之后的条目，供前端增量拉取。</summary>
	public (IReadOnlyList<LogEntryDto> Items, long LastSeq) Since(long seq, int limit)
	{
		lock (_lock)
		{
			List<LogEntryDto> result = _entries.Where(e => e.Seq > seq).Take(limit).ToList();
			return (result, _seq);
		}
	}

	public IReadOnlyList<LogEntryDto> Tail(int count)
	{
		lock (_lock)
		{
			return _entries.Skip(Math.Max(0, _entries.Count - count)).ToList();
		}
	}

	public long DroppedCount
	{
		get
		{
			lock (_lock)
			{
				return _dropped;
			}
		}
	}
}