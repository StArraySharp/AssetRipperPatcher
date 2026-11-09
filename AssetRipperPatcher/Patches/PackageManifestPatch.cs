using System.Text.Json;
using System.Text.Json.Nodes;
using AssetRipper.Export.UnityProjects.Project;
using AssetRipper.Primitives;
using AssetRipperPatcher.Patching;

namespace AssetRipperPatcher.Patches;

/// <summary>
/// PackageManifest 补丁：导出 manifest.json 时注入 IDE 与 UGUI 包依赖，
/// 并提供「导出后往已有清单里补依赖」的入口。
/// </summary>
internal static class PackageManifestPatch
{
    /// <summary>
    /// Harmony Prefix: 在导出 manifest.json 时注入 IDE 和 UGUI 包依赖
    /// </summary>
    [PatchMethod(typeof(PackageManifestPostExporter), "CreateManifest", PatchType = PatchType.Prefix)]
    public static bool PatchCreateManifest(UnityVersion version, ref PackageManifest __result)
    {
        // 先调用原始逻辑获取默认清单
        __result = PackageManifest.CreateDefault(version);

        // 注入 IDE 支持
        __result.Dependencies.TryAdd("com.unity.ide.rider", "3.0.31");
        __result.Dependencies.TryAdd("com.unity.ide.visualstudio", "2.0.22");

        // 仅 Unity 6+ 注入 UGUI（UGUI 2.0+ 自带 TMP，所以 TMP 不单独声明）。
        // 版本号从包目录里取：UGUI 3.x 与 2.x 是两个不同的包版本，
        // 硬编码会在 Unity 6 项目里写进一个装不上的版本。
        if (AppState.IsUnity6
            && UnityPackageCatalog.TryResolvePackage("UnityEngine.UI", out UnityPackage? ugui))
        {
            // 注册表里的 UGUI 只有 3.0.0-exp.4，真装上不去；换成可用的稳定版（2.0.0）。
            string uguiVersion = UnityPackageCatalog.GetManifestVersion(ugui.version);
            if (!string.Equals(uguiVersion, ugui.version, StringComparison.Ordinal))
            {
                Console.WriteLine(
                    $"[Packages] {ugui.name}: {ugui.version} 是实验版，改用稳定版 {uguiVersion}");
            }

            __result.Dependencies.TryAdd(ugui.name, uguiVersion);

            // UGUI 2.0+ 之后 TMP 已内置；若清单里同时出现两者，会各带一份
            // Unity.TextMeshPro.dll → CS0433。按版本门槛剔除。
            if (UnityPackageCatalog.Supersedes(ugui.name, uguiVersion, "com.unity.textmeshpro")
                && __result.Dependencies.Remove("com.unity.textmeshpro"))
            {
                Console.WriteLine(
                    $"[Packages] {ugui.name}@{uguiVersion} 已内置 TextMeshPro，" +
                    "跳过 com.unity.textmeshpro");
            }
        }

        return false; // 跳过原始方法
    }

    /// <summary>
    /// 往**已导出**的 <c>Packages/manifest.json</c> 里补依赖。
    ///
    /// <para><b>为什么需要单独一步。</b>
    /// 清单由 <see cref="PackageManifestPostExporter"/> 在导出早段写出，
    /// 而「裸 DLL 换成包」要等 <see cref="NativePluginsPatch"/> 拷完插件才知道结果。
    /// 那时清单已经在磁盘上了，只能在导出结束后原地改。</para>
    ///
    /// <para>已有的键一律不覆盖（尊重用户/上游的选择），只补缺失的。</para>
    /// </summary>
    /// <param name="outputPath">导出根目录（含 <c>ExportedProject</c>）。</param>
    /// <param name="packages">包名 → 版本；版本为空表示只写包名。</param>
    /// <returns>实际新增的条目数。</returns>
    public static int AddDependencies(string outputPath, IReadOnlyDictionary<string, string> packages)
    {
        if (packages.Count == 0)
        {
            return 0;
        }

        string manifestPath = Path.Combine(outputPath, "ExportedProject", "Packages", "manifest.json");
        if (!File.Exists(manifestPath))
        {
            Console.WriteLine($"[Packages] manifest not found at '{manifestPath}', skipped");
            return 0;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(manifestPath));
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[Packages] failed to parse '{manifestPath}': {ex.Message}");
            return 0;
        }

        if (root is not JsonObject obj)
        {
            Console.WriteLine($"[Packages] '{manifestPath}' is not a JSON object, skipped");
            return 0;
        }

        if (obj["dependencies"] is not JsonObject dependencies)
        {
            dependencies = new JsonObject();
            obj["dependencies"] = dependencies;
        }

        int added = 0;
        foreach ((string name, string version) in packages)
        {
            if (dependencies.ContainsKey(name))
            {
                continue;
            }
            dependencies[name] = version;
            added++;
            Console.WriteLine($"[Packages] + {name}{(string.IsNullOrEmpty(version) ? "" : $"@{version}")}");
        }

        // 最后统一去重：装了 com.unity.ugui 2.0+ 就不该再有 com.unity.textmeshpro。
        // 放在最后是因为要看到「合并后的完整清单」才能判断，而上面的循环是逐条补的。
        RemoveSupersededDependencies(dependencies);

        if (added == 0)
        {
            return 0;
        }

        File.WriteAllText(manifestPath, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return added;
    }

    /// <summary>
    /// 从清单里剔除「被别的包内置」的条目，并把结果打出来。
    /// </summary>
    private static void RemoveSupersededDependencies(JsonObject dependencies)
    {
        Dictionary<string, string> snapshot = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, JsonNode? node) in dependencies.ToList())
        {
            snapshot[name] = node?.GetValue<string>() ?? string.Empty;
        }

        foreach (string name in UnityPackageCatalog.RemoveSuperseded(snapshot))
        {
            dependencies.Remove(name);
            Console.WriteLine($"[Packages] - {name}（已被清单里的其它包内置）");
        }
    }
}
