namespace FACM.App.Models;

public sealed class PayloadManifest
{
    public int SchemaVersion { get; set; } = 1;
    public List<PayloadDefinition> Payloads { get; set; } = [];
}

public sealed class PayloadDefinition
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public bool RequiresElevation { get; set; }
}

public sealed record PayloadRunResult(
    bool Started,
    string ExecutablePath,
    string Message);
