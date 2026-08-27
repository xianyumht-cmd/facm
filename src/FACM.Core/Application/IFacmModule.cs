namespace FACM.Core.Application;

public interface IFacmModule : IDisposable
{
    string Id { get; }

    IReadOnlyList<string> Dependencies { get; }

    void Initialize();
}
