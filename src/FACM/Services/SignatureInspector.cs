using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace FACM.Services
{
    internal static class SignatureInspector
    {
        private static readonly Guid WinTrustActionGenericVerifyV2 =
            new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        private const uint WtdUiNone = 2;
        private const uint WtdRevokeNone = 0;
        private const uint WtdChoiceFile = 1;
        private const uint WtdStateActionIgnore = 0;
        private const uint WtdRevocationCheckNone = 0x00000010;
        private const uint WtdCacheOnlyUrlRetrieval = 0x00001000;
        private const int CertEUntrustedRoot = unchecked((int)0x800B0109);
        private const int CertEChaining = unchecked((int)0x800B010A);

        public static string GetCurrentExecutableSignatureStatus()
        {
            try
            {
                var location = System.Reflection.Assembly.GetExecutingAssembly().Location;
                using (var certificate2 = LoadSigner(location))
                {
                    return "已签名：" + certificate2.GetNameInfo(X509NameType.SimpleName, false);
                }
            }
            catch
            {
                return "当前构建未签名";
            }
        }

        public static void ValidateUpdatePackage(string path, string expectedVersion)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("更新文件不存在。", path);

            var currentPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            using (var currentSigner = LoadSigner(currentPath))
            using (var candidateSigner = LoadSigner(path))
            {
                if (!string.Equals(
                    currentSigner.Thumbprint,
                    candidateSigner.Thumbprint,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("更新文件发布签名与当前 FACM 不一致。已停止安装。");
                }

                var trustStatus = VerifyAuthenticode(path);
                if (trustStatus != 0)
                {
                    var currentSelfSigned = string.Equals(
                        currentSigner.Subject,
                        currentSigner.Issuer,
                        StringComparison.OrdinalIgnoreCase);
                    var expectedSelfSignedTrustWarning = currentSelfSigned &&
                                                         (trustStatus == CertEUntrustedRoot ||
                                                          trustStatus == CertEChaining);
                    if (!expectedSelfSignedTrustWarning)
                    {
                        throw new InvalidDataException(
                            "更新文件 Authenticode 完整性校验失败（0x" +
                            trustStatus.ToString("X8") + "）。已停止安装。");
                    }
                }
            }

            Version expected;
            if (!TryParseVersion(expectedVersion, out expected))
                throw new InvalidDataException("更新清单版本号无效。");

            var versionInfo = FileVersionInfo.GetVersionInfo(path);
            Version actual;
            if (!TryParseVersion(versionInfo.FileVersion, out actual) || !SameReleaseVersion(expected, actual))
            {
                throw new InvalidDataException(
                    "更新文件版本与清单不一致。期望 " + expectedVersion +
                    "，实际 " + (versionInfo.FileVersion ?? "未知") + "。");
            }
        }

        private static X509Certificate2 LoadSigner(string path)
        {
            try
            {
                using (var certificate = X509Certificate.CreateFromSignedFile(path))
                {
                    return new X509Certificate2(certificate);
                }
            }
            catch (Exception exception)
            {
                throw new InvalidDataException("未找到有效的 FACM 发布签名。", exception);
            }
        }

        private static int VerifyAuthenticode(string path)
        {
            var pathPointer = IntPtr.Zero;
            var fileInfoPointer = IntPtr.Zero;
            try
            {
                pathPointer = Marshal.StringToCoTaskMemUni(Path.GetFullPath(path));
                var fileInfo = new WinTrustFileInfo
                {
                    StructSize = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo)),
                    FilePath = pathPointer,
                    FileHandle = IntPtr.Zero,
                    KnownSubject = IntPtr.Zero
                };

                fileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(WinTrustFileInfo)));
                Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);

                var trustData = new WinTrustData
                {
                    StructSize = (uint)Marshal.SizeOf(typeof(WinTrustData)),
                    PolicyCallbackData = IntPtr.Zero,
                    SipClientData = IntPtr.Zero,
                    UiChoice = WtdUiNone,
                    RevocationChecks = WtdRevokeNone,
                    UnionChoice = WtdChoiceFile,
                    FileInfo = fileInfoPointer,
                    StateAction = WtdStateActionIgnore,
                    StateData = IntPtr.Zero,
                    UrlReference = IntPtr.Zero,
                    ProviderFlags = WtdRevocationCheckNone | WtdCacheOnlyUrlRetrieval,
                    UiContext = 0
                };

                var action = WinTrustActionGenericVerifyV2;
                return WinVerifyTrust(IntPtr.Zero, ref action, ref trustData);
            }
            finally
            {
                if (fileInfoPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(fileInfoPointer);
                if (pathPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pathPointer);
            }
        }

        private static bool TryParseVersion(string value, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var normalized = value.Trim();
            var separator = normalized.IndexOfAny(new[] { ' ', '+', '-' });
            if (separator > 0) normalized = normalized.Substring(0, separator);
            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(1);
            return Version.TryParse(normalized, out version);
        }

        private static bool SameReleaseVersion(Version expected, Version actual)
        {
            return expected.Major == actual.Major &&
                   expected.Minor == actual.Minor &&
                   expected.Build == actual.Build;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, PreserveSig = true)]
        private static extern int WinVerifyTrust(
            IntPtr windowHandle,
            [In] ref Guid actionId,
            [In] ref WinTrustData trustData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint StructSize;
            public IntPtr FilePath;
            public IntPtr FileHandle;
            public IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustData
        {
            public uint StructSize;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInfo;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProviderFlags;
            public uint UiContext;
        }
    }
}
