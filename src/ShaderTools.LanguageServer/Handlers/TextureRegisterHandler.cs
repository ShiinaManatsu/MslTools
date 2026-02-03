using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using ShaderTools.CodeAnalysis.Hlsl.Compilation;
using ShaderTools.CodeAnalysis.Hlsl.Syntax;

#pragma warning disable 618
namespace ShaderTools.LanguageServer.Handlers;

public class TextureRegisterRequest : IRequest<TextureRegisterResponse>
{
    public DocumentUri Uri { get; set; }
}

public class TextureRegisterItem
{
    public string Name { get; set; }
    public int RegisterSlot { get; set; }
}

public class TextureRegisterResponse
{
    public List<TextureRegisterItem> Textures { get; set; } = [];
}

internal class TextureRegisterHandler(
    LanguageServerWorkspace workspace,
    TextDocumentSelector documentSelector) : IJsonRpcRequestHandler<TextureRegisterRequest, TextureRegisterResponse>
{
    public async Task<TextureRegisterResponse> Handle(TextureRegisterRequest request,
        CancellationToken cancellationToken)
    {
        var document = workspace.GetDocument(request.Uri);
        var result = new TextureRegisterResponse();
        var sm = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false) as SemanticModel;

        foreach (var declaration in sm.GetLocalTextures())
        {
            var name = declaration.Identifier.Text;
            var slot = 0;
            foreach (var qualifier in declaration.Qualifiers)
                if (qualifier is RegisterLocation register)
                    int.TryParse(register.Register.Text[1..], out slot);

            result.Textures.Add(new TextureRegisterItem
            {
                Name = name,
                RegisterSlot = slot
            });
        }

        return result;
    }
}
#pragma warning restore 618