using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;

namespace ShaderTools.LanguageServer.Handlers
{
    internal sealed class TextDocumentSyncHandler(
        LanguageServerWorkspace workspace,
        TextDocumentSelector documentSelector)
        : ITextDocumentSyncHandler
    {
        private readonly TextDocumentChangeRegistrationOptions _changeRegistrationOptions = new()
        {
            DocumentSelector = documentSelector,
            SyncKind = TextDocumentSyncKind.Incremental,
        };

        public TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
        {
            var document = workspace.GetDocument(uri);

            return new TextDocumentAttributes(
                uri,
                Helpers.ToLspLanguage(document.Language));
        }

        public Task<Unit> Handle(DidChangeTextDocumentParams notification, CancellationToken cancellationToken)
        {
            if (notification.TextDocument.Uri.Scheme.Contains("chat"))
            {
                return Unit.Task;
            }
            var document = workspace.GetDocument(notification.TextDocument.Uri);

            if (document == null)
            {
                return Unit.Task;
            }

            workspace.UpdateDocument(
                document,
                notification
                .ContentChanges
                .Select(x =>
                    Helpers.ToTextChange(
                        document,
                        x.Range,
                        x.Text)));

            return Unit.Task;
        }

        public Task<Unit> Handle(DidOpenTextDocumentParams notification, CancellationToken cancellationToken)
        {

            if (notification.TextDocument.Uri.Scheme.Contains("chat"))
            {
                return Unit.Task;
            }
            workspace.OpenDocument(
                notification.TextDocument.Uri,
                notification.TextDocument.Text,
                notification.TextDocument.LanguageId);

            return Unit.Task;
        }

        public Task<Unit> Handle(DidCloseTextDocumentParams notification, CancellationToken cancellationToken)
        {

            if (notification.TextDocument.Uri.Scheme.Contains("chat"))
            {
                return Unit.Task;
            }
            var document = workspace.GetDocument(notification.TextDocument.Uri);

            if (document != null)
            {
                workspace.CloseDocument(document.Id);
            }

            return Unit.Task;
        }

        public Task<Unit> Handle(DidSaveTextDocumentParams notification, CancellationToken cancellationToken) => Unit.Task;

        TextDocumentChangeRegistrationOptions
            IRegistration<TextDocumentChangeRegistrationOptions, TextSynchronizationCapability>.GetRegistrationOptions(
                TextSynchronizationCapability capability,
                ClientCapabilities clientCapabilities)
        {
            return new TextDocumentChangeRegistrationOptions()
            {
                DocumentSelector = documentSelector,
                SyncKind = TextDocumentSyncKind.Incremental
            };
        }

        TextDocumentOpenRegistrationOptions
            IRegistration<TextDocumentOpenRegistrationOptions, TextSynchronizationCapability>.GetRegistrationOptions(
                TextSynchronizationCapability capability,
                ClientCapabilities clientCapabilities)
        {
            return new TextDocumentOpenRegistrationOptions()
            {
                DocumentSelector = documentSelector,
            };
        }

        TextDocumentCloseRegistrationOptions
            IRegistration<TextDocumentCloseRegistrationOptions, TextSynchronizationCapability>.GetRegistrationOptions(
                TextSynchronizationCapability capability,
                ClientCapabilities clientCapabilities)
        {
            return new TextDocumentCloseRegistrationOptions() { DocumentSelector = documentSelector };
        }

        TextDocumentSaveRegistrationOptions
            IRegistration<TextDocumentSaveRegistrationOptions, TextSynchronizationCapability>.GetRegistrationOptions(
                TextSynchronizationCapability capability,
                ClientCapabilities clientCapabilities)
        {
            return new TextDocumentSaveRegistrationOptions()
            { DocumentSelector = documentSelector, IncludeText = true };
        }
    }
}