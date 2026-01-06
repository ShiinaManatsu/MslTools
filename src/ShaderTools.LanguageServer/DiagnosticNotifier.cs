using Microsoft.Extensions.Configuration;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using ShaderTools.CodeAnalysis;
using ShaderTools.CodeAnalysis.Diagnostics;
using ShaderTools.CodeAnalysis.Hlsl.Diagnostics;
using ShaderTools.Utilities;
using ShaderTools.Utilities.Collections;
using ShaderTools.Utilities.Threading;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Protection.PlayReady;

namespace ShaderTools.LanguageServer
{
    internal sealed class DiagnosticNotifier : IDisposable
    {
        private readonly ILanguageServer _server;
        private readonly IDiagnosticService _diagnosticService;
        private readonly SimpleTaskQueue _queue;

        private readonly Dictionary<DocumentId, List<Uri>> _lastUris;

        public DiagnosticNotifier(ILanguageServer server, IDiagnosticService diagnosticService)
        {
            _server = server;
            _diagnosticService = diagnosticService;
            _queue = new SimpleTaskQueue(TaskScheduler.Default);
            _lastUris = new Dictionary<DocumentId, List<Uri>>();

            _diagnosticService.DiagnosticsUpdated += OnDiagnosticsUpdated;
        }

        public void Dispose()
        {
            _diagnosticService.DiagnosticsUpdated -= OnDiagnosticsUpdated;
        }

        private void OnDiagnosticsUpdated(object sender, DiagnosticsUpdatedEventArgs e)
        {
            _ = _queue.ScheduleTask(() => UpdateDiagnosticsAsync(e.Document));
        }

        private async Task UpdateDiagnosticsAsync(Document document)
        {
            var diagnostics = document != null
                ? await _diagnosticService.GetDiagnosticsAsync(document.Id, CancellationToken.None)
                : ImmutableArray<MappedDiagnostic>.Empty;

            var reportScope = await _server.Configuration.GetConfiguration([new ConfigurationItem { Section = "hlsl-client" }]).ConfigureAwait(false);

            var activeFileOnly = false;
            var reportTruncation = false;
            try
            {
                if (reportScope.AsEnumerable().ToDictionary()["hlsl-client:diagnostic:reportScope"] == "active")
                {
                    activeFileOnly = true;
                }
                if (reportScope.AsEnumerable().ToDictionary()["hlsl-client:diagnostic:types:reportImplicitTruncation"] == "True")
                {
                    reportTruncation = true;
                }
            }
            catch
            {
                // ignored
            }

            var diagnosticsGroupedByFile = diagnostics
                .Where(x => x.FileSpan.File.FilePath == document.FilePath || !activeFileOnly)
                .Where(x => x.Diagnostic.Descriptor.Code != (int)DiagnosticId.ImplicitTruncation || reportTruncation)
                .GroupBy(x => x.FileSpan.File.FilePath)
                .ToDictionary(x => Helpers.ToUri(x.Key), x => x.Select(Helpers.ToDiagnostic).Distinct(CachedDiagnosticComparer).ToList());

            if (!_lastUris.TryGetValue(document.Id, out var diagnosticUris))
            {
                _lastUris.Add(document.Id, diagnosticUris = new List<Uri>());
            }

            diagnosticUris.AddRange(diagnosticsGroupedByFile.Keys);

            foreach (var diagnosticUri in diagnosticUris)
            {
                if (!diagnosticsGroupedByFile.TryGetValue(diagnosticUri, out var diagnosticsForThisFile))
                {
                    diagnosticsForThisFile = [];
                }

                _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
                {
                    Uri = diagnosticUri,
                    Diagnostics = diagnosticsForThisFile
                });
            }

            diagnosticUris.Clear();
            diagnosticUris.AddRange(diagnosticsGroupedByFile.Keys);
        }

        private static readonly DiagnosticComparer CachedDiagnosticComparer = new();

        private sealed class DiagnosticComparer : IEqualityComparer<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>
        {
            public bool Equals(OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic x, OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic y)
            {
                return x.Code.Equals(y.Code)
                    && x.Message == y.Message
                    && x.Range == y.Range
                    && x.Severity == y.Severity
                    && x.Source == y.Source;
            }

            public int GetHashCode(OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic obj)
            {
                var hash = obj.Code.GetHashCode();
                hash = Hash.Combine(obj.Message.GetHashCode(), hash);
                hash = Hash.Combine(obj.Range.GetHashCode(), hash);
                hash = Hash.Combine(obj.Severity.GetHashCode(), hash);
                hash = Hash.Combine(obj.Source.GetHashCode(), hash);
                return hash;
            }
        }
    }
}
