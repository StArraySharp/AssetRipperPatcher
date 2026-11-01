using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace AssetRipperPatcher.Tools;

/// <summary>
/// 快速修复（Alt+Enter）：
/// - HRP001 字符串重命名为编辑距离最近的有效成员名（≤3 或仅大小写差异）
/// - HRP002 OverrideIndex 的值改 0
/// - HRP004 字符串字面量 → nameof(Owner.Member)（只对 public 成员提供）
/// - HRP003 不提供自动修复（改哪个签名是语义决策），消息里给出目标签名对照
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PatchCodeFixProvider))]
[Shared]
public sealed class PatchCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create("HRP001", "HRP002", "HRP004");

    public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            SyntaxNode node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);

            switch (diagnostic.Id)
            {
                case "HRP001":
                    await AddRenameFixAsync(context, diagnostic, node).ConfigureAwait(false);
                    break;

                case "HRP002":
                    {
                        const string title = "OverrideIndex 改为 0";
                        context.RegisterCodeFix(
                            CodeAction.Create(title,
                                token => ReplaceWithNodeAsync(context.Document, node,
                                    SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0))),
                                equivalenceKey: title),
                            diagnostic);
                        break;
                    }

                case "HRP004":
                    await AddNameofFixAsync(context, diagnostic, node).ConfigureAwait(false);
                    break;
            }
        }
    }

    private static async Task AddRenameFixAsync(CodeFixContext context, Diagnostic diagnostic, SyntaxNode node)
    {
        if (node is not LiteralExpressionSyntax literal || literal.Token.Value is not string currentName)
        {
            return;
        }

        SemanticModel semanticModel = await GetShadowModelAsync(context.Document, context.CancellationToken).ConfigureAwait(false);
        AttributeSyntax? attribute = node.AncestorsAndSelf().OfType<AttributeSyntax>().FirstOrDefault();
        if (attribute is null || GetTargetType(semanticModel, attribute) is not { } ownerType)
        {
            return;
        }

        // 候选 = owner 链上全部成员名（方法 + 构造函数）
        var names = new System.Collections.Generic.SortedSet<string>(StringComparer.Ordinal);
        for (ITypeSymbol? type = ownerType; type is not null; type = type.BaseType)
        {
            foreach (ISymbol member in type.GetMembers())
            {
                if (member is IMethodSymbol method
                    && method.MethodKind is MethodKind.Ordinary or MethodKind.Constructor)
                {
                    names.Add(method.Name);
                }
            }
        }

        string? best = names
            .Select(n => new { Name = n, Distance = Levenshtein(currentName, n) })
            .Where(x => x.Distance <= 3 || string.Equals(x.Name, currentName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .Select(x => x.Name)
            .FirstOrDefault();
        if (best is null || best == currentName)
        {
            return;
        }

        string title = $"改为 \"{best}\"";
        context.RegisterCodeFix(
            CodeAction.Create(title,
                token => ReplaceWithNodeAsync(context.Document, literal,
                    SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(best))),
                equivalenceKey: title),
            diagnostic);
    }

    private static async Task AddNameofFixAsync(CodeFixContext context, Diagnostic diagnostic, SyntaxNode node)
    {
        if (node is not LiteralExpressionSyntax literal || literal.Token.Value is not string memberName)
        {
            return;
        }

        SemanticModel semanticModel = await GetShadowModelAsync(context.Document, context.CancellationToken).ConfigureAwait(false);
        AttributeSyntax? attribute = node.AncestorsAndSelf().OfType<AttributeSyntax>().FirstOrDefault();
        if (attribute is null || GetTargetType(semanticModel, attribute) is not { } ownerType)
        {
            return;
        }

        string title = $"改为 nameof({ownerType.Name}.{memberName})";
        context.RegisterCodeFix(
            CodeAction.Create(title,
                token => ReplaceWithNodeAsync(context.Document, literal,
                    SyntaxFactory.ParseExpression($"nameof({ownerType.Name}.{memberName})").WithTriviaFrom(literal)),
                equivalenceKey: title),
            diagnostic);
    }

    /// <summary>
    /// 取注解的第一个位置实参 <c>typeof(...)</c> 解析出的目标类型。
    /// 注意：<c>GetTypeInfo(attributeSyntax)</c> 给的是注解类型本身（PatchMethodAttribute），
    /// 不是目标类型——必须走 typeof 表达式。
    /// </summary>
    private static ITypeSymbol? GetTargetType(SemanticModel semanticModel, AttributeSyntax attribute)
    {
        AttributeArgumentSyntax? target = (attribute.ArgumentList?.Arguments ?? default)
            .FirstOrDefault(a => a.NameEquals is null);
        if (target?.Expression is not TypeOfExpressionSyntax typeOfExpression)
        {
            return null;
        }
        ITypeSymbol? type = semanticModel.GetTypeInfo(typeOfExpression.Type).Type;
        return type is null || type is IErrorTypeSymbol ? null : type;
    }

    /// <summary>快速修复也必须看全量元数据（目标多是 private），与分析器同一个影子视图。</summary>
    private static async Task<SemanticModel> GetShadowModelAsync(Document document, System.Threading.CancellationToken token)
    {
        SyntaxTree tree = (await document.GetSyntaxTreeAsync(token).ConfigureAwait(false))!;
        Compilation compilation = (await document.Project.GetCompilationAsync(token).ConfigureAwait(false))!;
        return ShadowView.Of(compilation).GetSemanticModel(tree);
    }

    private static async Task<Solution> ReplaceWithNodeAsync(Document document, SyntaxNode oldNode, SyntaxNode newNode)
    {
        DocumentEditor editor = await DocumentEditor.CreateAsync(document).ConfigureAwait(false);
        editor.ReplaceNode(oldNode, newNode);
        return editor.GetChangedDocument().Project.Solution;
    }

    /// <summary>Levenshtein 编辑距离。</summary>
    private static int Levenshtein(string a, string b)
    {
        int[,] dp = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++)
        {
            dp[i, 0] = i;
        }
        for (int j = 0; j <= b.Length; j++)
        {
            dp[0, j] = j;
        }
        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = System.Math.Min(System.Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + cost);
            }
        }
        return dp[a.Length, b.Length];
    }
}
