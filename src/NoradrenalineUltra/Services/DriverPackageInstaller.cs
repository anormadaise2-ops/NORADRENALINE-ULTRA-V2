using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace NoradrenalineUltra.Services;

public static class DriverPackageInstaller
{
    private const int MaxPath = 260;

    public static DriverSignatureResult VerifyInfSignature(string infPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(infPath);
        var fullPath = Path.GetFullPath(infPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The selected driver package file was not found.", fullPath);
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".inf", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Select a Windows .inf driver package file.", nameof(infPath));
        }

        var signerInfo = new InfSignerInfo
        {
            Size = (uint)Marshal.SizeOf<InfSignerInfo>(),
            CatalogFile = string.Empty,
            DigitalSigner = string.Empty,
            DigitalSignerVersion = string.Empty
        };

        if (!SetupVerifyInfFile(fullPath, IntPtr.Zero, ref signerInfo))
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return new DriverSignatureResult(false, string.Empty, string.Empty, error);
        }

        return new DriverSignatureResult(
            true,
            signerInfo.CatalogFile.TrimEnd('\0'),
            signerInfo.DigitalSigner.TrimEnd('\0'),
            string.Empty);
    }

    public static async Task<string> InstallVerifiedDriverAsync(string infPath, CancellationToken cancellationToken = default)
    {
        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new UnauthorizedAccessException("Administrator permission is required to install a driver.");
        }

        var verification = VerifyInfSignature(infPath);
        if (!verification.IsTrusted)
        {
            throw new InvalidOperationException(
                $"Windows could not verify the INF package signature. Installation was blocked. {verification.Error}");
        }

        var pnputilPath = Path.Combine(Environment.SystemDirectory, "pnputil.exe");
        if (!File.Exists(pnputilPath))
        {
            throw new FileNotFoundException("The Windows PnPUtil tool was not found.", pnputilPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = pnputilPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/add-driver");
        startInfo.ArgumentList.Add(Path.GetFullPath(infPath));
        startInfo.ArgumentList.Add("/install");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Windows PnPUtil could not be started.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;

        var details = string.Join(
            Environment.NewLine,
            new[] { output.Trim(), error.Trim() }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(details)
                    ? $"PnPUtil failed with exit code {process.ExitCode}."
                    : $"PnPUtil failed with exit code {process.ExitCode}:{Environment.NewLine}{details}");
        }

        return string.IsNullOrWhiteSpace(details)
            ? "PnPUtil accepted the signed package."
            : details;
    }

    [DllImport("setupapi.dll", EntryPoint = "SetupVerifyInfFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupVerifyInfFile(
        string infName,
        IntPtr alternatePlatformInfo,
        ref InfSignerInfo signerInfo);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct InfSignerInfo
    {
        public uint Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)]
        public string CatalogFile;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)]
        public string DigitalSigner;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)]
        public string DigitalSignerVersion;
    }
}

public sealed record DriverSignatureResult(
    bool IsTrusted,
    string CatalogFile,
    string Signer,
    string Error);
