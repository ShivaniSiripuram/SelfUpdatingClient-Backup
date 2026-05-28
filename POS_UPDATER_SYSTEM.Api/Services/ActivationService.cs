using System.Text.Json;
using POS_UPDATER_SYSTEM.Api.Models;

namespace POS_UPDATER_SYSTEM.Api.Services;

public sealed class ActivationService : IActivationService
{
    private readonly StoragePaths _paths;

    public ActivationService(StoragePaths paths, ILogger<ActivationService> logger)
    {
        _paths = paths;
    }

    public async Task<string> BackupCurrentAsync(string currentVersion, ILogger logger, CancellationToken ct)
    {
        var backupVersion = string.IsNullOrWhiteSpace(currentVersion) ? "0.0.0" : currentVersion;
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var backupPath = Path.Combine(_paths.Backups, $"{SanitizePathSegment(backupVersion)}_{timestamp}");

        logger.LogInformation("[BACKUP] Backup process started");
        logger.LogInformation("[BACKUP] Creating snapshot of current live application");
        logger.LogInformation("[BACKUP] Backup path: {BackupPath}", ToDisplayPath(backupPath));

        try
        {
            Directory.CreateDirectory(backupPath);
            CopyDirectory(_paths.Current, backupPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError("[BACKUP] Backup creation failed");
            logger.LogError("[BACKUP] File lock detected during backup");
            throw;
        }

        var metadata = new BackupMetadata
        {
            Version = backupVersion,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            SourcePath = _paths.Current
        };

        await File.WriteAllTextAsync(
            Path.Combine(backupPath, "backup.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
            ct);

        logger.LogInformation("[BACKUP] Backup completed successfully");
        return backupPath;
    }

    public Task ActivateAsync(string stagingPath, ILogger logger, CancellationToken ct)
    {
        logger.LogInformation("[ACTIVATION] Stopping live application host");
        logger.LogInformation("[ACTIVATION] Replacing Current application with staged version");

        var newRoot = ResolveApplicationRoot(stagingPath);
        if (!File.Exists(Path.Combine(newRoot, "index.html")))
        {
            throw new InvalidOperationException("Activation failed: index.html missing in staging.");
        }

        var tempSwap = _paths.Current + "_swap_" + DateTime.UtcNow.Ticks;
        var oldCurrentBackup = _paths.Current + "_old_" + DateTime.UtcNow.Ticks;

        if (Directory.Exists(tempSwap))
        {
            Directory.Delete(tempSwap, true);
        }

        Directory.CreateDirectory(tempSwap);
        CopyDirectory(newRoot, tempSwap);

        if (!File.Exists(Path.Combine(tempSwap, "index.html")))
        {
            throw new InvalidOperationException("Activation failed: swap validation failed.");
        }

        try
        {
            logger.LogInformation("[ACTIVATION] Atomic application swap initiated");
            if (Directory.Exists(_paths.Current))
            {
                MoveDirectoryWithRetry(_paths.Current, oldCurrentBackup, logger, ct);
            }

            MoveDirectoryWithRetry(tempSwap, _paths.Current, logger, ct);
        }
        catch
        {
            logger.LogError("[ACTIVATION] Atomic application swap failed");
            if (!Directory.Exists(_paths.Current) && Directory.Exists(oldCurrentBackup))
            {
                MoveDirectoryWithRetry(oldCurrentBackup, _paths.Current, logger, CancellationToken.None);
            }

            throw;
        }

        if (Directory.Exists(oldCurrentBackup))
        {
            TryDeleteDirectory(oldCurrentBackup, logger, "Old Current cleanup");
        }

        logger.LogInformation("[ACTIVATION] Restarting application host");
        return Task.CompletedTask;
    }

    private static string ResolveApplicationRoot(string stagingPath)
    {
        if (File.Exists(Path.Combine(stagingPath, "index.html")))
        {
            return stagingPath;
        }

        var candidates = Directory
            .GetFiles(stagingPath, "index.html", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .Where(x => x != null)
            .ToList();

        return candidates.FirstOrDefault() ?? stagingPath;
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            CopyFileWithRetry(file, dest);
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
            catch (Exception ex) when (IsTransientFileException(ex) && attempt < maxAttempts)
            {
                logger.LogWarning("[ACTIVATION] File lock detected during atomic swap. Retry {Attempt}.", attempt);
                Thread.Sleep(TimeSpan.FromMilliseconds(300 * attempt));
            }
        }

        Directory.Move(source, target);
    }

    private static void CopyFileWithRetry(string source, string target)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                File.Copy(source, target, true);
                return;
            }
            catch (Exception ex) when (IsTransientFileException(ex) && attempt < maxAttempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(200 * attempt));
            }
        }

        File.Copy(source, target, true);
    }

    private static void TryDeleteDirectory(string path, ILogger logger, string operation)
    {
        try
        {
            Directory.Delete(path, true);
        }
        catch
        {
            logger.LogWarning("[ACTIVATION] {Operation} failed for {Path}. Manual cleanup may be required.", operation, path);
        }
    }

    private static bool IsTransientFileException(Exception ex)
    {
        return ex is IOException or UnauthorizedAccessException;
    }

    private static string SanitizePathSegment(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value;
    }

    private static string ToDisplayPath(string path)
    {
        var storageIndex = path.IndexOf($"{Path.DirectorySeparatorChar}Storage{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
        return storageIndex < 0 ? path : path[(storageIndex + 1)..];
    }
}
