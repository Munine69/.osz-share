using System.Text.Json;
using PuushShare.Client.Core.Abstractions;
using PuushShare.Client.Core.Models;

namespace PuushShare.Client.Core.Services;

public sealed class JsonClientSettingsStore : IClientSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public JsonClientSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PuushShare",
            "settings.json"))
    {
    }

    public JsonClientSettingsStore(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public string SettingsPath => _settingsPath;

    public async Task<ClientSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"Invalid settings path: {_settingsPath}");
        }

        Directory.CreateDirectory(directory);

        if (!File.Exists(_settingsPath))
        {
            var defaults = new ClientSettings().Normalize();
            await SaveAsync(defaults, cancellationToken);
            return defaults;
        }

        await using var source = new FileStream(
            _settingsPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 4096,
            useAsync: true);

        var loaded = await JsonSerializer.DeserializeAsync<ClientSettings>(source, SerializerOptions, cancellationToken)
            ?? new ClientSettings();

        return loaded.Normalize();
    }

    public async Task SaveAsync(ClientSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalized = settings.Normalize();
        var directory = Path.GetDirectoryName(_settingsPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"Invalid settings path: {_settingsPath}");
        }

        Directory.CreateDirectory(directory);

        var tempPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var target = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(target, normalized, SerializerOptions, cancellationToken);
            }

            if (File.Exists(_settingsPath))
            {
                File.Replace(tempPath, _settingsPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, _settingsPath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
