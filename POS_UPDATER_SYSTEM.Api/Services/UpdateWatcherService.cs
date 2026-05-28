using Microsoft.Extensions.Options;
using POS_UPDATER_SYSTEM.Api.Options;

namespace POS_UPDATER_SYSTEM.Api.Services;

public sealed class UpdateWatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly UpdaterOptions _options;
    private readonly ILogger<UpdateWatcherService> _logger;

    public UpdateWatcherService(
        IServiceScopeFactory scopeFactory,
        IOptions<UpdaterOptions> options,
        ILogger<UpdateWatcherService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.CheckIntervalMinutes));

        _logger.LogInformation("Watcher started with interval {Interval}. Watcher checks only; deployments remain manual.", interval);

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunCycleSafeAsync(stoppingToken);
        }

        _logger.LogInformation("Watcher stopped.");
    }

    private async Task RunCycleSafeAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IDeploymentOrchestrator>();

            _logger.LogInformation("Watcher check cycle started.");

            var result = await orchestrator.CheckForUpdateAsync(ct);

            if (result.IsUpdateAvailable)
            {
                _logger.LogInformation(
                    "Watcher found update. Current={CurrentVersion}, Latest={LatestVersion}. Deployment remains manual.",
                    result.CurrentVersion,
                    result.LatestVersion);
            }
            else
            {
                _logger.LogInformation(
                    "Watcher found no update. Current={CurrentVersion}, Latest={LatestVersion}.",
                    result.CurrentVersion,
                    result.LatestVersion);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Watcher check cycle failed.");
        }
    }
}
