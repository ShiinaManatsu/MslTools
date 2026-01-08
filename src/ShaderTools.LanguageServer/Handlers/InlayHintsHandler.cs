using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using ShaderTools.CodeAnalysis;
using ShaderTools.CodeAnalysis.Hlsl.Compilation;
using ShaderTools.CodeAnalysis.Hlsl.Syntax;
using ShaderTools.CodeAnalysis.Shared.Extensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace ShaderTools.LanguageServer.Handlers;

#pragma warning disable 618

internal class InlayHintsHandler(
    LanguageServerWorkspace workspace,
    ILanguageServer server,
    TextDocumentSelector documentSelector)
    : IInlayHintsHandler
{
    private InlayHintRegistrationOptions _options = new()
    {
        DocumentSelector = documentSelector,
        ResolveProvider = false,
        Id = "MSL Inlay Hints Handler"
    };

    private bool IfKeepNodes(SyntaxNode node)
    {
        return node switch
        {
            // IdentifierNameSyntax or
            FunctionInvocationExpressionSyntax => true,
            _ => false
        };
    }

    private List<(Range Range, SyntaxNode Node)>
        GetHintNodesRecursively(SyntaxNode syntaxNode, Document document,
            Range range)
    {
        var nodes = new List<(Range Range, SyntaxNode Node)>();

        if (syntaxNode.ChildNodes.Count > 0)
        {
            nodes.AddRange(syntaxNode.ChildNodes
                .SelectMany(x => GetHintNodesRecursively(x as SyntaxNode, document, range)));
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

    public async Task<InlayHintContainer> Handle(InlayHintParams request, CancellationToken cancellationToken)
    {
        var document = workspace.GetDocument(request.TextDocument.Uri);


        if (await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false) is not SemanticModel sm)
            return [];


        var withType = false;
        var configuration = await server.Configuration.GetConfiguration(new ConfigurationItem { Section = "hlsl-client" }).ConfigureAwait(false);

        try
        {
            if (configuration.AsEnumerable().ToDictionary()["hlsl-client:language:inlayHints:withType"] == "True")
            {
                withType = true;
            }
        }
        catch
        {
            // ignored
        }


        var nodes = GetHintNodesRecursively(sm.BindingRoot, document, request.Range);

        var hints = nodes
            .DistinctBy(x => x.Node)
            .SelectMany(x =>
                sm.GetBoundNode(x.Node, withType)
                    .Select(b => (Label: b.Label, IsParameter: b.IsParameter,
                        SourceFileSpan: sm.SyntaxTree.GetSourceFileSpan(b.SourceRange), Syntax: x.Node)))
            // .Where(x => x.SourceFileSpan.IsInRootFile)
            .Select(x =>
            {
                var r = Helpers.ToRange(document.SourceText, x.SourceFileSpan.Span);
                return new InlayHint
                {
                    Label = $"{x.Label}",
                    Position = new Position(r.Start.Line, r.Start.Character),
                    Kind = x.IsParameter ? InlayHintKind.Parameter : InlayHintKind.Type,
                    PaddingLeft = false,
                    PaddingRight = true,
                };
            })
            .DistinctBy(x => x.Position)
            .ToList();

        return hints;
    }


    // public List<(string label, bool isParameter)> GetBoundNode(SyntaxNode syntaxNode)
    // {
    //     var bn = _bindingResult.GetBoundNode(syntaxNode);
    //     var a = new List<(string label, bool isParameter)> { ("1", false), ("2", true) };
    //     return bn switch
    //     {
    //         BoundVariableExpression x => [(x.Type.Name, true)],
    //         BoundFunctionInvocationExpression x =>
    //             [(x.Type.Name, false), ..x.Symbol.Parameters.Select(p => ($"{p.Name}:{p.ValueType.Name}", true))],
    //         BoundFieldExpression x => [(x.Type.Name, true)],
    //         _ => []
    //     };
    // }

    public InlayHintRegistrationOptions GetRegistrationOptions(InlayHintClientCapabilities capability,
        ClientCapabilities clientCapabilities)
    {
        return _options;
    }
}

// internal class InlayHintResolveHandler() : IInlayHintResolveHandler
// {
//     public async Task<InlayHint> Handle(InlayHint request, CancellationToken cancellationToken)
//     {
//         throw new NotImplementedException();
//     }
//
//     public void SetCapability(InlayHintClientCapabilities capability, ClientCapabilities clientCapabilities)
//     {
//         throw new NotImplementedException();
//     }
//
//     public Guid Id { get; } = Guid.NewGuid();
// }


#pragma warning restore 618