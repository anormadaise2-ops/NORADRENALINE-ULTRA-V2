using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using NoradrenalineUltra.Models;

namespace NoradrenalineUltra.Services;

public interface IGameLibraryService
{
    Task<GameLibraryScanResult> ScanInstalledGamesAsync(CancellationToken cancellationToken = default);
}

public sealed class GameLibraryService : IGameLibraryService
{
    private static readonly Regex SteamLibraryPathPattern = new(
        "\"path\"\\s*\"((?:\\\\\\\\.|[^\"])*)\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SteamManifestValuePattern = new(
        "\"(?<key>name|installdir)\"\\s*\"(?<value>(?:\\\\.|[^\"])*)\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public Task<GameLibraryScanResult> ScanInstalledGamesAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => ScanInstalledGames(cancellationToken), cancellationToken);

    private static GameLibraryScanResult ScanInstalledGames(CancellationToken cancellationToken)
    {
        var games = new Dictionary<string, GameEntry>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        ScanSteam(games, warnings, cancellationToken);
        ScanEpic(games, warnings, cancellationToken);

        return new GameLibraryScanResult(
            games.Values.OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            warnings);
    }

    private static void ScanSteam(
        IDictionary<string, GameEntry> games,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        foreach (var installPath in GetSteamInstallPaths())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var steamAppsPath = Path.Combine(installPath, "steamapps");
            if (!Directory.Exists(steamAppsPath))
            {
                continue;
            }

            var libraryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamAppsPath };
            var libraryConfigPath = Path.Combine(steamAppsPath, "libraryfolders.vdf");
            if (File.Exists(libraryConfigPath))
            {
                try
                {
                    var contents = File.ReadAllText(libraryConfigPath);
                    foreach (Match match in SteamLibraryPathPattern.Matches(contents))
                    {
                        var libraryRoot = UnescapeVdf(match.Groups[1].Value);
                        if (!string.IsNullOrWhiteSpace(libraryRoot))
                        {
                            libraryPaths.Add(Path.Combine(libraryRoot, "steamapps"));
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"Could not read the Steam library list at {libraryConfigPath}: {exception.Message}");
                }
            }

            foreach (var libraryPath in libraryPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(libraryPath))
                {
                    continue;
                }

                try
                {
                    foreach (var manifestPath in Directory.EnumerateFiles(libraryPath, "appmanifest_*.acf"))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            AddSteamManifest(games, manifestPath, libraryPath);
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
                        {
                            warnings.Add($"Could not read Steam manifest {Path.GetFileName(manifestPath)}: {exception.Message}");
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"Could not scan Steam library {libraryPath}: {exception.Message}");
                }
            }
        }
    }

    private static void AddSteamManifest(IDictionary<string, GameEntry> games, string manifestPath, string steamAppsPath)
    {
        var contents = File.ReadAllText(manifestPath);
        var values = SteamManifestValuePattern.Matches(contents)
            .Cast<Match>()
            .GroupBy(match => match.Groups["key"].Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => UnescapeVdf(group.Last().Groups["value"].Value),
                StringComparer.OrdinalIgnoreCase);

        var appId = Path.GetFileNameWithoutExtension(manifestPath)["appmanifest_".Length..];
        if (!uint.TryParse(appId, out _) ||
            !values.TryGetValue("name", out var name) ||
            !values.TryGetValue("installdir", out var installDirectory) ||
            string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(installDirectory) ||
            name.Contains("Steamworks Common Redistributables", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var commonDirectory = Path.GetFullPath(Path.Combine(steamAppsPath, "common"));
        var installPath = Path.GetFullPath(Path.Combine(commonDirectory, installDirectory));
        var commonDirectoryPrefix = Path.TrimEndingDirectorySeparator(commonDirectory) + Path.DirectorySeparatorChar;
        if (!installPath.StartsWith(commonDirectoryPrefix, StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(installPath))
        {
            return;
        }

        games[$"steam:{appId}"] = new GameEntry(
            $"steam:{appId}",
            name,
            "Steam",
            installPath,
            null,
            appId);
    }

    private static void ScanEpic(
        IDictionary<string, GameEntry> games,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        var manifestDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic",
            "EpicGamesLauncher",
            "Data",
            "Manifests");
        if (!Directory.Exists(manifestDirectory))
        {
            return;
        }

        IEnumerable<string> manifests;
        try
        {
            manifests = Directory.EnumerateFiles(manifestDirectory, "*.item").ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Could not scan Epic manifests: {exception.Message}");
            return;
        }

        foreach (var manifestPath in manifests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var root = document.RootElement;
                var appName = GetString(root, "AppName");
                var displayName = GetString(root, "DisplayName");
                var installLocation = GetString(root, "InstallLocation");
                if (string.IsNullOrWhiteSpace(appName) ||
                    string.IsNullOrWhiteSpace(displayName) ||
                    string.IsNullOrWhiteSpace(installLocation) ||
                    !Directory.Exists(installLocation))
                {
                    continue;
                }

                var fullInstallPath = Path.GetFullPath(installLocation);
                games[$"epic:{appName}"] = new GameEntry(
                    $"epic:{appName}",
                    displayName,
                    "Epic Games",
                    fullInstallPath,
                    null,
                    appName);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                warnings.Add($"Could not read Epic manifest {Path.GetFileName(manifestPath)}: {exception.Message}");
            }
        }
    }

    private static IEnumerable<string> GetSteamInstallPaths()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, keyPath) in new[]
                 {
                     (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam"),
                     (Registry.LocalMachine, @"SOFTWARE\Valve\Steam"),
                     (Registry.CurrentUser, @"Software\Valve\Steam")
                 })
        {
            using var key = hive.OpenSubKey(keyPath);
            var path = key?.GetValue("InstallPath") as string ??
                       key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                candidates.Add(Path.GetFullPath(path));
            }
        }

        foreach (var path in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
                 })
        {
            if (Directory.Exists(path))
            {
                candidates.Add(Path.GetFullPath(path));
            }
        }

        return candidates;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string UnescapeVdf(string value) =>
        Regex.Replace(value, @"\\(.)", "$1", RegexOptions.CultureInvariant);
}
