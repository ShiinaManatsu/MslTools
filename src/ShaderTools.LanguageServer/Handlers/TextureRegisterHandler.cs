using MediatR;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Text;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Serialization;
using ShaderTools.CodeAnalysis;
using ShaderTools.CodeAnalysis.Classification;
using ShaderTools.CodeAnalysis.Hlsl.Compilation;
using ShaderTools.CodeAnalysis.Hlsl.Syntax;
using ShaderTools.CodeAnalysis.ReferenceHighlighting;
using ShaderTools.CodeAnalysis.Shared.Extensions;
using ShaderTools.CodeAnalysis.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;


#pragma warning disable 618
namespace ShaderTools.LanguageServer.Handlers
{

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
        public List<TextureRegisterItem> Textures { get; set; }
    }

    internal class TextureRegisterHandler(LanguageServerWorkspace workspace,
        TextDocumentSelector documentSelector) : IJsonRpcRequestHandler<TextureRegisterRequest, TextureRegisterResponse>
    {
        public async Task<TextureRegisterResponse> Handle(TextureRegisterRequest request, CancellationToken cancellationToken)
        {
            var document = workspace.GetDocument(request.Uri);
            var ast = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            var sm = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false) as SemanticModel;
            var result = new TextureRegisterResponse() { Textures = new List<TextureRegisterItem>() };
            foreach (var declaration in sm.GetTextures())
            {
                var name = declaration.Identifier.Text;
                int slot = 0;
                foreach (var qualifier in declaration.Qualifiers)
                {
                    if (qualifier is RegisterLocation register)
                    {
                        int.TryParse(register.Register.Text.Substring(1), out slot);
                    }
                }
                result.Textures.Add(new TextureRegisterItem()
                {
                    Name = name,
                    RegisterSlot = slot
                });
            }

            return result;
        }
    }
}
#pragma warning restore 618