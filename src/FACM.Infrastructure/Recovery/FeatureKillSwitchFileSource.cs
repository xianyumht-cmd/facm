using System.Text.Json;
using FACM.Core.Recovery;

namespace FACM.Infrastructure.Recovery;

public sealed class FeatureKillSwitchFileSource : IFeatureKillSwitchSource
{
    private const long MaxDocumentBytes = 32 * 1024;
    private const int CurrentSchemaVersion = 1;
    private readonly string _path;

    public FeatureKillSwitchFileSource(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task<FeatureKillSwitchLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_path))
            return new FeatureKillSwitchLoadResult(FeatureKillSwitch.None, FeatureKillSwitchLoadOrigin.Missing, "kill-switch-missing");

        try
        {
            var info = new FileInfo(_path);
            if (info.Length is < 0 or > MaxDocumentBytes) return FailClosed("kill-switch-size-invalid");

            var json = await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
            return Parse(json);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return FailClosed("kill-switch-read-invalid");
        }
    }

    public static FeatureKillSwitchLoadResult Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return FailClosed("kill-switch-empty");
        if (json.Length > MaxDocumentBytes) return FailClosed("kill-switch-size-invalid");

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return FailClosed("kill-switch-root-invalid");

            foreach (var property in root.EnumerateObject())
            {
                if (property.Name is not ("schemaVersion" or "disabled"))
                    return FailClosed("kill-switch-property-unknown");
            }

            if (!root.TryGetProperty("schemaVersion", out var schema) ||
                schema.ValueKind != JsonValueKind.Number ||
                !schema.TryGetInt32(out var schemaVersion) ||
                schemaVersion != CurrentSchemaVersion)
            {
                return FailClosed("kill-switch-schema-invalid");
            }

            if (!root.TryGetProperty("disabled", out var disabled) || disabled.ValueKind != JsonValueKind.Array)
                return FailClosed("kill-switch-disabled-invalid");

            var approved = FeatureBaseline.GetApprovedCapabilities()
                .ToDictionary(capability => capability.ToString(), StringComparer.Ordinal);
            var parsed = new HashSet<FacmFeatureCapability>();
            var count = 0;
            foreach (var item in disabled.EnumerateArray())
            {
                count++;
                if (count > 64 || item.ValueKind != JsonValueKind.String)
                    return FailClosed("kill-switch-disabled-invalid");
                var name = item.GetString();
                if (name is null || !approved.TryGetValue(name, out var capability))
                    return FailClosed("kill-switch-capability-unknown");
                parsed.Add(capability);
            }

            return new FeatureKillSwitchLoadResult(
                new FeatureKillSwitch(parsed),
                FeatureKillSwitchLoadOrigin.Loaded,
                "kill-switch-loaded");
        }
        catch (JsonException)
        {
            return FailClosed("kill-switch-json-invalid");
        }
    }

    private static FeatureKillSwitchLoadResult FailClosed(string reason) => new(
        FeatureKillSwitch.DisableAllApproved(),
        FeatureKillSwitchLoadOrigin.FailClosed,
        reason);
}
