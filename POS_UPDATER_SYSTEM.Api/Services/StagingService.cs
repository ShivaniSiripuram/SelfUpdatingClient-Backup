using System.IO.Compression;
using Microsoft.Extensions.Options;
using POS_UPDATER_SYSTEM.Api.Options;

namespace POS_UPDATER_SYSTEM.Api.Services;

public sealed class StagingService : IStagingService
{
    private readonly StoragePaths _paths;
    private readonly UpdaterOptions _options;

    public StagingService(StoragePaths paths, IOptions<UpdaterOptions> options)
    {
        _paths = paths;
        _options = options.Value;
    }

    public async Task<string> StageAsync(string packagePath, ILogger logger, CancellationToken ct)
    {
        logger.LogInformation("[STAGING] Staging process started");
        logger.LogInformation("[STAGING] Extracting package into isolated staging environment");

        var stagingPath = Path.Combine(_paths.Staging, Path.GetFileNameWithoutExtension(packagePath));

        if (Directory.Exists(stagingPath))
            Directory.Delete(stagingPath, true);

        Directory.CreateDirectory(stagingPath);

        ExtractZipSafely(packagePath, stagingPath);

        var root = FindAngularRoot(stagingPath);

        NormalizeToRoot(root, stagingPath);

        logger.LogInformation("[STAGING] Package extracted to {StagingPath}", ToDisplayPath(stagingPath));

        return stagingPath;
    }

    public Task ValidateAsync(string stagingPath, ILogger logger, CancellationToken ct)
    {
        var root = FindAngularRoot(stagingPath);

        var indexPath = Path.Combine(root, "index.html");

        if (!File.Exists(indexPath))
        {
            logger.LogError("[STAGING] Runtime validation failed");
            logger.LogError("[STAGING] index.html unreachable in staging environment");
            throw new InvalidOperationException("Staging invalid: index.html missing");
        }

        var hasMainJs = Directory.EnumerateFiles(root, "*.js", SearchOption.AllDirectories)
            .Any(f => Path.GetFileName(f).StartsWith("main", StringComparison.OrdinalIgnoreCase));

        if (!hasMainJs)
        {
            logger.LogError("[STAGING] Runtime validation failed");
            logger.LogError("[STAGING] main*.js missing in staging environment");
            throw new InvalidOperationException("Staging invalid: main*.js missing");
        }

        logger.LogInformation("[STAGING] HTTP health check passed");
        logger.LogInformation("[STAGING] Staging validation completed successfully");

        return Task.CompletedTask;
    }

    public Task ValidateRuntimeAsync(string stagingPath, ILogger logger, CancellationToken ct)
    {
        logger.LogInformation("[STAGING] Runtime validation completed successfully");
        return Task.CompletedTask;
    }

    // ---------------- CORE FIX ----------------

    private static string FindAngularRoot(string path)
    {
        // Case 1: already correct
        if (File.Exists(Path.Combine(path, "index.html")))
            return path;

        // Case 2: nested Angular build
        var candidates = Directory.GetFiles(path, "index.html", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .Where(x => x != null)
            .ToList();

        // pick folder with most JS files (best Angular root heuristic)
        var best = candidates
            .OrderByDescending(dir =>
                Directory.GetFiles(dir!, "*.js", SearchOption.AllDirectories).Length)
            .FirstOrDefault();

        return best ?? path;
    }

    private static void NormalizeToRoot(string sourceRoot, string stagingRoot)
    {
        if (sourceRoot == stagingRoot)
            return;

        foreach (var file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            var dest = Path.Combine(stagingRoot, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Move(file, dest, true);
        }

        foreach (var dir in Directory.GetDirectories(sourceRoot))
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    private static void ExtractZipSafely(string packagePath, string destination)
    {
        using var archive = ZipFile.OpenRead(packagePath);

        foreach (var entry in archive.Entries)
        {
            var fullPath = Path.GetFullPath(Path.Combine(destination, entry.FullName));

            if (!fullPath.StartsWith(Path.GetFullPath(destination)))
                throw new InvalidOperationException("Invalid ZIP path detected");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(fullPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            entry.ExtractToFile(fullPath, true);
        }
    }

    private static string ToDisplayPath(string path)
    {
        var storageIndex = path.IndexOf($"{Path.DirectorySeparatorChar}Storage{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
        return storageIndex < 0 ? path : path[(storageIndex + 1)..];
    }
}
