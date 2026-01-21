using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using ShaderTools.CodeAnalysis;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using ShaderTools.CodeAnalysis.Hlsl.Compilation;
using ShaderTools.CodeAnalysis.Hlsl.Syntax;
using ShaderTools.CodeAnalysis.SymbolSearch;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace ShaderTools.LanguageServer.Handlers;

public class UnusedSymbolRequest : IRequest<UnusedSymbolResponse>
{
    public DocumentUri Uri { get; set; }
    public Range Range { get; set; }
}

public class UnusedSymbolItem
{
    public Range Range { get; set; }
}

public class UnusedSymbolResponse
{
    public List<UnusedSymbolItem> Ranges { get; set; } = [];
}

internal class UnusedSymbolHandler(
    LanguageServerWorkspace workspace,
    ILanguageServer server,
    TextDocumentSelector documentSelector)
    : IJsonRpcRequestHandler<UnusedSymbolRequest, UnusedSymbolResponse>
{
    private bool IfKeepNodes(SyntaxNode node)
    {
        return node switch
        {
            VariableDeclaratorSyntax or FunctionDefinitionSyntax => true,
            _ => false
        };
    }

    private List<(Range Range, SyntaxNode Node)>
        GetNodesRecursively(SyntaxNode syntaxNode, Document document,
            Range range)
    {
        var nodes = new List<(Range Range, SyntaxNode Node)>();

        if (syntaxNode.ChildNodes.Count > 0)
        {
            nodes.AddRange(syntaxNode.ChildNodes
                .SelectMany(x => GetNodesRecursively(x as SyntaxNode, document, range)));
        }

        if (!IfKeepNodes(syntaxNode)) return nodes;

        var s = syntaxNode.GetTextSpanRoot();
        if (s == null) return nodes;

        var r = Helpers.ToRange(document.SourceText, s.Value.Span);
        if (range.IntersectsOrTouches(r))
        {
            nodes.Add((r, syntaxNode));
        }

        return nodes;
    }

    public async Task<UnusedSymbolResponse> Handle(UnusedSymbolRequest request,
        CancellationToken cancellationToken)
    {
        var greyOutUnusedDeclarations = await Helpers.GetConfigurationAsync<bool>(
            server,
            "hlsl-client.experimental.greyOutUnusedDeclarations");

        if (!greyOutUnusedDeclarations)
            return new UnusedSymbolResponse();        
        
        var document = workspace.GetDocument(request.Uri);

        if (await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false) is not SemanticModel sm)
            return new UnusedSymbolResponse();
        var symbolSearchService = document.LanguageServices.GetService<ISymbolSearchService>();

        var nodes = GetNodesRecursively(sm.BindingRoot, document, request.Range)
            .Select(x => x.Node)
            .Distinct()
            .Where(x =>
            {
                var symbol = sm.GetDeclaredSymbol(x);
                return symbol != null && symbolSearchService.FindUsages(sm, symbol).Length <= 1;
            })
            .Select(x => x is VariableDeclaratorSyntax ? x.Parent : x)
            .Where(x => x is VariableDeclarationSyntax or FunctionDefinitionSyntax)
            .Select(x => x.GetTextSpanRoot())
            .Where(x => x != null)
            .Where(x => x.Value.File.FilePath == document.FilePath)
            .Select(x => new UnusedSymbolItem()
            {
                Range = Helpers.ToRange(document.SourceText, x.Value.Span)
            })
            .ToList();

        return new UnusedSymbolResponse
        {
            Ranges = nodes
        };
    }
}