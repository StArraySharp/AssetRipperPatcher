using System.Text.Json.Serialization;

namespace AssetRipperPatcher.Api;

// JSON 传输对象。全部用 record + 明确的 JSON 属性名，前端不需要猜字段。
// 这些类型只描述「给 UI 看什么」，不映射 AssetRipper 的内部模型，所以不需要
// 跟着子模块的命名走。

/// <summary>应用与运行环境信息。</summary>
public sealed record StatusDto(
	string App,
	string Version,
	string? UnityVersion,
	bool Loaded,
	bool Exporting,
	string? WebView2,
	string? Shell,
	string? SettingsPath);

/// <summary>一次导入的顶层统计。</summary>
public sealed record StatsDto(
	int Collections,
	int Assets,
	int Bundles,
	int Resources,
	int FailedFiles,
	int Scenes);

/// <summary>资源树里的一级节点（bundle 或 collection）。</summary>
public sealed record TreeNodeDto(
	string Id,
	string Kind,
	string Name,
	int? Count,
	IReadOnlyList<TreeNodeDto>? Children);

/// <summary>资源列表中的一行。字段刻意保持精简：列表可能上万行。</summary>
public sealed record AssetRowDto(
	long PathId,
	int ClassId,
	string ClassName,
	string Name,
	string Collection,
	string CollectionPath,
	string? OriginalPath,
	string? Extension,
	int? SizeBytes,
	int? Width,
	int? Height,
	string? Preview,
	string? Audio,
	string? Model,
	string? Font,
	string? Video);

/// <summary>分页结果。</summary>
public sealed record PageDto<T>(
	IReadOnlyList<T> Items,
	int Total,
	int Offset,
	int Limit);

/// <summary>资源详情：属性表 + 可用预览。</summary>
public sealed record AssetDetailDto(
	long PathId,
	int ClassId,
	string ClassName,
	string Name,
	string Collection,
	string CollectionPath,
	string? OriginalPath,
	IReadOnlyList<PropertyDto> Properties,
	IReadOnlyList<string> Previews);

public sealed record PropertyDto(string Name, string Value);

/// <summary>设置项的一个枚举选项，带显示名和说明。</summary>
public sealed record SettingOptionDto(
	string Value,
	string Label,
	string? Description);

/// <summary>设置项。type 决定前端渲染成什么控件。</summary>
public sealed record SettingDto(
	string Key,
	string Label,
	string? Description,
	string Type,
	string? Value,
	IReadOnlyList<SettingOptionDto>? Options);

public sealed record SettingsDto(
	bool ReadOnly,
	IReadOnlyList<SettingGroupDto> Groups);

public sealed record SettingGroupDto(string Id, IReadOnlyList<SettingDto> Items);

/// <summary>界面文案包：前端启动时拉一次，之后所有标签都从这儿取。</summary>
public sealed record UiTextDto(
	string Language,
	IReadOnlyList<LanguageOptionDto> Languages,
	Dictionary<string, string> Strings);

public sealed record LanguageOptionDto(string Code, string Name);

/// <summary>一条日志。</summary>
public sealed record LogEntryDto(long Seq, string Time, string Level, string Category, string Message);

/// <summary>类名直方图的一项（左侧「资源类型」面板）。</summary>
public sealed record ClassCountDto(string Name, int Count);

/// <summary>前端 → 原生窗口的 IPC 报文。</summary>
public sealed record IpcRequest(string? Id, string? Action, string[]? Args);

/// <summary>原生窗口 → 前端的 IPC 回执。</summary>
public sealed record IpcResponse(string Id, bool Unsupported, bool Canceled, IReadOnlyList<string> Paths);

/// <summary>导出/导入进度。前端轮询或 SSE 读取。</summary>
public sealed record OperationDto(
	string Id,
	string Kind,
	string State,
	double Percent,
	string? Message,
	string? OutputPath,
	string? Error,
	string StartedAt,
	string? FinishedAt);

/// <summary>目录/文件对话框的返回值。</summary>
/// <param name="Unsupported">当前平台/环境不支持原生对话框，前端应退回手输路径。</param>
/// <param name="Canceled">用户取消。</param>
public sealed record DialogResultDto(
	bool Unsupported,
	bool Canceled,
	IReadOnlyList<string> Paths);

// ---- 请求体 ----

public sealed record LoadRequest(string[] Paths);
public sealed record ExportRequest(string OutputPath, bool CreateSubfolder);
public sealed record SettingsUpdateRequest(Dictionary<string, string?> Values);
public sealed record LanguageRequest(string? Language);

[JsonSerializable(typeof(StatusDto))]
[JsonSerializable(typeof(StatsDto))]
[JsonSerializable(typeof(TreeNodeDto))]
[JsonSerializable(typeof(AssetRowDto))]
[JsonSerializable(typeof(PageDto<AssetRowDto>))]
[JsonSerializable(typeof(AssetDetailDto))]
[JsonSerializable(typeof(PropertyDto))]
[JsonSerializable(typeof(SettingsDto))]
[JsonSerializable(typeof(SettingDto))]
[JsonSerializable(typeof(SettingOptionDto))]
[JsonSerializable(typeof(SettingGroupDto))]
[JsonSerializable(typeof(UiTextDto))]
[JsonSerializable(typeof(LanguageOptionDto))]
[JsonSerializable(typeof(LogEntryDto))]
[JsonSerializable(typeof(OperationDto))]
[JsonSerializable(typeof(ClassCountDto))]
[JsonSerializable(typeof(DialogResultDto))]
[JsonSerializable(typeof(IpcRequest))]
[JsonSerializable(typeof(IpcResponse))]
[JsonSerializable(typeof(LoadRequest))]
[JsonSerializable(typeof(ExportRequest))]
[JsonSerializable(typeof(SettingsUpdateRequest))]
[JsonSerializable(typeof(LanguageRequest))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(object))]
internal sealed partial class ApiJsonContext : JsonSerializerContext
{
}