namespace POS_UPDATER_SYSTEM.Api.Services;

public interface IActivationService
{
    Task<string> BackupCurrentAsync(string currentVersion, ILogger logger, CancellationToken cancellationToken);

    Task ActivateAsync(string stagingPath, ILogger logger, CancellationToken cancellationToken);
}
