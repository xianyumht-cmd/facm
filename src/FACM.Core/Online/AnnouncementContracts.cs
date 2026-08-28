namespace FACM.Core.Online;

public sealed record AnnouncementSnapshot(
    bool Enabled,
    string Id,
    string Title,
    string Body,
    string Level,
    bool Popup,
    string UpdatedAt,
    string LinkUrl)
{
    public Uri? DetailUri => OnlineUriPolicy.NormalizeAbsoluteHttps(LinkUrl);
}

public interface IAnnouncementSource
{
    Task<AnnouncementSnapshot?> GetAsync(CancellationToken cancellationToken = default);
}

public static class OnlineUriPolicy
{
    public static Uri? NormalizeAbsoluteHttps(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
               uri.Scheme == Uri.UriSchemeHttps &&
               !uri.IsLoopback
            ? uri
            : null;
    }

    public static string NormalizeAbsoluteHttpsString(string? value) =>
        NormalizeAbsoluteHttps(value)?.AbsoluteUri ?? string.Empty;
}
