using System.Net;
using System.Security.Cryptography;
using FACM.Core.Online;
using FACM.Core.Runtime;
using FACM.Infrastructure.Online;

internal static class PreparedUpdateInstallerSmoke
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-update-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bytes = Enumerable.Range(0, 4096).Select(index => (byte)(index % 251)).ToArray();
            var sha = Convert.ToHexString(SHA256.HashData(bytes));
            var handler = new StaticHandler(bytes);
            var launcher = new FakeLauncher();
            var verifier = new FakeIdentityVerifier();
            var layout = new RuntimePathLayout(
                root,
                Path.Combine(root, "settings.ini"),
                Path.Combine(root, "settings.v2.json"),
                Path.Combine(root, "ui-text.ini"),
                Path.Combine(root, "logs"),
                Path.Combine(root, "runtime"),
                Path.Combine(root, "runtime", "cache"),
                Path.Combine(root, "runtime", "pethost"),
                Path.Combine(root, "runtime", "updates"));
            using var installer = new HttpPreparedUpdateInstaller(layout, launcher, verifier, handler);
            var manifest = new UpdateManifestSnapshot(
                true,
                "4.0.1",
                "4.0.0",
                false,
                "https://github.com/xianyumht-cmd/facm/releases/download/v4.0.1/FACM.App.exe",
                sha,
                "smoke",
                "2026-08-28");

            var progress = new List<UpdateDownloadProgress>();
            var prepared = await installer.PrepareAsync(manifest, new Progress<UpdateDownloadProgress>(progress.Add));
            Require(prepared.ReceiptId.Length >= 16, "Prepared update did not issue an opaque receipt.");
            Require(File.Exists(prepared.PackagePath), "Prepared update package was not written.");
            Require(prepared.PackagePath.StartsWith(Path.GetFullPath(layout.UpdatesDirectory), StringComparison.OrdinalIgnoreCase),
                "Prepared update escaped RuntimePathLayout.UpdatesDirectory.");
            Require(prepared.Length == bytes.LongLength && prepared.Sha256.Equals(sha, StringComparison.OrdinalIgnoreCase),
                "Prepared package identity was not bound to the validated bytes.");
            Require(verifier.Calls == 1 && verifier.LastVersion == "4.0.1",
                "Downloaded update did not cross the package identity verifier exactly once.");

            await File.AppendAllTextAsync(prepared.PackagePath, "tamper");
            var tampered = await installer.StartReplacementAsync(prepared);
            Require(!tampered.Started && tampered.Reason is "package-length-changed" or "package-hash-changed",
                "Tampered package reached the replacement launcher.");
            Require(launcher.Calls == 0, "Replacement launcher ran for a tampered package.");
            Require(verifier.Calls == 1, "Tampered bytes should fail receipt/hash checks before identity revalidation.");

            var preparedAgain = await installer.PrepareAsync(manifest);
            Require(verifier.Calls == 2, "Second prepared package was not identity-verified.");
            var forged = preparedAgain with { ReceiptId = Guid.NewGuid().ToString("N") };
            var forgedResult = await installer.StartReplacementAsync(forged);
            Require(!forgedResult.Started && forgedResult.Reason == "receipt-missing",
                "A forged update receipt was accepted.");
            Require(verifier.Calls == 2, "Forged receipt should fail before identity revalidation.");

            var started = await installer.StartReplacementAsync(preparedAgain);
            Require(started.Started && started.Reason == "replacement-started",
                "Validated package did not cross the narrow replacement boundary.");
            Require(launcher.Calls == 1, "Validated replacement should launch exactly once.");
            Require(launcher.LastPath == preparedAgain.PackagePath && launcher.LastHash == sha && launcher.LastVersion == "4.0.1",
                "Replacement launcher received a different package identity.");
            Require(verifier.Calls == 3 && verifier.LastPath == preparedAgain.PackagePath,
                "Replacement did not revalidate the prepared package identity immediately before launch.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class StaticHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(request.Method == HttpMethod.Get, "Update downloader issued a non-GET request.");
            Require(request.RequestUri?.Scheme == Uri.UriSchemeHttps && request.RequestUri.Host == "github.com",
                "Update downloader escaped the validated HTTPS GitHub origin.");
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentLength = bytes.LongLength;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class FakeIdentityVerifier : IUpdatePackageIdentityVerifier
    {
        public int Calls { get; private set; }
        public string LastPath { get; private set; } = string.Empty;
        public string LastVersion { get; private set; } = string.Empty;

        public void Validate(string packagePath, string expectedVersion)
        {
            Calls++;
            LastPath = Path.GetFullPath(packagePath);
            LastVersion = expectedVersion;
        }
    }

    private sealed class FakeLauncher : IUpdateReplacementLauncher
    {
        public int Calls { get; private set; }
        public string LastPath { get; private set; } = string.Empty;
        public string LastHash { get; private set; } = string.Empty;
        public string LastVersion { get; private set; } = string.Empty;

        public Task<bool> StartAsync(
            string validatedPackagePath,
            string expectedSha256,
            string version,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastPath = validatedPackagePath;
            LastHash = expectedSha256;
            LastVersion = version;
            return Task.FromResult(true);
        }
    }
}
