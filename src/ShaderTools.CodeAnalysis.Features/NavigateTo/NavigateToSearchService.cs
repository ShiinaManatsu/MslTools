using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Host.Mef;
using ShaderTools.CodeAnalysis.Navigation;
using ShaderTools.CodeAnalysis.Symbols;
using ShaderTools.CodeAnalysis.Syntax;
using ShaderTools.Utilities.Diagnostics;
using ShaderTools.Utilities.PooledObjects;

namespace ShaderTools.CodeAnalysis.NavigateTo
{
    [ExportWorkspaceService(typeof(INavigateToSearchService))]
    internal sealed partial class NavigateToSearchService : INavigateToSearchService
    {
        public async Task<ImmutableArray<INavigateToSearchResult>> SearchDocumentAsync(Document document, string searchPattern, CancellationToken cancellationToken)
        {
            var result = ArrayBuilder<INavigateToSearchResult>.GetInstance();

            cancellationToken.ThrowIfCancellationRequested();

            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

            if (semanticModel == null)
            {
                return ImmutableArray<INavigateToSearchResult>.Empty;
            }

            foreach (var node in syntaxTree.Root.DescendantNodesAndSelf())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var declaredSymbol = semanticModel.GetDeclaredSymbol(node);

                if (declaredSymbol != null 
                    && declaredSymbol.Kind != SymbolKind.Parameter
                    && (declaredSymbol.Kind != SymbolKind.Variable || declaredSymbol.Parent == null || declaredSymbol.Parent.Kind != SymbolKind.Function)
                    && declaredSymbol.Name != null
                    && Contains(declaredSymbol.Name, searchPattern))
                {
                    var matchKind = declaredSymbol.Name.StartsWith(searchPattern, StringComparison.CurrentCultureIgnoreCase)
                        ? NavigateToMatchKind.Prefix
                        : (declaredSymbol.Name == searchPattern)
                            ? NavigateToMatchKind.Exact
                            : NavigateToMatchKind.Substring;

                    result.AddRange(ConvertResult(declaredSymbol, document, syntaxTree, matchKind));
                }
            }

            return result.ToImmutableAndFree();
        }

        private static bool Contains(string source, string toCheck)
        {
            return source.IndexOf(toCheck, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private static IEnumerable<INavigateToSearchResult> ConvertResult(
            ISymbol declaredSymbol, Document document, SyntaxTreeBase syntaxTree,
            NavigateToMatchKind matchKind)
        {
            var isCaseSensitive = false;
            var kind = GetItemKind(declaredSymbol.Kind);

            foreach (var location in declaredSymbol.Locations)
            {
                var sourceFileSpan = syntaxTree.GetSourceFileSpan(location);

                if (sourceFileSpan.IsInRootFile)
                {
                    var navigableItem = NavigableItemFactory.GetItemFromDeclaredSymbol(
                        declaredSymbol, document, sourceFileSpan);

                    yield return new SearchResult(
                        document, declaredSymbol, kind, matchKind,
                        isCaseSensitive, navigableItem);
                }
            }
        }

        private static string GetItemKind(SymbolKind symbolKind)
        {
            return symbolKind switch
            {
                SymbolKind.Class or
                    SymbolKind.IntrinsicObjectType or
                    SymbolKind.IntrinsicVectorType or
                    SymbolKind.IntrinsicMatrixType or
                    SymbolKind.IntrinsicScalarType => NavigateToItemKind.Class,
                SymbolKind.Variable or
                    SymbolKind.Field or
                    SymbolKind.Parameter or
                    SymbolKind.TypeAlias or
                    SymbolKind.Attribute => NavigateToItemKind.Field,
                SymbolKind.Interface => NavigateToItemKind.Interface,
                SymbolKind.Function => NavigateToItemKind.Method,
                SymbolKind.Namespace => NavigateToItemKind.Module,
                SymbolKind.ConstantBuffer or
                    SymbolKind.Toggle or
                    SymbolKind.Struct or
                    SymbolKind.Technique or
                    SymbolKind.MessiahTechnique =>
                    NavigateToItemKind.Structure,
                SymbolKind.Array => NavigateToItemKind.OtherSymbol,
                _ => Contract.FailWithReturn<string>("Unknown declaration kind " + symbolKind)
            };
        }
    }
}
