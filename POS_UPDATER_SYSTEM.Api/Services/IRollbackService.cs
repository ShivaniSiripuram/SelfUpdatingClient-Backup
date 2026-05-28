namespace POS_UPDATER_SYSTEM.Api.Services;

public interface IRollbackService
{
    Task<RollbackOutcome> RollbackLatestAsync(ILogger logger, CancellationToken cancellationToken);
}

public sealed record RollbackOutcome(string BackupPath, string Version);
