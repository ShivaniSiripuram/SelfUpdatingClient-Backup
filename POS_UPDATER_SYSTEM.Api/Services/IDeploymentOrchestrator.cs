using POS_UPDATER_SYSTEM.Api.Models;

namespace POS_UPDATER_SYSTEM.Api.Services;

public interface IDeploymentOrchestrator
{
    Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken);

    Task<DeploymentResult> RunDeploymentIfAvailableAsync(CancellationToken cancellationToken);

    Task<DeploymentResult> RunDeploymentAsync(LatestUpdateInfo latest, CancellationToken cancellationToken);

    Task<DeploymentResult> RollbackToPreviousVersionAsync(CancellationToken cancellationToken);
}
