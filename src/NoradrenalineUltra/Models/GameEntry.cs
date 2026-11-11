namespace NoradrenalineUltra.Models;

public sealed record GameEntry(
    string Id,
    string Name,
    string Platform,
    string InstallPath,
    string? ExecutablePath,
    string LaunchTarget)
{
    public string LaunchMode => ExecutablePath is null ? "Platform launcher" : "Direct launch";
}

public sealed record GameLibraryScanResult(
    IReadOnlyList<GameEntry> Games,
    IReadOnlyList<string> Warnings);
