using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using FACM.Core.Online;
using FACM.Core.Runtime;

namespace FACM.Platform.Windows.Runtime;

/// <summary>
/// FACM 3.5.15-compatible release-identity verification. The candidate must carry the same signer
/// certificate as the running FACM, pass Authenticode integrity checks (with the legacy self-signed
/// trust-chain exception), and expose the same major/minor/build version as the manifest.
/// </summary>
public sealed class WindowsUpdatePackageIdentityVerifier : IUpdatePackageIdentityVerifier
{
    private static readonly Guid WinTrustActionGenericVerifyV2 =
        new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionIgnore = 0;
    private const uint WtdRevocationCheckNone = 0x00000010;
    private const uint WtdCacheOnlyUrlRetrieval = 0x00001000;
    internal const int CertEUntrustedRoot = unchecked((int)0x800B0109);
    internal const int CertEChaining = unchecked((int)0x800B010A);

    private readonly IExecutablePathProvider _executablePaths;

    public WindowsUpdatePackageIdentityVerifier(IExecutablePathProvider executablePaths)
    {
        _executablePaths = executablePaths ?? throw new ArgumentNullException(nameof(executablePaths));
    }

    public void Validate(string packagePath, string expectedVersion)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            throw new FileNotFoundException("Update package does not exist.", packagePath);

        var currentPath = Path.GetFullPath(_executablePaths.ExecutablePath);
        var candidatePath = Path.GetFullPath(packagePath);
        using var currentSigner = LoadSigner(currentPath);
        using var candidateSigner = LoadSigner(candidatePath);

        if (!string.Equals(
                currentSigner.GetCertHashString(),
                candidateSigner.GetCertHashString(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Update package signer does not match the running FACM signer.");

        var trustStatus = VerifyAuthenticode(candidatePath);
        if (trustStatus != 0)
        {
            var currentSelfSigned = string.Equals(
                currentSigner.Subject,
                currentSigner.Issuer,
                StringComparison.OrdinalIgnoreCase);
            var expectedSelfSignedWarning = currentSelfSigned &&
                                            (trustStatus == CertEUntrustedRoot || trustStatus == CertEChaining);
            if (!expectedSelfSignedWarning)
                throw new InvalidDataException(
                    "Update package Authenticode verification failed (0x" + trustStatus.ToString("X8") + ").");
        }

        var expected = UpdateDecisionService.ParseVersion(expectedVersion)
            ?? throw new InvalidDataException("Update manifest version is invalid.");
        var actualText = FileVersionInfo.GetVersionInfo(candidatePath).FileVersion;
        var actual = UpdateDecisionService.ParseVersion(actualText ?? string.Empty);
        if (actual is null || !SameReleaseVersion(expected, actual))
            throw new InvalidDataException(
                "Update package version does not match the manifest. Expected " + expectedVersion +
                ", actual " + (actualText ?? "unknown") + ".");
    }

    internal static bool SameReleaseVersion(Version expected, Version actual) =>
        expected.Major == actual.Major &&
        expected.Minor == actual.Minor &&
        expected.Build == actual.Build;

    private static X509Certificate LoadSigner(string path)
    {
        try
        {
            return X509Certificate.CreateFromSignedFile(path);
        }
        catch (Exception exception)
        {
            throw new InvalidDataException("No valid FACM release signer was found.", exception);
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
                StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                FilePath = pathPointer,
                FileHandle = IntPtr.Zero,
                KnownSubject = IntPtr.Zero
            };

            fileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            var trustData = new WinTrustData
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
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
