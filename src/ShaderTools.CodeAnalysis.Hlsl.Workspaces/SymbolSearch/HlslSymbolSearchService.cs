using Microsoft.CodeAnalysis.Host.Mef;
using ShaderTools.CodeAnalysis.Compilation;
using ShaderTools.CodeAnalysis.Hlsl.Compilation;
using ShaderTools.CodeAnalysis.Hlsl.Symbols;
using ShaderTools.CodeAnalysis.Hlsl.Syntax;
using ShaderTools.CodeAnalysis.Symbols;
using ShaderTools.CodeAnalysis.SymbolSearch;
using ShaderTools.CodeAnalysis.Syntax;
using ShaderTools.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.AccessCache;

namespace ShaderTools.CodeAnalysis.Hlsl.SymbolSearch
{
    [ExportLanguageService(typeof(ISymbolSearchService), LanguageNames.Hlsl)]
    internal sealed class HlslSymbolSearchService : ISymbolSearchService
    {
        public SymbolSpan? FindSymbol(SemanticModelBase semanticModel, SourceLocation position)
        {
            if (semanticModel == null)
                throw new ArgumentNullException(nameof(semanticModel));

            var syntaxTreeRoot = (SyntaxNode)semanticModel.SyntaxTree.Root;
            return syntaxTreeRoot.FindNodes(position)
                .SelectMany(n => GetSymbolSpans((SemanticModel)semanticModel, n))
                .Where(s => s.Span.File.IsRootFile && s.SourceRange.ContainsOrTouches(position))
                .Select(s => s).Cast<SymbolSpan?>().FirstOrDefault();
        }

        public ImmutableArray<SymbolSpan> FindUsages(SemanticModelBase semanticModel, ISymbol symbol)
        {
            if (semanticModel == null)
                throw new ArgumentNullException(nameof(semanticModel));

            if (symbol == null)
                throw new ArgumentNullException(nameof(symbol));

            var syntaxTreeRoot = (SyntaxNode)semanticModel.SyntaxTree.Root;

            return
            [
                ..syntaxTreeRoot.DescendantNodes()
                    .SelectMany(n => GetSymbolSpans((SemanticModel)semanticModel, (SyntaxNode)n),
                        (n, s) => new { n, s })
                    .Where(@t => @t.s.Symbol.Equals(symbol))
                    .Select(@t => @t.s)
            ];
        }

        public async Task<ImmutableArray<SymbolSpan>> FindUsagesAsync(SemanticModelBase semanticModel, ISymbol symbol,
            CancellationToken cancellationToken,
            bool includeIdentifierToken = false)
        {
            ArgumentNullException.ThrowIfNull(semanticModel);
            ArgumentNullException.ThrowIfNull(symbol);
            cancellationToken.ThrowIfCancellationRequested();

            var syntaxTreeRoot = (SyntaxNode)semanticModel.SyntaxTree.Root;
            var nodes = syntaxTreeRoot.DescendantNodes()
                .OfType<SyntaxNode>()
                .Where(x => x.Kind is
                    SyntaxKind.VariableDeclarator or
                    SyntaxKind.ClassType or
                    SyntaxKind.StructType or
                    SyntaxKind.InterfaceType or
                    SyntaxKind.IdentifierName or
                    SyntaxKind.IdentifierDeclarationName or
                    SyntaxKind.FieldAccessExpression or
                    SyntaxKind.MethodInvocationExpression or
                    SyntaxKind.FunctionInvocationExpression or
                    SyntaxKind.FunctionDefinition or
                    SyntaxKind.FunctionDeclaration or
                    SyntaxKind.IdentifierToken)
                .ToList();

            var refs = await nodes
                .ToAsyncEnumerable()
                .SelectMany(n => GetSymbolSpans((SemanticModel)semanticModel, n))
                .Where(x => x.Kind == SymbolSpanKind.Reference)
                .Where(x => x.Symbol.Equals(symbol))
                .Take(1)
                .ToListAsync(cancellationToken);

            var tokens = await nodes
                .ToAsyncEnumerable()
                .OfType<SyntaxToken>()
                .Where(x => x.MacroReference != null)
                .Where(x => x.Kind == SyntaxKind.IdentifierToken)
                .Select(x => (Node: x, Name: x.ToString()))
                .ToListAsync(cancellationToken);

            var block = (symbol.DeclaringSyntaxNodes.First() as SyntaxNode).GetAncestor<BlockSyntax>();

            var mRefs = await tokens
                .ToAsyncEnumerable()
                .Where(x => x.Name == symbol.Name)
                .Where(x => x.Node.SourceRange.Start > symbol.Locations.First().Start)
                .Where(x => block == null || block == x.Node.GetAncestor<BlockSyntax>())
                .Select(x => SymbolSpan.CreateReference(
                    symbol, x.Node.SourceRange, x.Node.FileSpan
                ))
                .ToListAsync(cancellationToken);


            return [..refs, ..mRefs];
        }

        private static IEnumerable<SymbolSpan> GetSymbolSpans(SemanticModel semanticModel, SyntaxNode node)
        {
            switch (node.Kind)
            {
                case SyntaxKind.VariableDeclarator:
                {
                    var expression = (VariableDeclaratorSyntax)node;
                    var symbol = semanticModel.GetDeclaredSymbol(expression);
                    if (symbol != null)
                        yield return SymbolSpan.CreateDefinition(symbol, expression.Identifier.SourceRange,
                            expression.Identifier.FileSpan);
                    break;
                }
                case SyntaxKind.ClassType:
                case SyntaxKind.StructType:
                {
                    var expression = (StructTypeSyntax)node;
                    var symbol = semanticModel.GetDeclaredSymbol(expression);
                    if (symbol != null && expression.Name != null)
                        yield return SymbolSpan.CreateDefinition(symbol, expression.Name.SourceRange,
                            expression.Name.FileSpan);
                    break;
                }
                case SyntaxKind.InterfaceType:
                {
                    var expression = (InterfaceTypeSyntax)node;
                    var symbol = semanticModel.GetDeclaredSymbol(expression);
                    if (symbol != null)
                        yield return SymbolSpan.CreateDefinition(symbol, expression.Name.SourceRange,
                            expression.Name.FileSpan);
                    break;
                }
                case SyntaxKind.IdentifierName:
                {
                    var expression = (IdentifierNameSyntax)node;
                    var symbol = semanticModel.GetSymbol(expression);
                    if (symbol != null)
                        yield return SymbolSpan.CreateReference(symbol, expression.Name.SourceRange,
                            expression.Name.FileSpan);
                    break;
                }
                case SyntaxKind.IdentifierDeclarationName:
                {
                    var expression = (IdentifierDeclarationNameSyntax)node;
                    var symbol = semanticModel.GetSymbol(expression);
                    if (symbol != null)
                        yield return SymbolSpan.CreateDefinition(symbol, expression.Name.SourceRange,
                            expression.Name.FileSpan);
                    break;
                }
                case SyntaxKind.FieldAccessExpression:
                {
                    var expression = (FieldAccessExpressionSyntax)node;
                    var symbol = semanticModel.GetSymbol(expression);
                    if (symbol != null)
                        yield return SymbolSpan.CreateReference(symbol, expression.Name.SourceRange,
                            expression.Name.FileSpan);
                    break;
                }
                case SyntaxKind.MethodInvocationExpression:
                {
                    var expression = (MethodInvocationExpressionSyntax)node;
                    var symbol = semanticModel.GetSymbol(expression);
                    if (symbol != null)
                        yield return SymbolSpan.CreateReference(symbol, expression.Name.SourceRange,
                            expression.Name.FileSpan);
                    break;
                }
                case SyntaxKind.FunctionInvocationExpression:
                {
                    var expression = (FunctionInvocationExpressionSyntax)node;
                    var symbol = semanticModel.GetSymbol(expression);
                    if (symbol != null)
                        yield return SymbolSpan.CreateReference(symbol,
                            expression.Name.GetUnqualifiedName().Name.SourceRange,
                            expression.Name.GetUnqualifiedName().Name.FileSpan);
                    break;
                }
                case SyntaxKind.FunctionDefinition:
                {
                    var expression = (FunctionDefinitionSyntax)node;
                    var symbol = semanticModel.GetDeclaredSymbol(expression);
                    if (symbol != null)
                        yield return SymbolSpan.CreateDefinition(symbol,
                            expression.Name.GetUnqualifiedName().Name.SourceRange,
                            expression.Name.GetUnqualifiedName().Name.FileSpan);
                    break;
                }
                case SyntaxKind.FunctionDeclaration:
                {
                    var expression = (FunctionDeclarationSyntax)node;
                    var symbol = semanticModel.GetDeclaredSymbol(expression);
                    if (symbol != null)
                        yield return SymbolSpan.CreateDefinition(symbol,
                            expression.Name.GetUnqualifiedName().Name.SourceRange,
                            expression.Name.GetUnqualifiedName().Name.FileSpan);
                    break;
                }
            }
        }
    }
}