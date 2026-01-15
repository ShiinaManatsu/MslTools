using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using ShaderTools.CodeAnalysis;
using ShaderTools.CodeAnalysis.Hlsl.Syntax;
using ShaderTools.CodeAnalysis.Structure;

namespace ShaderTools.LanguageServer.Handlers
{
    internal sealed class FoldingRangeHandler : IFoldingRangeHandler
    {
        private readonly LanguageServerWorkspace _workspace;
        private readonly FoldingRangeRegistrationOptions _registrationOptions;
        private readonly FoldingRangeRegistrationOptions _capability;

        public FoldingRangeHandler(LanguageServerWorkspace workspace, TextDocumentSelector documentSelector)
        {
            _workspace = workspace;
            _registrationOptions = new FoldingRangeRegistrationOptions
            {
                DocumentSelector = documentSelector
            };
            _capability = new FoldingRangeRegistrationOptions()
            {
                DocumentSelector = documentSelector,
            };
        }


        public async Task<Container<FoldingRange>> Handle(FoldingRangeRequestParam request,
            CancellationToken cancellationToken)
        {
            var document = _workspace.GetDocument(request.TextDocument.Uri);

            var blockStructureProvider = document.LanguageServices.GetService<IBlockStructureProvider>();
            if (blockStructureProvider == null)
                return [];

            var folds = await GetDirectiveFolds(document, cancellationToken).ConfigureAwait(false);
            var blockSpans = await blockStructureProvider.ProvideBlockStructureAsync(document, cancellationToken).ConfigureAwait(false);

            var r = blockSpans
                .Select(x => (Range: Helpers.ToRange(document.SourceText, x.TextSpan), x.BannerText))
                .Select(x => new FoldingRange()
                {
                    StartLine = x.Range.Start.Line,
                    EndLine = x.Range.End.Line,
                    Kind = FoldingRangeKind.Region,
                    CollapsedText = x.BannerText,
                })
                .ToList();

            folds.AddRange(r);
            return folds.DistinctBy(x => x.StartLine).ToList();
        }


        public async Task<List<FoldingRange>> GetDirectiveFolds(Document document, CancellationToken cancellationToken)
        {
            var folds = new List<FoldingRange>();

            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false) as SyntaxTree;

            if (syntaxTree == null)
                return folds;

            var directives = syntaxTree.Root
                .ChildNodes
                .Cast<SyntaxNode>()
                .SelectMany(x => x.GetDirectives())
                .Select(x => (Node: x, Span: x.GetTextSpanRoot()))
                .Where(x => x.Span != null)
                .Select(x => (x.Node.Kind, Range: Helpers.ToRange(document.SourceText, x.Span.Value.Span)))
                .Where(x => x.Kind switch
                {
                    SyntaxKind.IfDefDirectiveTrivia or
                        SyntaxKind.IfDirectiveTrivia or
                        SyntaxKind.IfNDefDirectiveTrivia or
                        SyntaxKind.ElifDirectiveTrivia or
                        SyntaxKind.ElseDirectiveTrivia or
                        SyntaxKind.EndIfDirectiveTrivia
                        => true,
                    _ => false
                })
                .ToList();

            // Stack to track opening directives
            var stack = new Stack<(SyntaxKind Kind, Range Range, int StartLine)>();

            foreach (var (kind, range) in directives)
            {
                switch (kind)
                {
                    case SyntaxKind.IfDefDirectiveTrivia:
                    case SyntaxKind.IfDirectiveTrivia:
                    case SyntaxKind.IfNDefDirectiveTrivia:
                        stack.Push((kind, range, range.Start.Line));
                        break;

                    case SyntaxKind.ElifDirectiveTrivia:
                    case SyntaxKind.ElseDirectiveTrivia:
                        if (stack.Count > 0)
                        {
                            var prev = stack.Pop();
                            if (range.Start.Line > prev.StartLine)
                            {
                                folds.Add(new FoldingRange
                                {
                                    StartLine = prev.StartLine,
                                    EndLine = range.Start.Line - 1,
                                    Kind = FoldingRangeKind.Region
                                });
                            }

                            stack.Push((kind, range, range.Start.Line));
                        }

                        break;

                    case SyntaxKind.EndIfDirectiveTrivia:
                        if (stack.Count > 0)
                        {
                            var prev = stack.Pop();
                            if (range.Start.Line > prev.StartLine)
                            {
                                folds.Add(new FoldingRange
                                {
                                    StartLine = prev.StartLine,
                                    EndLine = range.Start.Line - 1,
                                    Kind = FoldingRangeKind.Region
                                });
                            }
                        }

                        break;
                }
            }

            return folds;
        }

        public FoldingRangeRegistrationOptions GetRegistrationOptions(FoldingRangeCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return _capability;
        }
    }
}