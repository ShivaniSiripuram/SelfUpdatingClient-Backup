namespace POS_UPDATER_SYSTEM.Api.Services;

public interface IHostRuntimeManager
{
    Task StopLiveAppAsync(ILogger logger, CancellationToken cancellationToken);

    Task RestartLiveAppAsync(ILogger logger, CancellationToken cancellationToken);
}
