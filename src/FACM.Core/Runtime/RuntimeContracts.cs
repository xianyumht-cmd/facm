namespace FACM.Core.Runtime;

public interface IExecutablePathProvider
{
    string ExecutablePath { get; }
    string BaseDirectory { get; }
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
