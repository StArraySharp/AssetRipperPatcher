using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace AssetRipperPatcher.Tools.UnityPackages;

/// <summary>
/// Unity 包注册表客户端：查版本、下载 tarball、解出文件。
///
/// <para>注册表是公开的 npm-style endpoint（<c>https://packages.unity.com</c>），
/// 但没有全量列表接口（<c>/-/all</c> 和 <c>/-/v1/search</c> 都是 404），
/// 所以包名清单由 <c>tools/unity-packages.txt</c> 提供。</para>
/// </summary>
internal sealed class UnityRegistry
{
    private const string Endpoint = "https://packages.unity.com";

    private readonly HttpClient m_http;

    public UnityRegistry()
    {
        m_http = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
        m_http.DefaultRequestHeaders.Add("User-Agent", "AssetRipperPatcher-UnityPackageHarvester");
    }

    /// <summary>一个包的下载计划。</summary>
    public readonly record struct PackagePlan(string Name, string Version, string TarballUrl, string CachePath);

    /// <summary>查一个包的元数据，返回可用的下载计划；没有可用版本时返回 null。</summary>
    /// <param name="packageName">UPM 包名。</param>
    /// <param name="pinnedVersion">
    /// 显式指定的版本（来自 seed 清单的 <c>name@version</c> 写法）。
    /// 非空时直接用这个版本，**不再过滤预发布**——用户显式点名就是要它。
    /// </param>
    /// <param name="cacheDir">tarball 缓存目录。</param>
    public async Task<PackagePlan?> ResolveAsync(string packageName, string? pinnedVersion, string cacheDir)
    {
        JsonDocument document;
        try
        {
            using HttpResponseMessage response = await m_http.GetAsync($"{Endpoint}/{packageName}");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        }
        catch
        {
            return null;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("versions", out JsonElement versions))
            {
                return null;
            }

            string? selected;
            if (!string.IsNullOrEmpty(pinnedVersion))
            {
                // 显式 pin：只要求这个版本存在。预发布也接受——UGUI 这类包只有 exp 版。
                selected = versions.TryGetProperty(pinnedVersion, out _) ? pinnedVersion : null;
            }
            else
            {
                // 自动挑：只接受正式版（版本号里没有 '-'）。
                // 预发布包在正式工程里会带来不兼容 API 和无法复现的依赖图，
                // 所以「只有预发布」的包整个跳过，而不是退而求其次。
                selected = versions.EnumerateObject()
                    .Select(static p => p.Name)
                    .Where(static v => !v.Contains('-', StringComparison.Ordinal))
                    .OrderByDescending(ParseVersion)
                    .FirstOrDefault();
            }

            if (selected is null
                || !versions.TryGetProperty(selected, out JsonElement version)
                || !version.TryGetProperty("dist", out JsonElement dist)
                || !dist.TryGetProperty("tarball", out JsonElement tarball)
                || tarball.GetString() is not { Length: > 0 } url)
            {
                return null;
            }

            return new PackagePlan(packageName, selected, url, Path.Combine(cacheDir, $"{packageName}-{selected}.tgz"));
        }
    }

    /// <summary>版本号排序键。解析不了的排到最后。</summary>
    private static Version ParseVersion(string text)
    {
        int cut = text.IndexOfAny(['-', '+']);
        string core = cut >= 0 ? text[..cut] : text;
        return Version.TryParse(core, out Version? parsed) ? parsed : new Version(0, 0);
    }

    /// <summary>下载 tarball（已缓存则跳过）。</summary>
    public async Task<bool> DownloadAsync(PackagePlan plan, bool refresh)
    {
        if (!refresh && File.Exists(plan.CachePath))
        {
            return true;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(plan.CachePath)!);
            using HttpResponseMessage response = await m_http.GetAsync(plan.TarballUrl);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }
            await using Stream source = await response.Content.ReadAsStreamAsync();
            await using FileStream target = File.Create(plan.CachePath);
            await source.CopyToAsync(target);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 把 tarball 解到一个临时目录，返回该目录；调用方负责删除。
    ///
    /// <para>用 <see cref="TarReader"/> 而不是调外部 <c>tar</c>：
    /// 工具要能在没有 tar 的环境里跑（Windows 10 之前没有，CI 镜像也可能没有），
    /// 而且直接读流可以跳过不需要的条目。</para>
    /// </summary>
    public static async Task<string?> ExtractAsync(string archivePath, string destination)
    {
        try
        {
            await using FileStream file = File.OpenRead(archivePath);
            await using GZipStream gzip = new(file, CompressionMode.Decompress);
            using TarReader reader = new(gzip, leaveOpen: false);

            Directory.CreateDirectory(destination);

            TarEntry? entry;
            while ((entry = await reader.GetNextEntryAsync()) is not null)
            {
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                {
                    continue;
                }
                if (entry.DataStream is null)
                {
                    continue;
                }

                // tarball 里的路径以 "package/" 开头；去掉以免多一层目录。
                string relative = entry.Name.StartsWith("package/", StringComparison.Ordinal)
                    ? entry.Name["package/".Length..]
                    : entry.Name;
                if (relative.Length == 0)
                {
                    continue;
                }

                string target = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using FileStream output = File.Create(target);
                await entry.DataStream.CopyToAsync(output);
            }
            return destination;
        }
        catch
        {
            return null;
        }
    }
}
