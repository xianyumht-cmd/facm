using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace FACM.ToolBundle
{
    public static class EmbeddedToolStore
    {
        private sealed class Descriptor
        {
            public Descriptor(string resourceName, string outputName, string sha256)
            {
                ResourceName = resourceName;
                OutputName = outputName;
                Sha256 = sha256;
            }

            public string ResourceName { get; }
            public string OutputName { get; }
            public string Sha256 { get; }
        }

        private static readonly Dictionary<string, Descriptor> Tools =
            new Dictionary<string, Descriptor>(StringComparer.OrdinalIgnoreCase)
            {
                { "tool-a", new Descriptor("FACM.ToolBundle.Resources.ToolA", "FACM-Tool-A.exe", "4180BAE46BED95661D63DC8D08DD458AE866CC107AB0F00AFC647B9BEB8B4ECA") }
            };

        public static string Extract(string toolId)
        {
            Descriptor descriptor;
            if (string.IsNullOrWhiteSpace(toolId) || !Tools.TryGetValue(toolId, out descriptor))
            {
                throw new ArgumentException("Unknown embedded tool id.", nameof(toolId));
            }

            var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime");
            Directory.CreateDirectory(directory);

            var outputPath = Path.Combine(directory, descriptor.OutputName);
            if (File.Exists(outputPath) && IsExpectedFile(outputPath, descriptor.Sha256))
            {
                return outputPath;
            }

            var temporaryPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(descriptor.ResourceName))
                {
                    if (resource == null)
                    {
                        throw new InvalidOperationException("Embedded tool resource is missing: " + descriptor.ResourceName);
                    }

                    using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        resource.CopyTo(output);
                    }
                }

                if (!IsExpectedFile(temporaryPath, descriptor.Sha256))
                {
                    throw new InvalidDataException("Embedded tool integrity check failed.");
                }

                if (File.Exists(outputPath)) File.Delete(outputPath);
                File.Move(temporaryPath, outputPath);
                return outputPath;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                catch
                {
                }
            }
        }

        public static string GetExpectedSha256(string toolId)
        {
            Descriptor descriptor;
            if (!Tools.TryGetValue(toolId ?? string.Empty, out descriptor))
            {
                throw new ArgumentException("Unknown embedded tool id.", nameof(toolId));
            }
            return descriptor.Sha256;
        }

        private static bool IsExpectedFile(string path, string expectedSha256)
        {
            return string.Equals(ComputeSha256(path), expectedSha256, StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeSha256(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }
    }
}
