using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace FACM.Services
{
    internal sealed class CloudIdentityState
    {
        public int SchemaVersion { get; set; } = 1;
        public string DeviceId { get; set; } = string.Empty;
        public string CloudUserId { get; set; } = string.Empty;
    }

    internal sealed class CloudIdentityStore
    {
        private const int MoveFileReplaceExisting = 0x1;
        private const int MoveFileWriteThrough = 0x8;
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly string _path;
        private readonly string _recoveryPath;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 };

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string existingFileName, string newFileName, int flags);

        public CloudIdentityStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Cloud identity directory is required.", nameof(directory));

            var fullDirectory = Path.GetFullPath(directory);
            _path = Path.Combine(fullDirectory, "cloud-identity.json");
            _recoveryPath = Path.Combine(fullDirectory, "cloud-identity.last-known-good.json");
        }

        public static CloudIdentityStore CreateDefault()
        {
            return new CloudIdentityStore(RuntimePaths.DataDirectory);
        }

        public CloudIdentityState LoadOrCreate()
        {
            CloudIdentityState state;
            if (TryLoad(_path, out state))
            {
                SaveRecovery(state);
                return state;
            }

            if (TryLoad(_recoveryPath, out state))
            {
                WriteAtomically(_path, Serialize(state));
                return state;
            }

            state = new CloudIdentityState
            {
                SchemaVersion = 1,
                DeviceId = Guid.NewGuid().ToString("D")
            };
            Save(state);
            return state;
        }

        public void SaveCloudUserId(string deviceId, string cloudUserId)
        {
            Guid parsed;
            if (!Guid.TryParse(deviceId, out parsed))
                throw new InvalidDataException("Cloud device identity is invalid.");

            var state = new CloudIdentityState
            {
                SchemaVersion = 1,
                DeviceId = parsed.ToString("D"),
                CloudUserId = (cloudUserId ?? string.Empty).Trim()
            };
            Save(state);
        }

        private void Save(CloudIdentityState state)
        {
            var normalized = Normalize(state);
            var text = Serialize(normalized);
            WriteAtomically(_path, text);
            WriteAtomically(_recoveryPath, text);
        }

        private void SaveRecovery(CloudIdentityState state)
        {
            try
            {
                WriteAtomically(_recoveryPath, Serialize(Normalize(state)));
            }
            catch
            {
            }
        }

        private bool TryLoad(string path, out CloudIdentityState state)
        {
            state = null;
            try
            {
                if (!File.Exists(path)) return false;
                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > 64 * 1024) return false;
                var parsed = _json.Deserialize<CloudIdentityState>(File.ReadAllText(path, Encoding.UTF8));
                state = Normalize(parsed);
                return true;
            }
            catch
            {
                state = null;
                return false;
            }
        }

        private static CloudIdentityState Normalize(CloudIdentityState state)
        {
            if (state == null || state.SchemaVersion != 1)
                throw new InvalidDataException("Unsupported cloud identity state.");

            Guid deviceId;
            if (!Guid.TryParse(state.DeviceId, out deviceId))
                throw new InvalidDataException("Cloud device identity is invalid.");

            return new CloudIdentityState
            {
                SchemaVersion = 1,
                DeviceId = deviceId.ToString("D"),
                CloudUserId = (state.CloudUserId ?? string.Empty).Trim()
            };
        }

        private string Serialize(CloudIdentityState state)
        {
            return _json.Serialize(state) + Environment.NewLine;
        }

        private static void WriteAtomically(string path, string text)
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidDataException("Cloud identity directory is unavailable.");

            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, Utf8NoBom))
                {
                    writer.Write(text ?? string.Empty);
                    writer.Flush();
                    stream.Flush(true);
                }

                var flags = MoveFileWriteThrough | (File.Exists(fullPath) ? MoveFileReplaceExisting : 0);
                if (!MoveFileEx(temporary, fullPath, flags))
                    throw new IOException("Cloud identity atomic replacement failed.", Marshal.GetHRForLastWin32Error());
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                catch
                {
                }
            }
        }

        internal static void ValidateForSmokeTest()
        {
            var root = Path.Combine(Path.GetTempPath(), "GGman-CloudIdentity-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var store = new CloudIdentityStore(root);
                var first = store.LoadOrCreate();
                Guid parsed;
                Require(Guid.TryParse(first.DeviceId, out parsed), "Cloud identity did not create a valid device ID.");

                var second = new CloudIdentityStore(root).LoadOrCreate();
                Require(string.Equals(first.DeviceId, second.DeviceId, StringComparison.Ordinal),
                    "Cloud identity did not persist the device ID.");

                store.SaveCloudUserId(first.DeviceId, "cloud-user-1");
                var savedText = File.ReadAllText(Path.Combine(root, "cloud-identity.json"));
                Require(savedText.IndexOf("token", StringComparison.OrdinalIgnoreCase) < 0,
                    "Cloud identity file must not persist access or refresh tokens.");
                Require(Directory.GetFiles(root, "*.tmp").Length == 0,
                    "Cloud identity atomic save left a temporary file behind.");

                File.WriteAllText(Path.Combine(root, "cloud-identity.json"), "{ broken");
                var recovered = new CloudIdentityStore(root).LoadOrCreate();
                Require(string.Equals(first.DeviceId, recovered.DeviceId, StringComparison.Ordinal),
                    "Cloud identity did not recover the stable device ID.");
                Require(string.Equals("cloud-user-1", recovered.CloudUserId, StringComparison.Ordinal),
                    "Cloud identity recovery lost the cloud user ID.");
            }
            finally
            {
                try { Directory.Delete(root, true); }
                catch { }
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
