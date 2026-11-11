using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using NoradrenalineUltra.Models;

namespace NoradrenalineUltra.Services;

public sealed class NoraX1ProbeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<GraphicsAdapter>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var executablePath = Path.Combine(AppContext.BaseDirectory, "nora-x1-probe.exe");
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "The NORA X1 user-mode probe is not beside the application. Build NORA-X1-DRIVER with BUILD-DRIVERS.ps1 first.",
                executablePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        startInfo.ArgumentList.Add("--json");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("The NORA X1 user-mode probe could not be started.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? $"The NORA X1 probe failed with exit code {process.ExitCode}."
                    : $"The NORA X1 probe failed: {error.Trim()}");
        }

        var response = JsonSerializer.Deserialize<ProbeResponse>(output, JsonOptions)
            ?? throw new InvalidDataException("The NORA X1 probe returned invalid JSON.");
        if (response.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported NORA X1 diagnostics schema version: {response.SchemaVersion}.");
        }

        if (!response.DxgiEnumerationAvailable)
        {
            throw new InvalidOperationException("DXGI could not enumerate display adapters.");
        }

        return response.Adapters;
    }

    private sealed record ProbeResponse(
        int SchemaVersion,
        bool DxgiEnumerationAvailable,
        IReadOnlyList<GraphicsAdapter> Adapters);
}
