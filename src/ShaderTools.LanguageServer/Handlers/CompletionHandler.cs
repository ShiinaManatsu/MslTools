using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using ShaderTools.CodeAnalysis;
using ShaderTools.CodeAnalysis.Completion;
using ShaderTools.CodeAnalysis.Shared.Extensions;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CompletionItem = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItem;
using CompletionList = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionList;
using CompletionTrigger = Microsoft.CodeAnalysis.Completion.CompletionTrigger;

namespace ShaderTools.LanguageServer.Handlers
{
    internal sealed class CompletionHandler : ICompletionHandler
    {
        private readonly LanguageServerWorkspace _workspace;
        private readonly CompletionRegistrationOptions _registrationOptions;

        public CompletionHandler(LanguageServerWorkspace workspace, TextDocumentSelector documentSelector)
        {
            _workspace = workspace;
            _registrationOptions = new CompletionRegistrationOptions
            {
                DocumentSelector = documentSelector,
                TriggerCharacters = new Container<string>(".", ":", " ", "#", "+", "-", "*", "/", ",", "<", "("),
                ResolveProvider = false
            };
        }

        public async Task<CompletionList> Handle(CompletionParams request, CancellationToken token)
        {
            var (document, position) = _workspace.GetLogicalDocument(request);

            var completionService = document.GetLanguageService<CompletionService>();

            CompletionTrigger trigger;
            if (request.Context.TriggerKind == CompletionTriggerKind.TriggerCharacter)
            {
                trigger = CompletionTrigger.CreateInsertionTrigger(request.Context.TriggerCharacter[0]);
            }
            else
            {
                trigger = CompletionTrigger.Invoke;
            }

            try
            {
                var completionList = await completionService.GetCompletionsAsync(document, position, trigger, cancellationToken: token).ConfigureAwait(false);
                if (completionList == null)
                {
                    return new CompletionList();
                }

                var completionItems = completionList.Items
                    .Select(x => ConvertCompletionItem(document, completionList.Rules, x))
                    .ToArray();

                return completionItems;
            }
            catch (Exception)
            {
                return new CompletionList();
            }
        }

        private static CompletionItem ConvertCompletionItem(Document document, Microsoft.CodeAnalysis.Completion.CompletionRules rules, CodeAnalysis.Completion.CompletionItem item)
        {
            var description = CommonCompletionItem.GetDescription(item);

            // A bit hacky: everything before the line break is the Detail, everything after is the Documentation.
            var detail = string.Empty;
            var documentation = string.Empty;
            var seenLineBreak = false;
            foreach (var taggedText in description.TaggedParts)
            {
                if (seenLineBreak)
                {
                    documentation += taggedText.Text;
                }
                else
                {
                    if (taggedText.Text.ContainsLineBreak())
                    {
                        seenLineBreak = true;
                    }
                    else
                    {
                        detail += taggedText.Text;
                    }
                }
            }

            return new CompletionItem
            {
                Label = item.DisplayText,
                SortText = item.SortText,
                FilterText = item.FilterText,
                Kind = GetKind(item.Glyph),
                TextEdit = new TextEdit
                {
                    NewText = item.DisplayText,
                    Range = Helpers.ToRange(document.SourceText, item.Span)
                },
                Detail = detail,
                CommitCharacters = rules.DefaultCommitCharacters.Select(x => x.ToString()).ToArray(),
                //CommitCharacters = rules.CommitCharacterRules.Select(x => x.ToString()).ToArray(),
                Documentation = documentation,
            };
        }

        private static CompletionItemKind GetKind(Glyph glyph)
        {
            switch (glyph)
            {
                case Glyph.None:
                    return CompletionItemKind.Class;
                case Glyph.Class:
                    return CompletionItemKind.Class;
                case Glyph.Constant:
                    return CompletionItemKind.Constant;
                case Glyph.Field:
                    return CompletionItemKind.Field;
                case Glyph.Interface:
                    return CompletionItemKind.Interface;
                case Glyph.IntrinsicClass:
                    return CompletionItemKind.Class;
                case Glyph.IntrinsicStruct:
                    return CompletionItemKind.Struct;
                case Glyph.Keyword:
                    return CompletionItemKind.Keyword;
                case Glyph.Label:
                    return CompletionItemKind.Keyword;
                case Glyph.Local:
                    return CompletionItemKind.Variable;
                case Glyph.Macro:
                    return CompletionItemKind.Reference;
                case Glyph.Namespace:
                    return CompletionItemKind.Module;
                case Glyph.Method:
                    return CompletionItemKind.Method;
                case Glyph.Module:
                    return CompletionItemKind.Module;
                case Glyph.OpenFolder:
                    return CompletionItemKind.Folder;
                case Glyph.Operator:
                    return CompletionItemKind.Operator;
                case Glyph.Parameter:
                    return CompletionItemKind.Variable;
                case Glyph.Structure:
                    return CompletionItemKind.Struct;
                case Glyph.Typedef:
                    return CompletionItemKind.Variable;
                case Glyph.TypeParameter:
                    return CompletionItemKind.TypeParameter;
                case Glyph.CompletionWarning:
                    return CompletionItemKind.Snippet;
                case Glyph.Toggle:
                    return CompletionItemKind.Variable;
                default:
                    throw new ArgumentOutOfRangeException(nameof(glyph));
            }
        }

        public CompletionRegistrationOptions GetRegistrationOptions(CompletionCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return _registrationOptions;
        }
    }
}
