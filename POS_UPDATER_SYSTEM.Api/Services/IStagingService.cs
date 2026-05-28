namespace POS_UPDATER_SYSTEM.Api.Services;

public interface IStagingService
{
    Task<string> StageAsync(string packagePath, ILogger logger, CancellationToken cancellationToken);

    Task ValidateAsync(string stagingPath, ILogger logger, CancellationToken cancellationToken);

    Task ValidateRuntimeAsync(string stagingPath, ILogger logger, CancellationToken cancellationToken);
}
