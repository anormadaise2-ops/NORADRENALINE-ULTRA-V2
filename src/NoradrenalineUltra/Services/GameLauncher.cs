using System.Diagnostics;
using System.IO;
using System.ComponentModel;
using NoradrenalineUltra.Models;

namespace NoradrenalineUltra.Services;

public static class GameLauncher
{
    public static GameLaunchResult Launch(GameEntry game, bool aboveNormalPriority)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (game.ExecutablePath is not null)
        {
            if (!File.Exists(game.ExecutablePath) ||
                !string.Equals(Path.GetExtension(game.ExecutablePath), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new FileNotFoundException("The game's executable is missing or is not a Windows executable.", game.ExecutablePath);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = Path.GetFullPath(game.ExecutablePath),
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(game.ExecutablePath))
                    ?? throw new InvalidOperationException("The game working directory could not be determined."),
                UseShellExecute = false
            };
            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Windows could not start the selected game.");

            if (aboveNormalPriority)
            {
                try
                {
                    process.PriorityClass = ProcessPriorityClass.AboveNormal;
                    return new GameLaunchResult(process, PriorityApplied: true, IsPlatformLauncher: false);
                }
                catch (Exception exception) when (
                    exception is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
                {
                    return new GameLaunchResult(process, PriorityApplied: false, IsPlatformLauncher: false);
                }
            }

            return new GameLaunchResult(process, PriorityApplied: false, IsPlatformLauncher: false);
        }

        if (aboveNormalPriority)
        {
            throw new InvalidOperationException(
                "A per-game process-priority profile requires a direct executable. A platform launcher does not expose the game process to this launcher.");
        }

        string launchUri;
        if (string.Equals(game.Platform, "Steam", StringComparison.Ordinal) &&
            uint.TryParse(game.LaunchTarget, out var appId))
        {
            launchUri = $"steam://rungameid/{appId}";
        }
        else if (string.Equals(game.Platform, "Epic Games", StringComparison.Ordinal) &&
                 !string.IsNullOrWhiteSpace(game.LaunchTarget))
        {
            launchUri = $"com.epicgames.launcher://apps/{Uri.EscapeDataString(game.LaunchTarget)}?action=launch&silent=true";
        }
        else
        {
            throw new InvalidOperationException("This library entry does not have a supported launch target.");
        }

        var launcherProcess = Process.Start(new ProcessStartInfo
        {
            FileName = launchUri,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("Windows could not open the Steam game link.");
        return new GameLaunchResult(launcherProcess, PriorityApplied: false, IsPlatformLauncher: true);
    }
}

public sealed record GameLaunchResult(
    Process Process,
    bool PriorityApplied,
    bool IsPlatformLauncher);
