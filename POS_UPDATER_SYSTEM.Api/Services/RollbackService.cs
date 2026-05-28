using System.Text.Json;
using POS_UPDATER_SYSTEM.Api.Models;
using POS_UPDATER_SYSTEM.Api.Services;

public sealed class RollbackService : IRollbackService
{
    private readonly StoragePaths _paths;
    private readonly IHostRuntimeManager _hostRuntimeManager;

    public RollbackService(
        StoragePaths paths,
        IDeploymentStateStore stateStore,
        IHostRuntimeManager hostRuntimeManager,
        ILogger<RollbackService> logger)
    {
        _paths = paths;
        _hostRuntimeManager = hostRuntimeManager;
    }

    public async Task<RollbackOutcome> RollbackLatestAsync(ILogger logger, CancellationToken cancellationToken)
    {
        var backupPath = GetMostRecentBackupPath();
        if (backupPath is null)
        {
            throw new InvalidOperationException("No backup is available for rollback.");
        }

        var metadata = await ReadMetadataAsync(backupPath, cancellationToken);
        logger.LogInformation("[ROLLBACK] Manual rollback started");
        logger.LogInformation("[ROLLBACK] Backup path: {BackupPath}", ToDisplayPath(backupPath));
        logger.LogInformation("[ROLLBACK] Target version: {Version}", metadata.Version);

        await _hostRuntimeManager.StopLiveAppAsync(logger, cancellationToken);
        RestoreBackupToCurrent(backupPath, logger, cancellationToken);
        await _hostRuntimeManager.RestartLiveAppAsync(logger, cancellationToken);

        Directory.Delete(backupPath, true);
        logger.LogInformation("[ROLLBACK] Backup consumed and removed");

        return new RollbackOutcome(backupPath, metadata.Version);
    }

    private string? GetMostRecentBackupPath()
    {
        if (!Directory.Exists(_paths.Backups))
        {
            return null;
        }

        return Directory.EnumerateDirectories(_paths.Backups, "*", SearchOption.TopDirectoryOnly)
            .Select(path => new DirectoryInfo(path))
            .OrderByDescending(dir => dir.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private static async Task<BackupMetadata> ReadMetadataAsync(string backupPath, CancellationToken cancellationToken)
    {
        var metadataPath = Path.Combine(backupPath, "backup.json");
        if (!File.Exists(metadataPath))
        {
            return new BackupMetadata
            {
                Version = ParseVersionFromBackupFolder(backupPath),
                CreatedAtUtc = Directory.GetCreationTimeUtc(backupPath)
            };
        }

        var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
        return JsonSerializer.Deserialize<BackupMetadata>(json)
            ?? new BackupMetadata
            {
                Version = ParseVersionFromBackupFolder(backupPath),
                CreatedAtUtc = Directory.GetCreationTimeUtc(backupPath)
            };
    }

    private void RestoreBackupToCurrent(string backupPath, ILogger logger, CancellationToken cancellationToken)
    {
        var tempRestore = _paths.Current + "_rollback_" + DateTime.UtcNow.Ticks;
        var failedCurrent = _paths.Current + "_failed_" + DateTime.UtcNow.Ticks;

        Directory.CreateDirectory(tempRestore);
        logger.LogInformation("[ROLLBACK] Restoring backup into temporary rollback folder");
        CopyDirectory(backupPath, tempRestore, includeMetadata: false);

        if (!File.Exists(Path.Combine(tempRestore, "index.html")))
        {
            throw new InvalidOperationException("Rollback failed: backup does not contain index.html.");
        }

        try
        {
            if (Directory.Exists(_paths.Current))
            {
                MoveDirectoryWithRetry(_paths.Current, failedCurrent, logger, cancellationToken);
            }

            MoveDirectoryWithRetry(tempRestore, _paths.Current, logger, cancellationToken);
        }
        catch
        {
            logger.LogError("[ROLLBACK] Atomic rollback swap failed");
            if (!Directory.Exists(_paths.Current) && Directory.Exists(failedCurrent))
            {
                MoveDirectoryWithRetry(failedCurrent, _paths.Current, logger, CancellationToken.None);
            }

            throw;
        }

        TryDeleteDirectory(failedCurrent, logger, "Failed Current cleanup");
    }

    private static void CopyDirectory(string source, string target, bool includeMetadata)
    {
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            if (!includeMetadata && string.Equals(Path.GetFileName(file), "backup.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var dest = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, true);
        }
    }

    private static void MoveDirectoryWithRetry(string source, string target, ILogger logger, CancellationToken ct)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                Directory.Move(source, target);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < maxAttempts)
            {
                logger.LogWarning("[ROLLBACK] File lock detected during rollback swap. Retry {Attempt}.", attempt);
                Thread.Sleep(TimeSpan.FromMilliseconds(300 * attempt));
            }
        }

        Directory.Move(source, target);
    }

    private static void TryDeleteDirectory(string path, ILogger logger, string operation)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, true);
        }
        catch
        {
            logger.LogWarning("[ROLLBACK] {Operation} failed for {Path}. Manual cleanup may be required.", operation, path);
        }
    }

    private static string ParseVersionFromBackupFolder(string backupPath)
    {
        var folder = Path.GetFileName(backupPath);
        var separator = folder.LastIndexOf('_');
        return separator <= 0 ? "0.0.0" : folder[..separator];
    }

    private static string ToDisplayPath(string path)
    {
        var storageIndex = path.IndexOf($"{Path.DirectorySeparatorChar}Storage{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
        return storageIndex < 0 ? path : path[(storageIndex + 1)..];
    }
}
