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

public class BufferLayoutRequest : IRequest<BufferLayoutResponse>
{
    public DocumentUri Uri { get; set; }
}

public class BufferLayoutItem
{
    public string Type { get; set; }
    public string Name { get; set; }
}

public class BufferLayoutResponse
{
    public List<BufferLayoutItem> Variables { get; set; } = [];
}

internal class BufferLayoutHandler(
    LanguageServerWorkspace workspace,
    TextDocumentSelector documentSelector)
    : IJsonRpcRequestHandler<BufferLayoutRequest, BufferLayoutResponse>
{
    public async Task<BufferLayoutResponse> Handle(BufferLayoutRequest request,
        CancellationToken cancellationToken)
    {
        var document = workspace.GetDocument(request.Uri);

        if (await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false) is not SemanticModel sm)
            return new BufferLayoutResponse();

        var variables =
            await Task.Run(
                    () => sm.GetConstantBufferByName("Shader", filterInvisible: false)
                        .Select(x => new BufferLayoutItem() { Type = x.Item1, Name = x.Item2 }).ToList(),
                    cancellationToken)
                .ConfigureAwait(false);

        return new BufferLayoutResponse
            { Variables = variables };
    }
}