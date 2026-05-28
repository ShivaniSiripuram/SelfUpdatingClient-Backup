using POS_UPDATER_SYSTEM.Api.Models;

namespace POS_UPDATER_SYSTEM.Api.Services;

public sealed class DeploymentOrchestrator : IDeploymentOrchestrator
{
    private static readonly SemaphoreSlim OperationLock = new(1, 1);

    private readonly IUpdateClient _updateClient;
    private readonly IStagingService _stagingService;
    private readonly IPackageVerifier _verifier;
    private readonly IActivationService _activationService;
    private readonly IHostRuntimeManager _hostRuntimeManager;
    private readonly IRollbackService _rollbackService;
    private readonly IDeploymentStateStore _stateStore;
    private readonly ILogger<DeploymentOrchestrator> _logger;
    private readonly StoragePaths _paths;

    public DeploymentOrchestrator(
        IUpdateClient updateClient,
        IStagingService stagingService,
        IPackageVerifier verifier,
        IActivationService activationService,
        IHostRuntimeManager hostRuntimeManager,
        IRollbackService rollbackService,
        IDeploymentStateStore stateStore,
        ILogger<DeploymentOrchestrator> logger,
        StoragePaths paths)
    {
        _updateClient = updateClient;
        _stagingService = stagingService;
        _verifier = verifier;
        _activationService = activationService;
        _hostRuntimeManager = hostRuntimeManager;
        _rollbackService = rollbackService;
        _stateStore = stateStore;
        _logger = logger;
        _paths = paths;
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken)
    {
        if (!await OperationLock.WaitAsync(0, cancellationToken))
        {
            var state = await _stateStore.GetAsync(cancellationToken);
            _logger.LogWarning("Update check skipped because another deployment operation is already running.");
            return new UpdateCheckResult
            {
                CurrentVersion = state.CurrentVersion,
                LatestVersion = state.CurrentVersion,
                IsUpdateAvailable = false,
                Latest = null,
                LogFile = state.LastOperationLogFile
            };
        }

        try
        {
            await SetStateAsync(DeploymentStatus.CHECKING, null, cancellationToken);

            var latest = await _updateClient.GetLatestAsync(cancellationToken);
            var stateSnapshot = await _stateStore.GetAsync(cancellationToken);
            var currentVersion = stateSnapshot.CurrentVersion;
            var rejectedVersion = stateSnapshot.RejectedVersion ?? stateSnapshot.CorruptedVersion;
            var rejectedReason = stateSnapshot.RejectedReason ?? stateSnapshot.CorruptedReason;
            if (IsNewerVersion(latest.Version, rejectedVersion))
            {
                await _stateStore.UpdateAsync(state =>
                {
                    state.RejectedVersion = null;
                    state.RejectedReason = null;
                    state.RejectedVersionAt = null;
                    state.CorruptedVersion = null;
                    state.CorruptedReason = null;
                    state.CorruptedVersionAt = null;
                    return state;
                }, cancellationToken);

                rejectedVersion = null;
                rejectedReason = null;
            }

            var isBlockedVersion = IsSameVersion(latest.Version, stateSnapshot.BlockedVersion);
            var isRejectedVersion = IsSameVersion(latest.Version, rejectedVersion);
            var isSuppressedVersion = isBlockedVersion || isRejectedVersion;
            var isUpdateAvailable = !isSuppressedVersion && !IsSameVersion(currentVersion, latest.Version);

            _logger.LogInformation(
                "Update check completed. Current={CurrentVersion}, Latest={LatestVersion}, UpdateAvailable={IsUpdateAvailable}, Blocked={IsBlockedVersion}, Rejected={IsRejectedVersion}.",
                currentVersion,
                latest.Version,
                isUpdateAvailable,
                isBlockedVersion,
                isRejectedVersion);

            await _stateStore.UpdateAsync(state =>
            {
                state.Status = DeploymentStatus.LIVE;
                state.IsUpdating = false;
                state.LastError = null;
                state.LastCheckTime = DateTimeOffset.UtcNow;
                return state;
            }, cancellationToken);

            return new UpdateCheckResult
            {
                CurrentVersion = currentVersion,
                LatestVersion = isSuppressedVersion ? currentVersion : latest.Version,
                IsUpdateAvailable = isUpdateAvailable,
                Latest = isSuppressedVersion ? null : latest,
                LogFile = stateSnapshot.LastOperationLogFile,
                RejectedVersion = rejectedVersion,
                RejectedReason = rejectedReason,
                IsLatestRejected = isRejectedVersion,
                CorruptedVersion = rejectedVersion,
                CorruptedReason = rejectedReason,
                IsLatestCorrupted = isRejectedVersion,
                RemoteLatestVersion = latest.Version,
                IsLatestSuppressed = isSuppressedVersion
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update check failed.");
            await MarkFailedAsync("Update check failed.", ex, null, cancellationToken);
            throw;
        }
        finally
        {
            OperationLock.Release();
        }
    }

    public async Task<DeploymentResult> RunDeploymentIfAvailableAsync(CancellationToken cancellationToken)
    {
        var check = await CheckForUpdateAsync(cancellationToken);

        if (!check.IsUpdateAvailable || check.Latest == null)
        {
            return new DeploymentResult
            {
                Succeeded = false,
                Message = "No updates available",
                LogFile = check.LogFile
            };
        }

        return await RunDeploymentAsync(check.Latest, cancellationToken);
    }

    public async Task<DeploymentResult> RunDeploymentAsync(LatestUpdateInfo latest, CancellationToken cancellationToken)
    {
        if (!await OperationLock.WaitAsync(0, cancellationToken))
        {
            return new DeploymentResult
            {
                Succeeded = false,
                Version = latest.Version,
                Message = "Another deployment operation is already running."
            };
        }

        var logPath = CreateOperationLogPath("deployment", latest.Version);
        using var scope = BeginOperationLogScope(logPath);

        string? backupPath = null;

        try
        {
            var startingState = await _stateStore.GetAsync(cancellationToken);
            var rejectedVersion = startingState.RejectedVersion ?? startingState.CorruptedVersion;
            if (IsSameVersion(latest.Version, startingState.BlockedVersion)
                || IsSameVersion(latest.Version, rejectedVersion))
            {
                _logger.LogWarning("Deployment refused because version {Version} is suppressed. Blocked={BlockedVersion}, Rejected={RejectedVersion}.", latest.Version, startingState.BlockedVersion, rejectedVersion);
                return new DeploymentResult
                {
                    Succeeded = false,
                    Version = latest.Version,
                    Message = "No updates available",
                    LogFile = Path.GetFileName(logPath)
                };
            }

            _logger.LogInformation("[WATCHER] New version detected. Deployment pipeline triggered.");
            _logger.LogInformation("[PIPELINE] Target version: {Version}", latest.Version);

            await SetStateAsync(DeploymentStatus.DOWNLOADING, Path.GetFileName(logPath), cancellationToken);
            var packagePath = await _updateClient.DownloadPackageAsync(latest, _logger, cancellationToken);

            await SetStateAsync(DeploymentStatus.VERIFYING, Path.GetFileName(logPath), cancellationToken);
            await _verifier.VerifyAsync(packagePath, latest.Sha256, _logger, cancellationToken);

            await SetStateAsync(DeploymentStatus.STAGING, Path.GetFileName(logPath), cancellationToken);
            var stagingPath = await _stagingService.StageAsync(packagePath, _logger, cancellationToken);

            await SetStateAsync(DeploymentStatus.VALIDATING_STATIC, Path.GetFileName(logPath), cancellationToken);
            await _stagingService.ValidateAsync(stagingPath, _logger, cancellationToken);

            await SetStateAsync(DeploymentStatus.VALIDATING_RUNTIME, Path.GetFileName(logPath), cancellationToken);
            await _stagingService.ValidateRuntimeAsync(stagingPath, _logger, cancellationToken);

            await SetStateAsync(DeploymentStatus.RESTARTING, Path.GetFileName(logPath), cancellationToken);
            await _hostRuntimeManager.StopLiveAppAsync(_logger, cancellationToken);

            await SetStateAsync(DeploymentStatus.BACKING_UP, Path.GetFileName(logPath), cancellationToken);
            var currentVersion = await _stateStore.GetCurrentVersionAsync(cancellationToken);
            backupPath = await _activationService.BackupCurrentAsync(currentVersion, _logger, cancellationToken);
            CleanupOldBackupsExcept(backupPath);

            await _stateStore.SaveLastBackupAsync(backupPath, cancellationToken);

            await SetStateAsync(DeploymentStatus.ACTIVATING, Path.GetFileName(logPath), cancellationToken);
            await _activationService.ActivateAsync(stagingPath, _logger, cancellationToken);

            await SetStateAsync(DeploymentStatus.RESTARTING, Path.GetFileName(logPath), cancellationToken);
            await _hostRuntimeManager.RestartLiveAppAsync(_logger, cancellationToken);

            await _stateStore.UpdateAsync(state =>
            {
                state.CurrentVersion = latest.Version;
                state.LastKnownGoodVersion = latest.Version;
                state.LastBackupPath = backupPath;
                state.Status = DeploymentStatus.LIVE;
                state.IsUpdating = false;
                state.LastError = null;
                state.LastUpdateTime = DateTimeOffset.UtcNow;
                state.LastOperationLogFile = Path.GetFileName(logPath);
                state.BlockedVersion = null;
                state.BlockedVersionAt = null;
                state.RejectedVersion = null;
                state.RejectedReason = null;
                state.RejectedVersionAt = null;
                state.CorruptedVersion = null;
                state.CorruptedReason = null;
                state.CorruptedVersionAt = null;
                return state;
            }, cancellationToken);

            _logger.LogInformation("[ACTIVATION] Version {Version} is now LIVE", latest.Version);

            return new DeploymentResult
            {
                Succeeded = true,
                Version = latest.Version,
                Message = "Deployment successful. Manual rollback is available.",
                LogFile = Path.GetFileName(logPath)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[PIPELINE] Deployment stopped for version {Version}. BackupPath={BackupPath}.",
                latest.Version,
                backupPath ?? "none");

            await MarkDeploymentFailedAsync(
                latest.Version,
                "Deployment stopped before completion.",
                ex,
                Path.GetFileName(logPath),
                cancellationToken);

            return new DeploymentResult
            {
                Succeeded = false,
                Version = latest.Version,
                Message = ex.Message,
                LogFile = Path.GetFileName(logPath)
            };
        }
        finally
        {
            OperationLock.Release();
        }
    }

    public async Task<DeploymentResult> RollbackToPreviousVersionAsync(CancellationToken cancellationToken)
    {
        if (!await OperationLock.WaitAsync(0, cancellationToken))
        {
            return new DeploymentResult
            {
                Succeeded = false,
                RolledBack = false,
                Message = "Another deployment operation is already running."
            };
        }

        var logPath = CreateOperationLogPath("rollback", "manual");
        using var scope = BeginOperationLogScope(logPath);

        try
        {
            var rolledBackFromVersion = await _stateStore.GetCurrentVersionAsync(cancellationToken);
            await SetStateAsync(DeploymentStatus.ROLLBACK, Path.GetFileName(logPath), cancellationToken);
            var outcome = await _rollbackService.RollbackLatestAsync(_logger, cancellationToken);

            await _stateStore.UpdateAsync(state =>
            {
                state.CurrentVersion = outcome.Version;
                state.LastKnownGoodVersion = outcome.Version;
                state.LastBackupPath = null;
                state.Status = DeploymentStatus.ROLLED_BACK;
                state.IsUpdating = false;
                state.LastError = null;
                state.LastRollbackTime = DateTimeOffset.UtcNow;
                state.LastOperationLogFile = Path.GetFileName(logPath);
                state.BlockedVersion = rolledBackFromVersion;
                state.BlockedVersionAt = DateTimeOffset.UtcNow;
                return state;
            }, cancellationToken);

            _logger.LogInformation("[ROLLBACK] Manual rollback succeeded");
            _logger.LogInformation("[ROLLBACK] Restored version {Version}", outcome.Version);
            _logger.LogInformation("[ROLLBACK] Blocked rolled-back version {BlockedVersion}", rolledBackFromVersion);

            return new DeploymentResult
            {
                Succeeded = true,
                RolledBack = true,
                Version = outcome.Version,
                Message = "Rollback successful. Backup was consumed; no further rollback is available until the next deployment.",
                LogFile = Path.GetFileName(logPath)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ROLLBACK] Manual rollback failed");
            await MarkFailedAsync("Manual rollback failed.", ex, Path.GetFileName(logPath), cancellationToken);

            return new DeploymentResult
            {
                Succeeded = false,
                RolledBack = false,
                Message = ex.Message,
                LogFile = Path.GetFileName(logPath)
            };
        }
        finally
        {
            OperationLock.Release();
        }
    }

    private async Task SetStateAsync(DeploymentStatus status, string? logFile, CancellationToken cancellationToken)
    {
        await _stateStore.UpdateAsync(state =>
        {
            state.Status = status;
            state.IsUpdating = status is DeploymentStatus.DOWNLOADING
                or DeploymentStatus.VERIFYING
                or DeploymentStatus.STAGING
                or DeploymentStatus.VALIDATING_STATIC
                or DeploymentStatus.VALIDATING_RUNTIME
                or DeploymentStatus.BACKING_UP
                or DeploymentStatus.ACTIVATING
                or DeploymentStatus.RESTARTING
                or DeploymentStatus.ROLLBACK;
            state.LastError = null;
            if (!string.IsNullOrWhiteSpace(logFile))
            {
                state.LastOperationLogFile = logFile;
            }
            return state;
        }, cancellationToken);
    }

    private async Task MarkFailedAsync(string reason, Exception exception, string? logFile, CancellationToken cancellationToken)
    {
        await _stateStore.UpdateAsync(state =>
        {
            state.Status = DeploymentStatus.FAILED;
            state.IsUpdating = false;
            state.LastError = $"{reason} {exception.Message}";
            if (!string.IsNullOrWhiteSpace(logFile))
            {
                state.LastOperationLogFile = logFile;
            }
            return state;
        }, cancellationToken);
    }

    private async Task MarkDeploymentFailedAsync(string version, string reason, Exception exception, string? logFile, CancellationToken cancellationToken)
    {
        await _stateStore.UpdateAsync(state =>
        {
            var failureStatus = state.Status;
            var failureReason = $"{reason} {exception.Message}";

            state.Status = DeploymentStatus.FAILED;
            state.IsUpdating = false;
            state.LastError = failureReason;

            if (IsCandidateValidationFailure(failureStatus))
            {
                state.RejectedVersion = version;
                state.RejectedReason = failureReason;
                state.RejectedVersionAt = DateTimeOffset.UtcNow;
                state.CorruptedVersion = version;
                state.CorruptedReason = failureReason;
                state.CorruptedVersionAt = DateTimeOffset.UtcNow;
            }

            if (!string.IsNullOrWhiteSpace(logFile))
            {
                state.LastOperationLogFile = logFile;
            }

            return state;
        }, cancellationToken);
    }

    private IDisposable? BeginOperationLogScope(string logPath)
    {
        return _logger.BeginScope(new Dictionary<string, object>
        {
            ["OperationLogFile"] = logPath
        });
    }

    private string CreateOperationLogPath(string operation, string version)
    {
        _paths.EnsureInitialized();
        var safeVersion = SanitizePathSegment(version);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        return Path.Combine(_paths.Logs, $"{operation}-{timestamp}-{safeVersion}.log");
    }

    private void CleanupOldBackupsExcept(string backupPathToKeep)
    {
        foreach (var backupPath in Directory.EnumerateDirectories(_paths.Backups, "*", SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(Path.GetFullPath(backupPath), Path.GetFullPath(backupPathToKeep), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                Directory.Delete(backupPath, true);
                _logger.LogInformation("Removed older rollback backup {BackupPath}.", backupPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not remove older rollback backup {BackupPath}.", backupPath);
            }
        }
    }

    private static string SanitizePathSegment(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value;
    }

    private static bool IsSameVersion(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNewerVersion(string latestVersion, string? rejectedVersion)
    {
        if (string.IsNullOrWhiteSpace(rejectedVersion))
        {
            return false;
        }

        return Version.TryParse(latestVersion, out var latest)
            && Version.TryParse(rejectedVersion, out var rejected)
            && latest > rejected;
    }

    private static bool IsCandidateValidationFailure(DeploymentStatus status)
    {
        return status is DeploymentStatus.DOWNLOADING
            or DeploymentStatus.VERIFYING
            or DeploymentStatus.STAGING
            or DeploymentStatus.VALIDATING_STATIC
            or DeploymentStatus.VALIDATING_RUNTIME;
    }
}
