namespace POS_UPDATER_SYSTEM.Api.Services;

public interface ILiveAppValidator
{
    Task ValidateAsync(ILogger logger, CancellationToken cancellationToken);
}
