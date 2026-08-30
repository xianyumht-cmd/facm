using FACM.Core.Observability;

namespace FACM.App.ViewModels;

public sealed class DiagnosticsCenterViewModel
{
    private readonly IDiagnosticsSnapshotSource _source;
    private readonly IDiagnosticsBundleExporter _exporter;
    private readonly Func<IReadOnlyDictionary<string, string>>? _runtimeFactsProvider;
    private DiagnosticsSnapshot? _snapshot;

    public DiagnosticsCenterViewModel(
        IDiagnosticsSnapshotSource source,
        IDiagnosticsBundleExporter exporter,
        Func<IReadOnlyDictionary<string, string>>? runtimeFactsProvider = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
        _runtimeFactsProvider = runtimeFactsProvider;
    }

    public string Summary { get; private set; } = string.Empty;
    public DiagnosticsExportReceipt? LastExport { get; private set; }

    public async Task<string> RefreshAsync(CancellationToken cancellationToken = default)
    {
        _snapshot = AddRuntimeFacts(await _source.CaptureAsync(cancellationToken).ConfigureAwait(false));
        Summary = DiagnosticsSummaryFormatter.Format(_snapshot);
        return Summary;
    }

    public async Task<DiagnosticsExportReceipt> ExportAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = _snapshot ?? AddRuntimeFacts(await _source.CaptureAsync(cancellationToken).ConfigureAwait(false));
        _snapshot = snapshot;
        Summary = DiagnosticsSummaryFormatter.Format(snapshot);
        LastExport = await _exporter.ExportAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return LastExport;
    }

    private DiagnosticsSnapshot AddRuntimeFacts(DiagnosticsSnapshot snapshot)
    {
        if (_runtimeFactsProvider is null) return snapshot;

        try
        {
            var facts = snapshot.RuntimeFacts.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
            foreach (var pair in _runtimeFactsProvider())
            {
                if (string.IsNullOrWhiteSpace(pair.Key)) continue;
                facts[pair.Key] = pair.Value ?? string.Empty;
            }
            return snapshot with { RuntimeFacts = facts };
        }
        catch
        {
            // Runtime facts are an optional diagnostics enhancement and must never block export.
            return snapshot;
        }
    }
}
