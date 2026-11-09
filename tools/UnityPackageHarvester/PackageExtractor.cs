using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AssetRipperPatcher.Tools.UnityPackages;

/// <summary>
/// 从一个已解包的 Unity 包里提取数据模型（<see cref="PackageModel"/>）。
///
/// <para>提取三类东西：<c>package.json</c> 的元数据、每个 <c>.asmdef</c>（程序集名 + GUID）、
/// 以及每个 <c>.cs</c> 里可序列化类型的 <c>.cs.meta</c> GUID。</para>
/// </summary>
internal static class PackageExtractor
{
    /// <summary>已知的「Unity 框架里的脚本基类」简单名。</summary>
    /// <remarks>
    /// 继承链经常跨包：TMP 3.0.9 的 <c>TextMeshPro : TMP_Text : MaskableGraphic</c>，
    /// 而 <c>MaskableGraphic</c> 在 <c>UnityEngine.UI</c> 里、不在 TMP 包内。
    /// 包内找不到基类时无法继续回溯，就会漏掉 <c>TextMeshPro</c>——恰恰是最常出现在
    /// prefab 里的类型。这里列出这些「跨包断点」，命中即视为可序列化。
    /// </remarks>
    private static readonly HashSet<string> KnownScriptBaseTypes = new(StringComparer.Ordinal)
    {
        // UnityEngine
        "MonoBehaviour",
        "ScriptableObject",
        "StateMachineBehaviour",
        "TrackAsset",
        "PlayableAsset",
        "PlayableBehaviour",
        // UnityEngine.EventSystems
        "UIBehaviour",
        "BaseRaycaster",
        "BaseInput",
        "BaseInputModule",
        "PointerInputModule",
        "StandaloneInputModule",
        // UnityEngine.UI
        "Graphic",
        "MaskableGraphic",
        "Selectable",
        "BaseMeshEffect",
        "LayoutGroup",
        "HorizontalOrVerticalLayoutGroup",
        "ScrollRect",
        // TextMeshPro
        "TMP_Text",
        "TMP_Asset",
        "TMP_InputValidator",
        // Editor
        "Editor",
        "EditorWindow",
        "PropertyDrawer",
        "ScriptableWizard",
        "AssetPostprocessor",
    };

    // ---------- package.json ----------

    /// <summary>从 <c>package.json</c> 读出包元数据。</summary>
    public static PackageModel ReadPackageJson(string packageRoot)
    {
        string path = Path.Combine(packageRoot, "package.json");
        if (!File.Exists(path))
        {
            return new PackageModel();
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;

            PackageModel model = new()
            {
                Name = GetString(root, "name") ?? string.Empty,
                DisplayName = GetString(root, "displayName") ?? string.Empty,
                Version = GetString(root, "version") ?? string.Empty,
                Unity = GetString(root, "unity") ?? string.Empty,
                Description = GetString(root, "description") ?? string.Empty,
                Category = GetString(root, "category") ?? string.Empty,
            };

            if (root.TryGetProperty("dependencies", out JsonElement dependencies)
                && dependencies.ValueKind is JsonValueKind.Object)
            {
                foreach (JsonProperty property in dependencies.EnumerateObject())
                {
                    if (property.Value.GetString() is { Length: > 0 } value)
                    {
                        model.Dependencies[property.Name] = value;
                    }
                }
            }
            return model;
        }
        catch
        {
            return new PackageModel();
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    // ---------- asmdef ----------

    /// <summary>读出包里所有 asmdef 及其 <c>.asmdef.meta</c> 的 GUID。</summary>
    public static List<AsmDefModel> ReadAsmDefs(string packageRoot)
    {
        List<AsmDefModel> result = new();
        foreach (string file in Directory.EnumerateFiles(packageRoot, "*.asmdef", SearchOption.AllDirectories))
        {
            string? name = ReadAsmDefName(file);
            if (name is null)
            {
                continue;
            }
            result.Add(new AsmDefModel(name, ReadMetaGuid(file + ".meta") ?? string.Empty, Path.GetDirectoryName(file)!));
        }
        // 目录长的优先：解析某个 .cs 归属哪个程序集时，内层 asmdef 赢。
        result.Sort(static (a, b) => b.Directory.Length.CompareTo(a.Directory.Length));
        return result;
    }

    /// <summary><c>.asmdef</c> 的 <c>name</c> 字段——即编译后的程序集名。</summary>
    private static string? ReadAsmDefName(string asmdefPath)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(asmdefPath));
            return document.RootElement.TryGetProperty("name", out JsonElement name)
                && name.GetString() is { Length: > 0 } value
                ? value
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 补齐「预编译 DLL 但没 asmdef」的程序集。
    ///
    /// <para><b>为什么需要。</b>
    /// 官方 NuGet 桥包（<c>com.unity.nuget.newtonsoft-json</c>）根本不带
    /// <c>.asmdef</c>——它直接放一个 <c>Runtime/Newtonsoft.Json.dll</c> + <c>.dll.meta</c>。
    /// 只扫 asmdef 会让这些包「查不到任何程序集」，于是
    /// <c>Assets/Plugins/Newtonsoft.Json.dll</c> 永远删不掉。</para>
    ///
    /// <para>这类程序集没有源码，所以拿不到 <c>ScriptType</c> 列表——但「DLL 名 → 包」
    /// 这条映射本身就有价值，足以让清单加上依赖。</para>
    /// </summary>
    public static void AddPrecompiledAssemblies(string packageRoot, List<AsmDefModel> asmdefs)
    {
        HashSet<string> known = new(asmdefs.Select(static a => a.Name), StringComparer.OrdinalIgnoreCase);

        foreach (string dll in Directory.EnumerateFiles(packageRoot, "*.dll", SearchOption.AllDirectories))
        {
            string name = Path.GetFileNameWithoutExtension(dll);
            if (name.Length == 0 || known.Contains(name))
            {
                continue;
            }
            // 已经被某个 asmdef 目录罩住的 DLL 是那个 asmdef 的编译产物，不要重复登记。
            if (ResolveAssembly(dll, asmdefs) is not null)
            {
                continue;
            }
            // 必须有配套 .meta：没有说明它不是 Unity 资产（可能是 Tools~ 里的构建脚本）。
            string? guid = ReadMetaGuid(dll + ".meta");
            if (guid is null)
            {
                continue;
            }

            asmdefs.Add(new AsmDefModel(name, guid, Path.GetDirectoryName(dll)!));
            known.Add(name);
        }
    }

    // ---------- .meta ----------

    /// <summary>从 <c>.meta</c> 里读 <c>guid:</c> 行。</summary>
    internal static string? ReadMetaGuid(string metaPath)
    {
        if (!File.Exists(metaPath))
        {
            return null;
        }
        try
        {
            foreach (string line in File.ReadLines(metaPath))
            {
                if (line.StartsWith("guid:", StringComparison.Ordinal))
                {
                    return line[5..].Trim();
                }
            }
        }
        catch
        {
            // 损坏的 .meta 跳过
        }
        return null;
    }

    /// <summary>把文件归属到包含它的最深 asmdef 目录。</summary>
    internal static AsmDefModel? ResolveAssembly(string file, List<AsmDefModel> asmdefs)
    {
        AsmDefModel? best = null;
        foreach (AsmDefModel asmdef in asmdefs)
        {
            if (file.StartsWith(asmdef.Directory, StringComparison.OrdinalIgnoreCase)
                && (best is null || asmdef.Directory.Length > best.Directory.Length))
            {
                best = asmdef;
            }
        }
        return best;
    }

    // ---------- 可序列化类型 ----------

    /// <summary>
    /// 收集一个包下所有可序列化类型。
    ///
    /// <para>可序列化判据（Unity 规则）：继承 <c>MonoBehaviour</c> / <c>ScriptableObject</c>，
    /// 或带 <c>[Serializable]</c> 的类 / 结构体。这些类型可能以 <c>m_Script</c>
    /// 出现在场景、prefab、<c>.asset</c> 里。</para>
    ///
    /// <para><b>为什么是两遍。</b>
    /// 继承链常常是间接的：<c>TextMeshPro</c> → <c>TMP_Text</c> → <c>MaskableGraphic</c>
    /// → <c>Graphic</c> → <c>UIBehaviour</c> → <c>MonoBehaviour</c>，链条**跨文件**。
    /// 只看直接基类会把 <c>TextMeshPro</c> 漏掉——而它恰恰是最常出现在 prefab 里的类型。
    /// 所以先收集「类型 → 基类名」的图，再沿图做传递闭包。</para>
    /// </summary>
    public static void CollectTypes(string packageRoot, List<AsmDefModel> asmdefs)
    {
        List<TypeCandidate> candidates = new();
        TypeIndex index = new();

        foreach (string file in Directory.EnumerateFiles(packageRoot, "*.cs", SearchOption.AllDirectories))
        {
            string? guid = ReadMetaGuid(file + ".meta");
            if (guid is null)
            {
                continue;
            }
            AsmDefModel? asmdef = ResolveAssembly(file, asmdefs);
            if (asmdef is null)
            {
                continue; // 不在任何 asmdef 下的零散脚本（Documentation~ 之类）
            }

            string source;
            try
            {
                source = File.ReadAllText(file);
            }
            catch
            {
                continue;
            }

            foreach (TypeCandidate candidate in ReadDeclarations(file, source, guid, asmdef))
            {
                candidates.Add(candidate);
                index.Add(candidate);
            }
        }

        foreach (TypeCandidate candidate in candidates)
        {
            if (IsSerializable(candidate, index, new HashSet<string>(StringComparer.Ordinal)))
            {
                candidate.AsmDef.Types.Add(new TypeModel(candidate.Namespace, candidate.Name, candidate.Guid));
            }
        }
    }

    /// <summary>读一个源文件里的全部类型声明（含嵌套）。</summary>
    private static IEnumerable<TypeCandidate> ReadDeclarations(
        string file, string source, string guid, AsmDefModel asmdef)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, path: file);
        foreach (TypeDeclarationSyntax type in tree.GetCompilationUnitRoot()
            .DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            string? ns = GetNamespace(type);
            if (ns is null)
            {
                continue; // 全局命名空间的类型极罕见，跳过
            }

            List<string> baseTypes = new();
            if (type.BaseList is not null)
            {
                foreach (BaseTypeSyntax baseType in type.BaseList.Types)
                {
                    baseTypes.Add(baseType.Type.ToString());
                }
            }

            yield return new TypeCandidate(
                ns,
                BuildTypeName(type),
                guid,
                asmdef,
                baseTypes,
                HasSerializableAttribute(type));
        }
    }

    private static bool IsSerializable(
        TypeCandidate candidate,
        TypeIndex index,
        HashSet<string> visiting)
    {
        if (!visiting.Add(candidate.FullName))
        {
            return false; // 继承环（编译器不允许，防御一下）
        }
        if (candidate.HasSerializableAttribute)
        {
            return true;
        }

        foreach (string baseType in candidate.BaseTypes)
        {
            if (KnownScriptBaseTypes.Contains(SimpleName(baseType)))
            {
                return true;
            }

            // 基类可能在同一包的另一个文件/另一个命名空间（`using` 之后只写简单名）。
            TypeCandidate? parent = index.Find(baseType, candidate.Namespace);
            if (parent is not null && IsSerializable(parent, index, visiting))
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasSerializableAttribute(TypeDeclarationSyntax type)
    {
        foreach (AttributeListSyntax list in type.AttributeLists)
        {
            foreach (AttributeSyntax attribute in list.Attributes)
            {
                string name = attribute.Name.ToString();
                if (name is "Serializable" or "SerializableAttribute"
                    or "System.Serializable" or "System.SerializableAttribute")
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>去掉泛型参数与命名空间，取类型简单名。</summary>
    private static string SimpleName(string typeName)
    {
        int generic = typeName.IndexOf('<');
        string text = generic >= 0 ? typeName[..generic] : typeName;
        int dot = text.LastIndexOf('.');
        return (dot >= 0 ? text[(dot + 1)..] : text).Trim();
    }

    private static string? GetNamespace(SyntaxNode node)
    {
        for (SyntaxNode? current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is BaseNamespaceDeclarationSyntax ns)
            {
                return ns.Name.ToString();
            }
        }
        return null;
    }

    private static string BuildTypeName(TypeDeclarationSyntax type)
    {
        List<string> parts = [type.Identifier.ValueText];
        for (SyntaxNode? current = type.Parent; current is not null; current = current.Parent)
        {
            if (current is TypeDeclarationSyntax outer)
            {
                parts.Insert(0, outer.Identifier.ValueText);
            }
        }
        return string.Join('+', parts);
    }

    // ---------- 入口 ----------

    /// <summary>从包目录提取完整模型。</summary>
    public static PackageModel Extract(string packageRoot, string fallbackName, string fallbackVersion)
    {
        PackageModel model = ReadPackageJson(packageRoot);
        if (model.Name.Length == 0)
        {
            model.Name = fallbackName;
        }
        if (model.Version.Length == 0)
        {
            model.Version = fallbackVersion;
        }

        List<AsmDefModel> asmdefs = ReadAsmDefs(packageRoot);
        CollectTypes(packageRoot, asmdefs);
        AddPrecompiledAssemblies(packageRoot, asmdefs);
        model.AsmDefs = asmdefs;
        return model;
    }
}

/// <summary>生成期的包模型（对应主工程的 <see cref="UnityPackage"/>）。</summary>
internal sealed class PackageModel
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Unity { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public Dictionary<string, string> Dependencies { get; } = new(StringComparer.Ordinal);
    public List<AsmDefModel> AsmDefs { get; set; } = new();

    /// <summary>扁平化的全部类型 GUID（与主模型的 <c>guids</c> 对应）。</summary>
    public IEnumerable<string> AllGuids =>
        AsmDefs.SelectMany(static a => a.Types).Select(static t => t.Guid).Distinct(StringComparer.Ordinal);
}

/// <summary>生成期的 asmdef 模型。</summary>
internal sealed class AsmDefModel(string name, string guid, string directory)
{
    public string Name { get; } = name;
    public string Guid { get; } = guid;
    public string Directory { get; } = directory;
    public List<TypeModel> Types { get; } = new();
}

/// <summary>生成期的可序列化类型模型。</summary>
internal sealed record TypeModel(string Namespace, string Name, string Guid);

/// <summary>遍 1 的中间结果：一个类型声明 + 它所在文件/程序集/基类列表。</summary>
internal sealed class TypeCandidate(
    string @namespace,
    string name,
    string guid,
    AsmDefModel asmdef,
    List<string> baseTypes,
    bool hasSerializableAttribute)
{
    public string Namespace { get; } = @namespace;
    public string Name { get; } = name;
    public string Guid { get; } = guid;
    public AsmDefModel AsmDef { get; } = asmdef;
    public List<string> BaseTypes { get; } = baseTypes;
    public bool HasSerializableAttribute { get; } = hasSerializableAttribute;

    /// <summary>索引用的完整名（命名空间 + 类型名，嵌套用 <c>+</c>）。</summary>
    public string FullName => $"{Namespace}.{Name}";
}

/// <summary>
/// 全包类型索引，供遍 2 沿基类链回溯。
///
/// <para>源文件里写基类时通常不带命名空间（靠 <c>using</c>），所以查找要宽容：
/// 先按「同命名空间 + 嵌套写法」，再按全限定名，最后按「任意命名空间下的同名类型」。</para>
/// </summary>
internal sealed class TypeIndex
{
    private readonly Dictionary<string, TypeCandidate> m_byFullName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<TypeCandidate>> m_byName = new(StringComparer.Ordinal);

    public void Add(TypeCandidate candidate)
    {
        m_byFullName.TryAdd(candidate.FullName, candidate);
        if (!m_byName.TryGetValue(candidate.Name, out List<TypeCandidate>? list))
        {
            m_byName[candidate.Name] = list = new List<TypeCandidate>(1);
        }
        if (!list.Contains(candidate))
        {
            list.Add(candidate);
        }
    }

    /// <summary>按源码里的写法找类型。<paramref name="namespace"/> 是「引用方」的命名空间。</summary>
    public TypeCandidate? Find(string reference, string @namespace)
    {
        // 去掉泛型参数后可能是 `Base`、`Ns.Base`、`Outer.Inner`。
        int generic = reference.IndexOf('<');
        string text = (generic >= 0 ? reference[..generic] : reference).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        // 1) 同命名空间 + 嵌套写法
        string nested = text.Replace('.', '+');
        if (m_byFullName.TryGetValue($"{@namespace}.{nested}", out TypeCandidate? exact))
        {
            return exact;
        }
        // 2) 全限定名
        if (text.Contains('.') && m_byFullName.TryGetValue(nested, out TypeCandidate? qualified))
        {
            return qualified;
        }
        // 3) 只写了简单名：在任意命名空间下找同名类型。
        //    同一程序集不能有两个同名程序集级类型，所以「取第一个」在实践中是安全的，
        //    比漏判（→ Missing MonoScript）好。
        string simple = SimpleName(text);
        return m_byName.TryGetValue(simple, out List<TypeCandidate>? list) && list.Count > 0 ? list[0] : null;
    }

    private static string SimpleName(string typeName)
    {
        int dot = typeName.LastIndexOf('.');
        return dot >= 0 ? typeName[(dot + 1)..] : typeName;
    }
}
