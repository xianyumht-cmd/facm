using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace FACM.ToolBundle
{
    public static class EmbeddedToolStore
    {
        private const string BundleVersion = "1";

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
                {
                    "tool-a",
                    new Descriptor(
                        "FACM.ToolBundle.Resources.ToolA",
                        "FACM-Tool-A.exe",
                        "4180BAE46BED95661D63DC8D08DD458AE866CC107AB0F00AFC647B9BEB8B4ECA")
                },
                {
                    "mode-tool",
                    new Descriptor(
                        "FACM.ToolBundle.Resources.ModeTool",
                        "FACM-Mode-Tool.exe",
                        "A30E8ABD86AF01746EC63E2B51F80B83703965D5F1001768236F8BE3B5A3B935")
                },
                {
                    "mode-script-1",
                    new Descriptor(
                        "FACM.ToolBundle.Resources.ModeScript1",
                        "FACM-Mode-1.cmd",
                        "6AA4FD59A1BDD9D262123ABA1673A6C255E98AE2BC0BBC0ECFC1A839936A8535")
                },
                {
                    "mode-script-2",
                    new Descriptor(
                        "FACM.ToolBundle.Resources.ModeScript2",
                        "FACM-Mode-2.cmd",
                        "5574C930E21604C660FA52B294A974D184BC07561F19CF6548186F66A7E4B51C")
                },
                {
                    "mode-script-3",
                    new Descriptor(
                        "FACM.ToolBundle.Resources.ModeScript3",
                        "FACM-Mode-3.cmd",
                        "35DE4B643CBCCF867F2533D257754A55877B414EA7D341B208AF8F7B3D9D4447")
                },
                {
                    "mode-script-4",
                    new Descriptor(
                        "FACM.ToolBundle.Resources.ModeScript4",
                        "FACM-Mode-4.cmd",
                        "48F6ED8E06B3F96CF4791AF4FE9105327FC230267801A1DEC731784D0CA859E1")
                }
            };

        public static string Extract(string toolId)
        {
            Descriptor descriptor;
            if (string.IsNullOrWhiteSpace(toolId) || !Tools.TryGetValue(toolId, out descriptor))
            {
                throw new ArgumentException("Unknown embedded tool id.", nameof(toolId));
            }

            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FACM",
                "Tools",
                "Bundle-" + BundleVersion);
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
