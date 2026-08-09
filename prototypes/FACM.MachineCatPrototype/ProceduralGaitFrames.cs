using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FACM.MachineCatPrototype;

/// <summary>
/// Builds a small in-memory animation cache from the already approved Walk/Run PNGs.
/// No additional character artwork is generated or stored. Instead a smooth local
/// displacement field moves hand/foot regions while keeping the face and most of the
/// torso visually locked to the approved source pixels.
/// </summary>
internal static class ProceduralGaitFrames
{
    internal const int FrameCount = 32;

    private static readonly Dictionary<string, BitmapSource[]> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static BitmapSource Get(string asset, double phase)
    {
        if (!asset.Equals("Walk", StringComparison.OrdinalIgnoreCase) &&
            !asset.Equals("Run", StringComparison.OrdinalIgnoreCase))
            return MachineCatAssetCatalog.Get(asset);

        BitmapSource[] frames;
        lock (Cache)
        {
            if (!Cache.TryGetValue(asset, out frames!))
            {
                frames = BuildFrames(asset, asset.Equals("Run", StringComparison.OrdinalIgnoreCase));
                Cache[asset] = frames;
            }
        }

        phase -= Math.Floor(phase);
        if (phase < 0d) phase += 1d;
        var index = (int)Math.Floor(phase * FrameCount) % FrameCount;
        return frames[index];
    }

    private static BitmapSource[] BuildFrames(string asset, bool run)
    {
        var source = MachineCatAssetCatalog.Get(asset);
        var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0d);
        converted.Freeze();

        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = width * 4;
        var sourcePixels = new byte[stride * height];
        converted.CopyPixels(sourcePixels, stride, 0);

        var controls = run ? BuildRunControls() : BuildWalkControls();
        var weights = controls.Select(control => BuildWeightMap(width, height, control)).ToArray();
        var frames = new BitmapSource[FrameCount];
        var normalizedY = BuildNormalizedY(height);

        for (var frame = 0; frame < FrameCount; frame++)
        {
            var phase = frame / (double)FrameCount;
            var cycle = phase * Math.PI * 2d;
            var lowerBodySin = Math.Sin(cycle);
            var movements = BuildMovements(controls, cycle);
            var pixels = new byte[sourcePixels.Length];

            for (var y = 0; y < height; y++)
            {
                var lower = normalizedY[y];
                for (var x = 0; x < width; x++)
                {
                    var pixelIndex = y * width + x;
                    var dx = lower * lowerBodySin * (run ? 0.95d : 0.60d);
                    var dy = 0d;

                    for (var c = 0; c < controls.Length; c++)
                    {
                        var weight = weights[c][pixelIndex];
                        dx += weight * movements[c].Dx;
                        dy += weight * movements[c].Dy;
                    }

                    SampleBilinear(
                        sourcePixels,
                        width,
                        height,
                        stride,
                        x - dx,
                        y - dy,
                        pixels,
                        pixelIndex * 4);
                }
            }

            var bitmap = BitmapSource.Create(
                width,
                height,
                converted.DpiX,
                converted.DpiY,
                PixelFormats.Pbgra32,
                null,
                pixels,
                stride);
            bitmap.Freeze();
            frames[frame] = bitmap;
        }

        return frames;
    }

    private static Movement[] BuildMovements(ControlPoint[] controls, double cycle)
    {
        var result = new Movement[controls.Length];
        for (var i = 0; i < controls.Length; i++)
        {
            var control = controls[i];
            var angle = cycle + control.PhaseOffset;
            var sin = Math.Sin(angle);
            var cos = Math.Cos(angle);
            var lift = Math.Max(cos, 0d);
            var settle = Math.Max(-cos, 0d);
            result[i] = new Movement(
                control.HorizontalPixels * sin,
                (-control.LiftPixels * lift) + (control.ReturnPixels * settle));
        }
        return result;
    }

    private static double[] BuildNormalizedY(int height)
    {
        var result = new double[height];
        for (var y = 0; y < height; y++)
        {
            var normalized = height <= 1 ? 0d : y / (double)(height - 1);
            if (normalized <= 0.58d)
            {
                result[y] = 0d;
                continue;
            }

            var lower = (normalized - 0.58d) / 0.42d;
            result[y] = lower * lower;
        }
        return result;
    }

    private static ControlPoint[] BuildWalkControls() =>
    [
        new(0.13d, 0.70d, 0.105d, 0d,       3.0d, 3.8d, 1.15d),
        new(0.87d, 0.70d, 0.105d, Math.PI,  3.0d, 3.8d, 1.15d),
        new(0.42d, 0.84d, 0.125d, Math.PI,  5.6d, 5.0d, 1.45d),
        new(0.69d, 0.92d, 0.110d, 0d,       4.4d, 4.0d, 1.30d)
    ];

    private static ControlPoint[] BuildRunControls() =>
    [
        new(0.13d, 0.70d, 0.105d, 0d,       3.8d, 4.6d, 1.35d),
        new(0.85d, 0.58d, 0.105d, Math.PI,  3.8d, 4.6d, 1.35d),
        new(0.39d, 0.86d, 0.130d, Math.PI,  6.8d, 6.1d, 1.70d),
        new(0.69d, 0.91d, 0.115d, 0d,       5.4d, 4.8d, 1.45d)
    ];

    private static double[] BuildWeightMap(int width, int height, ControlPoint control)
    {
        var result = new double[width * height];
        var sigmaSquared2 = 2d * control.Sigma * control.Sigma;
        for (var y = 0; y < height; y++)
        {
            var v = height <= 1 ? 0d : y / (double)(height - 1);
            for (var x = 0; x < width; x++)
            {
                var u = width <= 1 ? 0d : x / (double)(width - 1);
                var du = u - control.X;
                var dv = v - control.Y;
                result[y * width + x] = Math.Exp(-((du * du) + (dv * dv)) / sigmaSquared2);
            }
        }
        return result;
    }

    private static void SampleBilinear(
        byte[] source,
        int width,
        int height,
        int stride,
        double sourceX,
        double sourceY,
        byte[] destination,
        int destinationOffset)
    {
        if (sourceX < 0d || sourceY < 0d || sourceX > width - 1d || sourceY > height - 1d)
        {
            destination[destinationOffset] = 0;
            destination[destinationOffset + 1] = 0;
            destination[destinationOffset + 2] = 0;
            destination[destinationOffset + 3] = 0;
            return;
        }

        var x0 = (int)Math.Floor(sourceX);
        var y0 = (int)Math.Floor(sourceY);
        var x1 = Math.Min(width - 1, x0 + 1);
        var y1 = Math.Min(height - 1, y0 + 1);
        var fx = sourceX - x0;
        var fy = sourceY - y0;

        var i00 = y0 * stride + x0 * 4;
        var i10 = y0 * stride + x1 * 4;
        var i01 = y1 * stride + x0 * 4;
        var i11 = y1 * stride + x1 * 4;

        for (var channel = 0; channel < 4; channel++)
        {
            var top = source[i00 + channel] + ((source[i10 + channel] - source[i00 + channel]) * fx);
            var bottom = source[i01 + channel] + ((source[i11 + channel] - source[i01 + channel]) * fx);
            var value = top + ((bottom - top) * fy);
            destination[destinationOffset + channel] = (byte)Math.Clamp((int)Math.Round(value), 0, 255);
        }
    }

    private readonly record struct Movement(double Dx, double Dy);

    private readonly record struct ControlPoint(
        double X,
        double Y,
        double Sigma,
        double PhaseOffset,
        double HorizontalPixels,
        double LiftPixels,
        double ReturnPixels);
}
