using System.Text.Json;

namespace Tokenmon.Core;

public sealed class JsonStateStore(string directory)
{
    public string DirectoryPath { get; } = directory;
    public string CompanionPath => Path.Combine(DirectoryPath, "companion.json");
    public string SettingsPath => Path.Combine(DirectoryPath, "settings.json");

    public CompanionState LoadCompanion() => Load(CompanionPath, new CompanionState());

    public AppSettings LoadSettings() => Load(SettingsPath, new AppSettings());

    public Task SaveCompanion(CompanionState state, CancellationToken cancellationToken = default) =>
        Save(CompanionPath, state, cancellationToken);

    public Task SaveSettings(AppSettings settings, CancellationToken cancellationToken = default) =>
        Save(SettingsPath, settings, cancellationToken);

    private static T Load<T>(string path, T fallback)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? fallback
                : fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private async Task Save<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(DirectoryPath);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
    }

    private static JsonSerializerOptions JsonOptions => new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}
