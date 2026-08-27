using FACM.Core.Observability;

namespace FACM.App.ViewModels;

public sealed class DiagnosticsCenterViewModel
{
    private readonly IDiagnosticsSnapshotSource _source;
    private readonly IDiagnosticsBundleExporter _exporter;
    private DiagnosticsSnapshot? _snapshot;

    public DiagnosticsCenterViewModel(
        IDiagnosticsSnapshotSource source,
        IDiagnosticsBundleExporter exporter)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
    }

    public string Summary { get; private set; } = string.Empty;
    public DiagnosticsExportReceipt? LastExport { get; private set; }

    public async Task<string> RefreshAsync(CancellationToken cancellationToken = default)
    {
        _snapshot = await _source.CaptureAsync(cancellationToken).ConfigureAwait(false);
        Summary = DiagnosticsSummaryFormatter.Format(_snapshot);
        return Summary;
    }

    public async Task<DiagnosticsExportReceipt> ExportAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = _snapshot ?? await _source.CaptureAsync(cancellationToken).ConfigureAwait(false);
        _snapshot = snapshot;
        Summary = DiagnosticsSummaryFormatter.Format(snapshot);
        LastExport = await _exporter.ExportAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return LastExport;
    }
}
