using System.IO;
using System.Text.Json;
using NoradrenalineUltra.Models;

namespace NoradrenalineUltra.Services;

public static class DriverReportExporter
{
    public static async Task ExportJsonAsync(
        string destinationPath,
        IReadOnlyCollection<DriverDevice> devices,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(devices);

        var report = new DriverReport(
            DateTimeOffset.Now,
            Environment.OSVersion.VersionString,
            devices);
        var json = JsonSerializer.Serialize(
            report,
            new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(destinationPath, json, cancellationToken);
    }

    private sealed record DriverReport(
        DateTimeOffset GeneratedAt,
        string OperatingSystem,
        IReadOnlyCollection<DriverDevice> Devices);
}
