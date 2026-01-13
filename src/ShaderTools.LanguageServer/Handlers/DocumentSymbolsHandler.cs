using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using ShaderTools.CodeAnalysis.NavigateTo;
using ShaderTools.Utilities.Collections;

namespace ShaderTools.LanguageServer.Handlers
{
    internal sealed class DocumentSymbolsHandler(
        LanguageServerWorkspace workspace,
        TextDocumentSelector documentSelector)
        : IDocumentSymbolHandler
    {
        private readonly DocumentSymbolRegistrationOptions _registrationOptions = new()
        {
            DocumentSelector = documentSelector,
        };

        public DocumentSymbolRegistrationOptions GetRegistrationOptions(DocumentSymbolCapability capability, ClientCapabilities clientCapabilities)
        {
            return _registrationOptions;
        }

        public async Task<SymbolInformationOrDocumentSymbolContainer> Handle(DocumentSymbolParams request, CancellationToken token)
        {
            var document = workspace.GetDocument(request.TextDocument.Uri);

            var searchService = workspace.Services.GetService<INavigateToSearchService>();

            var symbols = ImmutableArray.CreateBuilder<SymbolInformation>();

            await Helpers.FindSymbolsInDocumentAsync(searchService, document, string.Empty, token, symbols);

            var symbolsResult = ImmutableArray.CreateRange(
                symbols
                .Where(x => !x.Name.IsEmpty())
                .ToImmutableArray(),
                x => new SymbolInformationOrDocumentSymbol(x));

            return new SymbolInformationOrDocumentSymbolContainer(symbolsResult);
        }
    }
}
