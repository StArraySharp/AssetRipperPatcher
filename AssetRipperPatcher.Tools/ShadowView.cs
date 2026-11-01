using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AssetRipperPatcher.Tools;

/// <summary>
/// 「全量元数据」编译视图。
///
/// 默认编译只导入被引用程序集的 public/internal 成员（<see cref="MetadataImportOptions.Internal"/>），
/// 而本系统的候选集语义要求全量成员（补丁目标大量是 private）。这里用
/// <see cref="Compilation.WithOptions"/> 造一个共享同一批语法树与引用的影子编译，
/// 符号侧看到的成员集才与运行时反射一致。
///
/// <para><b>ref assembly 的问题与解决。</b>
/// 仅设 <c>MetadataImportOptions.All</c> 还不够：项目一旦生成引用程序集
/// （<c>obj/&lt;cfg&gt;/ref/*.dll</c>），那些 dll 在**物理上**就剔除了 private 成员，
/// Roslyn 再怎么看也看不到，于是所有 private 补丁目标都会误报 HRP001。
///
/// 传统做法是给上游工程加 <c>ProduceReferenceAssembly=false</c>，但那需要改 git submodule
/// 里的文件（每次 <c>git pull</c> 都可能冲突、且对每个克隆者都要重做一遍）。
/// 本类改为在分析器内部消化：对每个指向 <c>obj/&lt;cfg&gt;/ref/</c> 的引用，
/// 换成同一项目 <c>obj/&lt;cfg&gt;/</c> 下的完整程序集（若已生成），
/// 从而**不需要任何构建配置改动**。
/// </para>
///
/// 分析器与快速修复都必须走这里，否则两边对「有哪些成员」的判断会不一致。
/// </summary>
internal static class ShadowView
{
    private static readonly ConditionalWeakTable<Compilation, Compilation> Cache = new();

    public static Compilation Of(Compilation compilation)
    {
        var options = (CSharpCompilationOptions)compilation.Options;
        if (options.MetadataImportOptions == MetadataImportOptions.All)
        {
            return compilation;
        }
        return Cache.GetValue(compilation, Upgrade);
    }

    private static Compilation Upgrade(Compilation compilation)
    {
        var options = (CSharpCompilationOptions)compilation.Options;

        // 1) 先把 ref assembly 换成完整程序集（ref assembly 缺 private 元数据）
        List<MetadataReference> references = ReplaceReferenceAssemblies(compilation.References);

        // 2) 再放开元数据导入级别
        return compilation
            .RemoveAllReferences()
            .AddReferences(references)
            .WithOptions(options.WithMetadataImportOptions(MetadataImportOptions.All));
    }

    /// <summary>
    /// 把 <c>.../obj/&lt;cfg&gt;/ref/Foo.dll</c> 换成 <c>.../obj/&lt;cfg&gt;/Foo.dll</c>（完整程序集）。
    /// 找不到完整程序集时保留原引用。
    /// </summary>
    private static List<MetadataReference> ReplaceReferenceAssemblies(IEnumerable<MetadataReference> references)
    {
        List<MetadataReference> result = new();
        foreach (MetadataReference reference in references)
        {
            string? fullPath = TryGetFullAssemblyPath(reference);
            if (fullPath is not null)
            {
                MetadataReference? replacement = TryCreateReference(fullPath);
                if (replacement is not null)
                {
                    result.Add(replacement);
                    continue;
                }
            }
            result.Add(reference);
        }
        return result;
    }

    /// <summary>
    /// 由 ref assembly 路径推出完整程序集路径；不是 ref assembly 或完整程序集不存在时返回 null。
    /// </summary>
    private static string? TryGetFullAssemblyPath(MetadataReference reference)
    {
        if (reference is not PortableExecutableReference portable || portable.FilePath is null)
        {
            return null;
        }

        string path = portable.FilePath;
        string? refDirectory = Path.GetDirectoryName(path);
        if (refDirectory is null
            || !string.Equals(Path.GetFileName(refDirectory), "ref", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // .../obj/<cfg>/ref/Foo.dll  ->  .../obj/<cfg>/Foo.dll
        string? configurationDirectory = Path.GetDirectoryName(refDirectory);
        if (configurationDirectory is null)
        {
            return null;
        }

        string candidate = Path.Combine(configurationDirectory, Path.GetFileName(path));
        return File.Exists(candidate) ? candidate : null;
    }

    private static MetadataReference? TryCreateReference(string path)
    {
        try
        {
            return MetadataReference.CreateFromFile(path);
        }
        catch
        {
            // 文件被占用 / 正在写入：放弃替换，保留原 ref 引用
            return null;
        }
    }
}
