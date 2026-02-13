using PuushShare.Client.Core.Models;
using PuushShare.Client.Core.Services;

namespace PuushShare.Client.Tests;

public sealed class JsonClientSettingsStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "PuushShare.Client.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var settingsPath = Path.Combine(root, "settings.json");
        var store = new JsonClientSettingsStore(settingsPath);

        try
        {
            var input = new ClientSettings
            {
                ServerBaseUrl = "http://localhost:5088",
                DefaultExpiryMinutes = 10,
                MinExpiryMinutes = 1,
                MaxExpiryMinutes = 60
            };

            await store.SaveAsync(input, CancellationToken.None);
            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.Equal("http://localhost:5088", loaded.ServerBaseUrl);
            Assert.Equal(10, loaded.DefaultExpiryMinutes);
            Assert.Equal(1, loaded.MinExpiryMinutes);
            Assert.Equal(60, loaded.MaxExpiryMinutes);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Load_NormalizesInvalidUrl()
    {
        var root = Path.Combine(Path.GetTempPath(), "PuushShare.Client.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var settingsPath = Path.Combine(root, "settings.json");
        var store = new JsonClientSettingsStore(settingsPath);

        try
        {
            await File.WriteAllTextAsync(
                settingsPath,
                """
                {
                  "serverBaseUrl": "invalid-url",
                  "defaultExpiryMinutes": 999,
                  "minExpiryMinutes": 1,
                  "maxExpiryMinutes": 60
                }
                """);

            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.Equal(ClientSettings.DefaultServerBaseUrl, loaded.ServerBaseUrl);
            Assert.Equal(60, loaded.DefaultExpiryMinutes);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
