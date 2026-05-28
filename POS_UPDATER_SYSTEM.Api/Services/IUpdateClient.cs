using POS_UPDATER_SYSTEM.Api.Models;

namespace POS_UPDATER_SYSTEM.Api.Services;

public interface IUpdateClient
{
    Task<LatestUpdateInfo> GetLatestAsync(CancellationToken cancellationToken);

    // Lightweight check to obtain the latest version string without performing
    // manifest signature verification. Used to determine whether a full manifest
    // download & verification is necessary before proceeding with a deployment.
    Task<string> GetLatestVersionAsync(CancellationToken cancellationToken);

    Task<string> DownloadPackageAsync(LatestUpdateInfo latest, ILogger logger, CancellationToken cancellationToken);
}
