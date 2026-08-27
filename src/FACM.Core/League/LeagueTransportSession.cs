using System.Text;

namespace FACM.Core.League;

/// <summary>
/// Secret-bearing LCU transport session. Password/token material is deliberately encapsulated:
/// presentation state receives only <see cref="LeagueSessionDescriptor"/>.
/// </summary>
public sealed class LeagueTransportSession
{
    private readonly string _password;

    public LeagueTransportSession(LeagueSessionDescriptor descriptor, string password)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (descriptor.Port is <= 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(descriptor));

        var protocol = (descriptor.Protocol ?? string.Empty).Trim().ToLowerInvariant();
        if (protocol is not ("https" or "http")) throw new ArgumentException("Unsupported LCU protocol.", nameof(descriptor));

        Descriptor = descriptor with { Protocol = protocol };
        _password = password;
        BaseUri = new Uri($"{protocol}://127.0.0.1:{descriptor.Port}/", UriKind.Absolute);
    }

    public LeagueSessionDescriptor Descriptor { get; }
    public Uri BaseUri { get; }

    public string CreateBasicAuthorizationParameter() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:" + _password));

    public bool Matches(LeagueTransportSession? other) =>
        other is not null &&
        Descriptor.ProcessId == other.Descriptor.ProcessId &&
        Descriptor.Port == other.Descriptor.Port &&
        string.Equals(Descriptor.Protocol, other.Descriptor.Protocol, StringComparison.Ordinal) &&
        string.Equals(_password, other._password, StringComparison.Ordinal);

    public override string ToString() =>
        $"LeagueTransportSession(source={Descriptor.Source}, protocol={Descriptor.Protocol}, port={Descriptor.Port})";
}

public interface ILeagueTransportSessionSource
{
    LeagueTransportSession? GetSession(bool forceRefresh = false);
    void Invalidate(LeagueTransportSession expected);
}

public static class LeagueTransportSessionParser
{
    public static bool TryParseLockfile(string? content, out LeagueTransportSession? session)
    {
        session = null;
        if (string.IsNullOrWhiteSpace(content)) return false;

        var parts = content.Trim().Split(':');
        if (parts.Length < 5) return false;
        if (!int.TryParse(parts[2], out var port) || port is <= 0 or > 65535) return false;
        if (string.IsNullOrWhiteSpace(parts[3])) return false;

        var protocol = string.IsNullOrWhiteSpace(parts[4]) ? "https" : parts[4].Trim().ToLowerInvariant();
        if (protocol is not ("https" or "http")) return false;
        if (!int.TryParse(parts[1], out var processId) || processId < 0) processId = 0;

        try
        {
            session = new LeagueTransportSession(
                new LeagueSessionDescriptor(processId, port, protocol, "lockfile", null, null),
                parts[3]);
            return true;
        }
        catch
        {
            session = null;
            return false;
        }
    }

    public static bool TryParseCommandLine(string? commandLine, out LeagueTransportSession? session)
    {
        session = null;
        if (string.IsNullOrWhiteSpace(commandLine)) return false;

        var portText = ReadArgument(commandLine, "--app-port");
        var token = ReadArgument(commandLine, "--remoting-auth-token");
        if (!int.TryParse(portText, out var port) || port is <= 0 or > 65535 || string.IsNullOrWhiteSpace(token)) return false;

        var processIdText = ReadArgument(commandLine, "--app-pid");
        if (!int.TryParse(processIdText, out var processId) || processId < 0) processId = 0;

        var platformId = FirstNonEmpty(
            ReadArgument(commandLine, "--rso_platform_id"),
            ReadArgument(commandLine, "--rso-platform-id"));
        var region = ReadArgument(commandLine, "--region");

        try
        {
            session = new LeagueTransportSession(
                new LeagueSessionDescriptor(processId, port, "https", "command-line", platformId, region),
                token);
            return true;
        }
        catch
        {
            session = null;
            return false;
        }
    }

    private static string? ReadArgument(string commandLine, string key)
    {
        var marker = key + "=";
        var index = commandLine.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;

        var start = index + marker.Length;
        if (start >= commandLine.Length) return null;
        if (commandLine[start] == '"')
        {
            var endQuote = commandLine.IndexOf('"', start + 1);
            return endQuote > start ? commandLine[(start + 1)..endQuote] : null;
        }

        var end = start;
        while (end < commandLine.Length && !char.IsWhiteSpace(commandLine[end])) end++;
        return commandLine[start..end].Trim().Trim('"');
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
}
