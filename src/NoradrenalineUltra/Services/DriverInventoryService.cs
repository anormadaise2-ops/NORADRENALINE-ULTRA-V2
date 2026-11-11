using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using NoradrenalineUltra.Models;

namespace NoradrenalineUltra.Services;

public interface IDriverInventoryService
{
    Task<IReadOnlyList<DriverDevice>> ScanAsync(CancellationToken cancellationToken = default);
}

public sealed class DriverInventoryService : IDriverInventoryService
{
    private const string InventoryScript = """
        $ErrorActionPreference = 'Stop'
        $OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        $entities = @(Get-CimInstance -ClassName Win32_PnPEntity)
        $drivers = @{}
        Get-CimInstance -ClassName Win32_PnPSignedDriver | ForEach-Object {
            if ($_.DeviceID) {
                $drivers[[string]$_.DeviceID] = $_
            }
        }
        $devices = @($entities | ForEach-Object {
            $driver = $drivers[[string]$_.PNPDeviceID]
            $date = ''
            if ($driver -and $driver.DriverDate) {
                if ($driver.DriverDate -is [DateTime]) {
                    $date = $driver.DriverDate.ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
                }
                else {
                    try {
                        $date = [DateTime]::ParseExact(
                            ([string]$driver.DriverDate).Substring(0, 14),
                            'yyyyMMddHHmmss',
                            [Globalization.CultureInfo]::InvariantCulture
                        ).ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
                    }
                    catch {
                        $date = [string]$driver.DriverDate
                    }
                }
            }
            $problemCode = $null
            if ($null -ne $_.ConfigManagerErrorCode) {
                $problemCode = [int]$_.ConfigManagerErrorCode
            }
            [pscustomobject]@{
                DeviceName = if ($driver -and $driver.DeviceName) { [string]$driver.DeviceName } else { [string]$_.Name }
                Manufacturer = if ($driver -and $driver.Manufacturer) { [string]$driver.Manufacturer } else { [string]$_.Manufacturer }
                DeviceId = [string]$_.PNPDeviceID
                DriverVersion = if ($driver) { [string]$driver.DriverVersion } else { '' }
                DriverDate = [string]$date
                InfName = if ($driver) { [string]$driver.InfName } else { '' }
                HasDriverRecord = $null -ne $driver
                IsSigned = if ($null -eq $driver -or $null -eq $driver.IsSigned) { $null } else { [bool]$driver.IsSigned }
                ProblemCode = $problemCode
            }
        })
        ConvertTo-Json -InputObject $devices -Depth 3 -Compress
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<DriverDevice>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var powershellPath = Path.Combine(
            Environment.SystemDirectory,
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        if (!File.Exists(powershellPath))
        {
            throw new FileNotFoundException("Windows PowerShell was not found.", powershellPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = powershellPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("-");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Windows PowerShell could not be started.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.StandardInput.WriteAsync(InventoryScript.AsMemory(), cancellationToken);
        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? $"Windows device inventory failed with exit code {process.ExitCode}."
                    : $"Windows device inventory failed: {error.Trim()}");
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<DriverDevice>>(output, JsonOptions)
            ?? throw new InvalidOperationException("Windows returned an invalid device inventory response.");
    }
}
