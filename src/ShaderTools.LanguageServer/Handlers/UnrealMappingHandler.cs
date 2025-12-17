using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using ShaderTools.CodeAnalysis.Hlsl.Compilation;

namespace ShaderTools.LanguageServer.Handlers;

public class UnrealMappingRequest : IRequest<UnrealMappingResponse>
{
    public DocumentUri Uri { get; set; }
}

public class UnrealMappingItem
{
    public string Type { get; set; }
    public string Name { get; set; }
}

public class UnrealMappingResponse
{
    public List<UnrealMappingItem> Variables { get; set; }
    public List<UnrealMappingItem> Textures { get; set; }
    public List<UnrealMappingItem> Toggles { get; set; }
}

internal class UnrealMappingHandler(
    LanguageServerWorkspace workspace,
    TextDocumentSelector documentSelector)
    : IJsonRpcRequestHandler<UnrealMappingRequest, UnrealMappingResponse>
{
    public async Task<UnrealMappingResponse> Handle(UnrealMappingRequest request,
        CancellationToken cancellationToken)
    {
        var document = workspace.GetDocument(request.Uri);

        if (await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false) is not SemanticModel sm)
            return new UnrealMappingResponse();

        var variables =
            await Task.Run(
                    () => sm.GetConstantBufferByName("Shader")
                        .Select(x => new UnrealMappingItem() { Type = x.Item1, Name = x.Item2 }).ToList(),
                    cancellationToken)
                .ConfigureAwait(false);

        var textures =
            await Task.Run(
                () => sm.GetLocalUserTextures().Select(x => new UnrealMappingItem() { Type = x.Item1, Name = x.Item2 })
                    .ToList(),
                cancellationToken).ConfigureAwait(false);

        var toggles =
            await Task.Run(
                () => sm.GetLocalToggles().Select(x => new UnrealMappingItem() { Type = x.Item1, Name = x.Item2 })
                    .ToList(),
                cancellationToken).ConfigureAwait(false);

        return new UnrealMappingResponse
            { Variables = variables, Textures = textures, Toggles = toggles };
    }
}