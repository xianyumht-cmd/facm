using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using FACM.Services;

namespace FACM.Online
{
    /// <summary>
    /// One-shot hand-off from the legacy net48 single-file client to the FACM 4.0
    /// native bootstrapper. The bridge is deliberately separate from the normal
    /// updater protocol: 4.0 is a composed installation, not a replacement EXE.
    /// </summary>
    internal static class Facm4MigrationBridge
    {
        private const string BridgeVersion = "3.5.17";
        private const string StateRelativePath = @".facm\migration\bridge-state.json";
        private const string BootstrapConfigName = "bootstrap.json";

        public static bool TryStart(string[] args)
        {
            if (HasArgument(args, "--facm4-migration-test")) return false;
            if (HasArgument(args, "--facm4-migration-skip")) return false;

            Version current;
            if (!Version.TryParse(BridgeVersion, out current)) return false;
            var executingVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (executingVersion == null || executingVersion.Major != current.Major ||
                executingVersion.Minor != current.Minor || executingVersion.Build != current.Build)
                return false;

            try
            {
                RuntimePaths.Initialize();
                var statePath = GetStatePath();
                var previousStatus = ReadStateStatus(statePath);
                if (!HasArgument(args, "--facm4-migration-retry") &&
                    (string.Equals(previousStatus, "started", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(previousStatus, "installing", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(previousStatus, "failed", StringComparison.OrdinalIgnoreCase)))
                {
                    AppLog.Info("FACM 4.0 migration skipped; previous bridge attempt requires explicit retry.");
                    return false;
                }

                var snapshot = OnlineService.FetchSnapshotAsync(CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                var target = snapshot == null || snapshot.Update == null ? null : snapshot.Update.Migration;
                if (!IsValidTarget(target)) return false;

                WriteState(statePath, "started", target.Version, null);
                CopyLegacySettingsToModularRoot();

                var progress = new Progress<int>(value =>
                    AppLog.Info("FACM 4.0 migration bootstrapper download=" + value + "%"));
                var downloaded = UpdateInstaller.DownloadMigrationBootstrapperAsync(
                        target,
                        snapshot.Update.ResolvedSources,
                        progress,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                WriteBootstrapConfig(target.ManifestUrl);
                WriteState(statePath, "installing", target.Version, null);
                UpdateInstaller.StartMigrationReplacement(downloaded, target.Version, statePath);
                AppLog.Info("FACM 4.0 migration replacement started; target=" + target.Version);
                return true;
            }
            catch (Exception exception)
            {
                try
                {
                    RuntimePaths.Initialize();
                    WriteState(GetStatePath(), "failed", null, exception.Message);
                }
                catch { }

                AppLog.Error("FACM 4.0 migration bridge failed", exception);
                try
                {
                    MessageBox.Show(
                        "FACM 4.0 迁移未完成，旧版本仍可使用。\r\n\r\n" +
                        exception.Message +
                        "\r\n\r\n如需重试，请重新启动 FACM。",
                        "FACM 4.0 迁移",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                catch { }
                return false;
            }
        }

        public static int RunSmokeTest()
        {
            var valid = new Facm4MigrationTarget
            {
                Enabled = true,
                Version = "4.0.0",
                BootstrapperUrl = "https://github.com/xianyumht-cmd/facm/releases/download/v4.0.0/FACM.exe",
                BootstrapperSha256 = new string('A', 64),
                ManifestUrl = "https://github.com/xianyumht-cmd/facm/releases/download/v4.0.0/manifest.json"
            };
            Require(IsValidTarget(valid), "valid FACM 4.0 migration target rejected");

            valid.BootstrapperSha256 = "not-a-sha256";
            Require(!IsValidTarget(valid), "invalid bootstrapper hash accepted");
            valid.BootstrapperSha256 = new string('A', 64);
            valid.ManifestUrl = "http://github.com/xianyumht-cmd/facm/releases/download/v4.0.0/manifest.json";
            Require(!IsValidTarget(valid), "insecure manifest URL accepted");
            valid.ManifestUrl = "https://github.com/xianyumht-cmd/facm/releases/download/v4.0.0/manifest.json";
            valid.BootstrapperUrl = "https://github.com/xianyumht-cmd/facm/releases/download/v4.0.0/not-facm.exe";
            Require(!IsValidTarget(valid), "unexpected bootstrapper asset accepted");

            var config = BuildBootstrapConfig(valid.ManifestUrl);
            Require(config.IndexOf("\"manifestUrl\": \"" + valid.ManifestUrl + "\"", StringComparison.Ordinal) >= 0,
                "bootstrap.json did not preserve the exact production manifest URL");

            var wireManifest = new UpdateManifest { Migration = valid };
            using (var output = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(UpdateManifest)).WriteObject(output, wireManifest);
                output.Position = 0;
                var decoded = new DataContractJsonSerializer(typeof(UpdateManifest)).ReadObject(output) as UpdateManifest;
                Require(decoded != null && decoded.Migration != null &&
                        string.Equals(decoded.Migration.ManifestUrl, valid.ManifestUrl, StringComparison.Ordinal),
                    "legacy manifest did not round-trip migration metadata");
            }
            Console.WriteLine("FACM 4.0 migration bridge smoke: SUCCESS");
            return 0;
        }

        internal static bool IsValidTarget(Facm4MigrationTarget target)
        {
            if (target == null || !target.Enabled) return false;

            Version version;
            if (!Version.TryParse((target.Version ?? string.Empty).Trim(), out version) ||
                version.Major != 4 || version.Minor < 0 || version.Build < 0)
                return false;

            if (!IsHexSha256(target.BootstrapperSha256)) return false;
            if (!IsReleaseUrl(target.BootstrapperUrl, version, "FACM.exe")) return false;
            if (!IsReleaseUrl(target.ManifestUrl, version, "manifest.json")) return false;
            return true;
        }

        private static bool IsReleaseUrl(string value, Version version, string assetName)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                return false;

            var prefix = "/xianyumht-cmd/facm/releases/download/v" + version + "/";
            return uri.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(uri.AbsolutePath.Substring(prefix.Length), assetName, StringComparison.OrdinalIgnoreCase) &&
                   string.IsNullOrWhiteSpace(uri.Query) &&
                   string.IsNullOrWhiteSpace(uri.Fragment);
        }

        private static bool IsHexSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64) return false;
            return value.All(character =>
                character >= '0' && character <= '9' ||
                character >= 'a' && character <= 'f' ||
                character >= 'A' && character <= 'F');
        }

        private static string GetStatePath()
        {
            return Path.Combine(RuntimePaths.BaseDirectory, StateRelativePath);
        }

        private static string ReadStateStatus(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var text = File.ReadAllText(path, Encoding.UTF8);
                const string marker = "\"status\"";
                var markerIndex = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex < 0) return null;
                var quote = text.IndexOf('"', markerIndex + marker.Length);
                if (quote < 0) return null;
                var end = text.IndexOf('"', quote + 1);
                return end > quote ? text.Substring(quote + 1, end - quote - 1) : null;
            }
            catch { return null; }
        }

        private static void WriteState(string path, string status, string targetVersion, string error)
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory)) throw new InvalidDataException("FACM 4.0 migration state path is invalid.");
            Directory.CreateDirectory(directory);
            var json = "{\n" +
                       "  \"schemaVersion\": 1,\n" +
                       "  \"status\": \"" + EscapeJson(status) + "\",\n" +
                       "  \"targetVersion\": \"" + EscapeJson(targetVersion ?? string.Empty) + "\",\n" +
                       "  \"updatedAt\": \"" + EscapeJson(DateTimeOffset.UtcNow.ToString("o")) + "\",\n" +
                       "  \"error\": \"" + EscapeJson(error ?? string.Empty) + "\"\n" +
                       "}\n";
            AtomicWrite(path, json);
        }

        private static void WriteBootstrapConfig(string manifestUrl)
        {
            AtomicWrite(
                Path.Combine(RuntimePaths.BaseDirectory, BootstrapConfigName),
                BuildBootstrapConfig(manifestUrl));
        }

        private static string BuildBootstrapConfig(string manifestUrl)
        {
            return "{\n" +
                   "  \"schemaVersion\": 1,\n" +
                   "  \"manifestUrl\": \"" + EscapeJson(manifestUrl) + "\",\n" +
                   "  \"manifestMirrors\": [],\n" +
                   "  \"allowUnsignedLocal\": false,\n" +
                   "  \"allowInsecureLocal\": false\n" +
                   "}\n";
        }

        private static void CopyLegacySettingsToModularRoot()
        {
            var source = RuntimePaths.SettingsPath;
            if (!File.Exists(source)) return;

            var destination = Path.Combine(RuntimePaths.BaseDirectory, ".facm", "settings.ini");
            if (File.Exists(destination)) return;
            var directory = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(directory);
            var temporary = destination + ".migration-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(source, temporary, false);
                if (!File.Exists(destination)) File.Move(temporary, destination);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
        }

        private static void AtomicWrite(string path, string content)
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory)) throw new InvalidDataException("FACM migration output path is invalid.");
            Directory.CreateDirectory(directory);
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            try
            {
                if (File.Exists(path))
                {
                    try { File.Replace(temporary, path, null, true); }
                    catch (PlatformNotSupportedException)
                    {
                        File.Delete(path);
                        File.Move(temporary, path);
                    }
                    catch (IOException)
                    {
                        File.Delete(path);
                        File.Move(temporary, path);
                    }
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        }

        private static bool HasArgument(string[] args, string value)
        {
            return args != null && args.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
