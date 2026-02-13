using System.Net.Http;
using System.Windows;
using PuushShare.Client.Core.Abstractions;
using PuushShare.Client.Core.Models;
using PuushShare.Client.Core.Services;
using PuushShare.Client.Services;

namespace PuushShare.Client;

public partial class App : System.Windows.Application
{
    private SingleInstanceGuard? _singleInstanceGuard;
    private IClientLogger? _logger;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!SingleInstanceGuard.TryAcquire(@"Global\PuushShare.Client", out var guard))
        {
            System.Windows.MessageBox.Show(
                "Osz Share가 이미 실행 중입니다.",
                "Osz Share",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);

            Shutdown(0);
            return;
        }

        _singleInstanceGuard = guard;
        _logger = new FileClientLogger();

        try
        {
            _logger.Info("app_start", "GUI client startup initiated.");

            IClientSettingsStore settingsStore = new JsonClientSettingsStore();
            ClientSettings settings = await settingsStore.LoadAsync(CancellationToken.None);

            using var resolverClient = new HttpClient();
            var resolver = new ServerEndpointResolver(resolverClient);
            var resolvedEndpoint = await resolver.ResolveAsync(settings.ServerBaseUrl, CancellationToken.None);
            if (resolvedEndpoint.IsSuccess
                && !string.Equals(settings.ServerBaseUrl, resolvedEndpoint.BaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                settings.ServerBaseUrl = resolvedEndpoint.BaseUrl;
                await settingsStore.SaveAsync(settings, CancellationToken.None);
                _logger.Info("server_autodetect", $"Detected reachable server endpoint: {resolvedEndpoint.BaseUrl}");
            }
            else if (!resolvedEndpoint.IsSuccess)
            {
                _logger.Warn("server_autodetect_failed", $"No healthy endpoint found. Keeping configured URL: {settings.ServerBaseUrl}");
            }

            var activeBaseUrl = resolvedEndpoint.IsSuccess ? resolvedEndpoint.BaseUrl : settings.ServerBaseUrl;
            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(activeBaseUrl)
            };

            var beatmapProvider = new OsuStableBeatmapProvider();
            var packager = new OszPackager();
            var shareApiClient = new ShareApiClient(httpClient);

            var window = new MainWindow(settings, beatmapProvider, packager, shareApiClient, _logger);
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            _logger?.Error("app_start_failed", "Client failed to initialize.", exception);
            System.Windows.MessageBox.Show(
                $"Osz Share failed to start:\n{exception.Message}",
                "Osz Share",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Info("app_exit", "GUI client shutdown complete.");
        _singleInstanceGuard?.Dispose();
        base.OnExit(e);
    }
}

