using System.IO;
using System.Text.Json;
using NoradrenalineUltra.Models;

namespace NoradrenalineUltra.Services;

public sealed class GameLibraryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _libraryFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NORADRENALINE TECHNOLOGIES",
        "NORADRENALINE ULTRA",
        "games.json");

    public async Task<IReadOnlyList<GameEntry>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_libraryFilePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_libraryFilePath);
        return await JsonSerializer.DeserializeAsync<List<GameEntry>>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The saved game library contains no valid game list.");
    }

    public async Task SaveAsync(IReadOnlyCollection<GameEntry> games, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(games);
        var directory = Path.GetDirectoryName(_libraryFilePath)
            ?? throw new InvalidOperationException("The local game library directory could not be determined.");
        Directory.CreateDirectory(directory);

        var temporaryPath = _libraryFilePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, games, JsonOptions, cancellationToken);
            }

            File.Move(temporaryPath, _libraryFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
