using FACM.Core.Text;

namespace FACM.Infrastructure.Text;

public sealed class FileUiTextProvider : IUiTextProvider
{
    private readonly DictionaryUiTextProvider _inner;

    public FileUiTextProvider(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        IReadOnlyDictionary<string, string>? overrides = null;
        try
        {
            if (File.Exists(path))
                overrides = LegacyUiTextOverrideCodec.Parse(File.ReadLines(path));
        }
        catch (IOException)
        {
            // UI copy customization is optional. Defaults remain usable when the override file
            // cannot be read; product startup must not depend on a cosmetic text file.
        }
        catch (UnauthorizedAccessException)
        {
        }

        _inner = new DictionaryUiTextProvider(overrides);
    }

    public string Get(string key) => _inner.Get(key);
}
