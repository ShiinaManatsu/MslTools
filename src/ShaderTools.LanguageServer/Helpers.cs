using Microsoft.CodeAnalysis.Text;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using ShaderTools.CodeAnalysis;
using ShaderTools.CodeAnalysis.NavigateTo;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace ShaderTools.LanguageServer
{
    internal static class Helpers
    {
        public static string FromUri(DocumentUri uri)
        {
            return uri.GetFileSystemPath();
        }

        public static Uri ToUri(string filePath)
        {
            filePath = filePath.Replace('\\', '/');

            return (!filePath.StartsWith("/"))
                ? new Uri($"file:///{filePath}")
                : new Uri($"file://{filePath}");
        }

        public static Range ToRange(SourceText sourceText, TextSpan textSpan)
        {
            var linePositionSpan = sourceText.Lines.GetLinePositionSpan(textSpan);

            return new Range
            {
                Start = new Position
                {
                    Line = linePositionSpan.Start.Line,
                    Character = linePositionSpan.Start.Character
                },
                End = new Position
                {
                    Line = linePositionSpan.End.Line,
                    Character = linePositionSpan.End.Character
                }
            };
        }

        public static TextSpan ToSpan(SourceText sourceText, Range range)
        {
            var start = range.Start;
            var end = range.End;

            return new TextSpan(sourceText.Lines.GetPosition(new LinePosition(start.Line, start.Character)),
                sourceText.Lines.GetPosition(new LinePosition(end.Line, end.Character)));
        }

        private const string DiagnosticSourceName = "Shader Tools";

        public static Diagnostic ToDiagnostic(this CodeAnalysis.Diagnostics.MappedDiagnostic diagnostic)
        {
            var sourceFileSpan = diagnostic.FileSpan;

            var linePositionSpan = ToRange(sourceFileSpan.File.Text, sourceFileSpan.Span);

            var severity = ToDiagnosticSeverity(diagnostic.Diagnostic.Severity);
            return new Diagnostic
            {
                Severity = severity,
                Message = diagnostic.Diagnostic.Message,
                Code = diagnostic.Diagnostic.Descriptor.Id,
                Source = DiagnosticSourceName,
                Range = linePositionSpan,
                Tags = severity == DiagnosticSeverity.Hint ? new List<DiagnosticTag> { DiagnosticTag.Unnecessary } : []
            };
        }

        private static DiagnosticSeverity ToDiagnosticSeverity(CodeAnalysis.Diagnostics.DiagnosticSeverity severity)
        {
            switch (severity)
            {
                case CodeAnalysis.Diagnostics.DiagnosticSeverity.Error:
                    return DiagnosticSeverity.Error;

                case CodeAnalysis.Diagnostics.DiagnosticSeverity.Warning:
                    return DiagnosticSeverity.Warning;

                case CodeAnalysis.Diagnostics.DiagnosticSeverity.Info:
                    return DiagnosticSeverity.Hint;

                default:
                    return DiagnosticSeverity.Error;
            }
        }

        public static TextChange ToTextChange(Document document, Range changeRange, string insertString)
        {
            var startPosition = document.SourceText.Lines.GetPosition(ToLinePosition(changeRange.Start));
            var endPosition = document.SourceText.Lines.GetPosition(ToLinePosition(changeRange.End));

            return new TextChange(
                TextSpan.FromBounds(startPosition, endPosition),
                insertString);
        }

        private static LinePosition ToLinePosition(Position position)
        {
            return new LinePosition(position.Line, position.Character);
        }

        public static async Task FindSymbolsInDocumentAsync(
            INavigateToSearchService searchService,
            Document document,
            string searchPattern,
            CancellationToken cancellationToken,
            ImmutableArray<SymbolInformation>.Builder resultsBuilder)
        {
            var foundSymbols = await searchService.SearchDocumentAsync(document, searchPattern, cancellationToken);

            resultsBuilder.AddRange(foundSymbols
                .Select(r => new SymbolInformation
                {
                    ContainerName = r.AdditionalInformation,
                    Kind = GetSymbolKind(r.Kind),
                    Location = new Location
                    {
                        Uri = ToUri(r.NavigableItem.SourceSpan.File.FilePath),
                        Range = ToRange(r.NavigableItem.Document.SourceText, r.NavigableItem.SourceSpan.Span)
                    },
                    Name = r.Name
                }));
        }

        public static async Task FindSymbolsInDocumentAsync(
            INavigateToSearchService searchService,
            Document document,
            string searchPattern,
            CancellationToken cancellationToken,
            ImmutableArray<WorkspaceSymbol>.Builder resultsBuilder)
        {
            var foundSymbols = await searchService.SearchDocumentAsync(document, searchPattern, cancellationToken);

            resultsBuilder.AddRange(foundSymbols
                .Select(r => new WorkspaceSymbol
                {
                    ContainerName = r.AdditionalInformation,
                    Kind = GetSymbolKind(r.Kind),
                    Location = new Location
                    {
                        Uri = ToUri(r.NavigableItem.SourceSpan.File.FilePath),
                        Range = ToRange(r.NavigableItem.Document.SourceText, r.NavigableItem.SourceSpan.Span)
                    },
                    Name = r.Name
                }));
        }

        private static SymbolKind GetSymbolKind(string symbolType)
        {
            switch (symbolType)
            {
                case NavigateToItemKind.Class:
                    return SymbolKind.Class;

                case NavigateToItemKind.Structure:
                    return SymbolKind.Struct;

                case NavigateToItemKind.Module:
                    return SymbolKind.Namespace;

                case NavigateToItemKind.Interface:
                    return SymbolKind.Interface;

                case NavigateToItemKind.Field:
                    return SymbolKind.Field;

                case NavigateToItemKind.Method:
                    return SymbolKind.Method;

                default:
                    return SymbolKind.Variable;
            }
        }

        public static string ToLspLanguage(string language)
        {
            return language.ToLowerInvariant();
        }

        public static async Task<dynamic> GetConfigurationAsync<T>(
            ILanguageServer server,
            string key)
        {
            try
            {                
                var configuration = await server.Configuration
                    .GetConfiguration(new ConfigurationItem { Section = "hlsl-client" })
                    .ConfigureAwait(false);

                key = key.Replace(".", ":");
                return typeof(T) switch
                {
                    var type when type == typeof(bool) =>configuration.AsEnumerable().ToDictionary()[key] == "True",
                    _ => configuration.AsEnumerable().ToDictionary()[key]
                };
            }
            catch
            {
                return default(T);
            }
        }
    }
}
