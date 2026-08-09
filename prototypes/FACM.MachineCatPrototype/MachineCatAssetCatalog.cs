using System.Reflection;
using System.Windows.Media.Imaging;

namespace FACM.MachineCatPrototype;

internal static class MachineCatAssetCatalog
{
    private static readonly Dictionary<string, BitmapImage> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static BitmapImage Get(string key)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var resourceName = $"FACM.MachineCatPrototype.Assets.{ResolveFileName(key)}.b64";
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Missing embedded machine-cat asset resource: {resourceName}");
            using var reader = new StreamReader(stream);
            var bytes = Convert.FromBase64String(reader.ReadToEnd().Trim());
            using var imageStream = new MemoryStream(bytes, writable: false);

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = imageStream;
            image.EndInit();
            image.Freeze();
            Cache[key] = image;
            return image;
        }
    }

    public static bool Contains(string key)
    {
        try
        {
            var resourceName = $"FACM.MachineCatPrototype.Assets.{ResolveFileName(key)}.b64";
            return Assembly.GetExecutingAssembly().GetManifestResourceInfo(resourceName) is not null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static string ResolveFileName(string key) => key switch
    {
        "Idle" => "Idle",
        "Walk" => "Walk",
        "Run" => "Run",
        "Observe" => "Observe",
        "Raised" => "Raised",
        "Recover" => "Recover",
        "Sleep" => "Sleep",
        "TurnFront" => "TurnFront",
        "TurnThreeQuarter" => "TurnThreeQuarter",
        "TurnSide" => "TurnSide",
        "TurnBack" => "TurnBack",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown machine-cat asset.")
    };
}
