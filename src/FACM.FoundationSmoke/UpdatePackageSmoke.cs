using System.Net;
using System.Security.Cryptography;
using FACM.Core.Online;
using FACM.Core.Runtime;
using FACM.Infrastructure.Online;

internal static class UpdatePackageSmoke
{
    public static async Task RunAsync()
    {
        await ValidateVerifiedDownloadAsync();
        await ValidateBadHashNeverReplacesPackageAsync();
        await ValidateOversizedHeaderIsRejectedAsync();
    }

    private static async Task ValidateVerifiedDownloadAsync()
    {
        var root = CreateRoot();
        try
        {
            var payload = CreatePePayload(4096, seed: 17);
            var manifest = Manifest("4.1.0", payload);
            var handler = new StaticPackageHandler(payload, manifest.DownloadUrl);
            using var downloader = new HttpUpdatePackageDownloader(Layout(root), handler);
            var reports = new List<UpdateDownloadProgress>();

            var package = await downloader.DownloadAsync(manifest, new Progress<UpdateDownloadProgress>(reports.Add));

            Require(handler.Calls == 1, "Verified update download must issue exactly one package request.");
            Require(File.Exists(package.FilePath), "Verified update package was not persisted.");
            Require(package.Length == payload.Length, "Validated receipt length did not bind the downloaded bytes.");
            Require(package.Version == "4.1.0", "Validated receipt version was not normalized.");
            Require(package.DownloadUrl == manifest.DownloadUrl, "Validated receipt lost the approved release URL.");
            Require(package.Sha256 == manifest.Sha256.ToUpperInvariant(), "Validated receipt SHA did not bind the manifest identity.");
            Require(string.Equals(await HttpUpdatePackageDownloader.ComputeSha256Async(package.FilePath), package.Sha256, StringComparison.OrdinalIgnoreCase),
                "Package bytes changed after receipt creation.");
            Require(!File.Exists(package.FilePath + ".download"), "Successful download leaked the temporary file.");
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static async Task ValidateBadHashNeverReplacesPackageAsync()
    {
        var root = CreateRoot();
        try
        {
            var payload = CreatePePayload(4096, seed: 29);
            var manifest = Manifest("4.2.0", payload) with { Sha256 = new string('0', 64) };
            var destination = Path.Combine(Layout(root).UpdatesDirectory, "FACM-4.2.0.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var existing = CreatePePayload(2048, seed: 4);
            await File.WriteAllBytesAsync(destination, existing);
            using var downloader = new HttpUpdatePackageDownloader(Layout(root), new StaticPackageHandler(payload, manifest.DownloadUrl));

            try
            {
                await downloader.DownloadAsync(manifest);
                throw new InvalidOperationException("Bad SHA update unexpectedly produced a validated receipt.");
            }
            catch (InvalidDataException)
            {
            }

            Require((await File.ReadAllBytesAsync(destination)).SequenceEqual(existing),
                "Failed verification overwrote the previously validated destination package.");
            Require(!File.Exists(destination + ".download"), "Failed verification leaked a temporary package.");
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static async Task ValidateOversizedHeaderIsRejectedAsync()
    {
        var root = CreateRoot();
        try
        {
            var payload = CreatePePayload(2048, seed: 8);
            var manifest = Manifest("4.3.0", payload);
            using var downloader = new HttpUpdatePackageDownloader(
                Layout(root),
                new OversizedHeaderHandler(manifest.DownloadUrl));
            try
            {
                await downloader.DownloadAsync(manifest);
                throw new InvalidOperationException("Oversized update response was not rejected from headers.");
            }
            catch (InvalidDataException)
            {
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static UpdateManifestSnapshot Manifest(string version, byte[] payload)
    {
        var sha = Convert.ToHexString(SHA256.HashData(payload));
        return new UpdateManifestSnapshot(
            true,
            version,
            "4.0.0",
            false,
            $"https://github.com/xianyumht-cmd/facm/releases/download/v{version}/FACM.App.exe",
            sha,
            "smoke",
            "2026-08-28");
    }

    private static RuntimePathLayout Layout(string root)
    {
        var runtime = Path.Combine(root, "runtime");
        return new RuntimePathLayout(
            root,
            Path.Combine(root, "settings.ini"),
            Path.Combine(root, "settings.v2.json"),
            Path.Combine(root, "ui-text.ini"),
            Path.Combine(root, "logs"),
            runtime,
            Path.Combine(runtime, "cache"),
            Path.Combine(runtime, "pethost"),
            Path.Combine(runtime, "updates"));
    }

    private static byte[] CreatePePayload(int length, int seed)
    {
        var bytes = new byte[Math.Max(1024, length)];
        new Random(seed).NextBytes(bytes);
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        return bytes;
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-update-package-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class StaticPackageHandler(byte[] payload, string expectedUrl) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Require(request.Method == HttpMethod.Get && request.RequestUri?.AbsoluteUri == expectedUrl,
                "Update downloader escaped the validated manifest download URL.");
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
            response.Content.Headers.ContentLength = payload.LongLength;
            return Task.FromResult(response);
        }
    }

    private sealed class OversizedHeaderHandler(string expectedUrl) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(request.RequestUri?.AbsoluteUri == expectedUrl, "Oversize test used an unexpected URL.");
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            response.Content.Headers.ContentLength = HttpUpdatePackageDownloader.MaximumUpdateBytes + 1;
            return Task.FromResult(response);
        }
    }
}
