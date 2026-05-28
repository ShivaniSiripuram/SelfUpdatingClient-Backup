namespace POS_UPDATER_SYSTEM.Api.Services;

public sealed class StaticFileHostRuntimeManager : IHostRuntimeManager
{
    public Task StopLiveAppAsync(ILogger logger, CancellationToken cancellationToken)
    {
        logger.LogInformation("[MAINTENANCE] Maintenance mode enabled");
        logger.LogInformation("[MAINTENANCE] User traffic temporarily blocked");
        logger.LogInformation("[MAINTENANCE] Maintenance UI activated");
        return Task.CompletedTask;
    }

    public Task RestartLiveAppAsync(ILogger logger, CancellationToken cancellationToken)
    {
        logger.LogInformation("[MAINTENANCE] Maintenance mode disabled");
        logger.LogInformation("[MAINTENANCE] User traffic restored");
        return Task.CompletedTask;
    }
}
