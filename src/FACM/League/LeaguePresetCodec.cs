using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FACM.League
{
    // Keeps large champion catalogs within the existing bounded settings/cloud string fields.
    // Legacy JSON remains readable. Untrusted compressed input has a hard expansion limit.
    internal static class LeaguePresetCodec
    {
        private const string Prefix = "z1:";
        internal const int MaxExpandedBytes = 128 * 1024;

        internal static string Decode(string value, int maxStoredLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Length > maxStoredLength) return null;
            if (!value.StartsWith(Prefix, StringComparison.Ordinal)) return value;
            try
            {
                var compressed = Convert.FromBase64String(value.Substring(Prefix.Length));
                using (var input = new MemoryStream(compressed, false))
                using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[4096];
                    int read;
                    while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (output.Length + read > MaxExpandedBytes) return null;
                        output.Write(buffer, 0, read);
                    }
                    return new UTF8Encoding(false, true).GetString(output.ToArray());
                }
            }
            catch (ArgumentException) { }
            catch (FormatException) { }
            catch (InvalidDataException) { }
            catch (IOException) { }
            catch (DecoderFallbackException) { }
            return null;
        }

        internal static string Encode(string json, int maxStoredLength)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var bytes = Encoding.UTF8.GetBytes(json);
            if (bytes.Length > MaxExpandedBytes)
                throw new ArgumentException("Champion preset catalog exceeds its expanded size limit.");
            if (json.Length <= maxStoredLength) return json;
            using (var output = new MemoryStream())
            {
                using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true))
                    deflate.Write(bytes, 0, bytes.Length);
                var stored = Prefix + Convert.ToBase64String(output.ToArray());
                if (stored.Length > maxStoredLength)
                    throw new ArgumentException("Champion preset catalog exceeds its stored size limit.");
                return stored;
            }
        }
    }
}
