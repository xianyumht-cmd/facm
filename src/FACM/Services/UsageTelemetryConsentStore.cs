using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace FACM.Services
{
    internal sealed class UsageTelemetryConsentStore
    {
        private const int MoveFileReplaceExisting = 0x1;
        private const int MoveFileWriteThrough = 0x8;
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly string _path;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 };

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string existingFileName, string newFileName, int flags);

        public UsageTelemetryConsentStore()
        {
            RuntimePaths.Initialize();
            _path = Path.Combine(RuntimePaths.DataDirectory, "telemetry-consent.json");
        }

        public bool Load()
        {
            try
            {
                if (!File.Exists(_path)) return false;
                var info = new FileInfo(_path);
                if (info.Length <= 0 || info.Length > 16 * 1024) return false;
                var state = _json.Deserialize<TelemetryConsentState>(File.ReadAllText(_path, Encoding.UTF8));
                return state != null && state.SchemaVersion == 1 && state.Enabled;
            }
            catch
            {
                return false;
            }
        }

        public void Save(bool enabled)
        {
            try
            {
                var text = _json.Serialize(new TelemetryConsentState
                {
                    SchemaVersion = 1,
                    Enabled = enabled
                }) + Environment.NewLine;
                WriteAtomically(text);
            }
            catch (Exception exception)
            {
                AppLog.Info("Telemetry consent save skipped: " + exception.GetType().Name);
            }
        }

        private void WriteAtomically(string text)
        {
            var directory = Path.GetDirectoryName(_path);
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, Path.GetFileName(_path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
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
                    writer.Write(text);
                    writer.Flush();
                    stream.Flush(true);
                }

                var flags = MoveFileWriteThrough | (File.Exists(_path) ? MoveFileReplaceExisting : 0);
                if (!MoveFileEx(temporary, _path, flags))
                    throw new IOException("Telemetry consent atomic replacement failed.", Marshal.GetHRForLastWin32Error());
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

        private sealed class TelemetryConsentState
        {
            public int SchemaVersion { get; set; }
            public bool Enabled { get; set; }
        }
    }
}
