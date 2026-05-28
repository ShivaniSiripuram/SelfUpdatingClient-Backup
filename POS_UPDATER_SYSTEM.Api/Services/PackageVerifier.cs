using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using POS_UPDATER_SYSTEM.Api.Models;
using POS_UPDATER_SYSTEM.Api.Options;

namespace POS_UPDATER_SYSTEM.Api.Services;

public sealed class PackageVerifier : IPackageVerifier
{
    private readonly UpdaterOptions _options;

    public PackageVerifier(IOptions<UpdaterOptions> options)
    {
        _options = options.Value;
    }

    public async Task VerifyAsync(string packagePath, string expectedSha256, ILogger logger, CancellationToken cancellationToken)
    {
        logger.LogInformation("[VERIFY] SHA256 validation started");
        var actualHash = await ComputeSha256Async(packagePath, cancellationToken);

        if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("[VERIFY] Package validation failed");
            logger.LogError("[VERIFY] SHA256 mismatch. Expected {ExpectedSha256}, actual {ActualSha256}", expectedSha256, actualHash);
            throw new InvalidOperationException($"SHA256 mismatch. Expected {expectedSha256}, actual {actualHash}.");
        }

        logger.LogInformation("[VERIFY] SHA256 verification successful");
        logger.LogInformation("[VERIFY] ZIP integrity validation started");

        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .Select(entry => entry.FullName.Replace('\\', '/'))
            .ToArray();

        if (!entries.Any(entry => string.Equals(Path.GetFileName(entry), "index.html", StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogError("[VERIFY] Package validation failed");
            logger.LogError("[VERIFY] index.html missing inside package");
            throw new InvalidOperationException("Package is invalid. index.html was not found.");
        }

        logger.LogInformation("[VERIFY] Angular shell validation started");

        if (!entries.Any(IsMainScript))
        {
            logger.LogError("[VERIFY] Package validation failed");
            logger.LogError("[VERIFY] main*.js missing inside package");
            throw new InvalidOperationException($"Package is invalid. No script matching {_options.MainScriptPattern} was found.");
        }

        logger.LogInformation("[VERIFY] main*.js validation successful");
    }

    private static async Task<string> ComputeSha256Async(string packagePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(packagePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private bool IsMainScript(string entry)
    {
        var fileName = Path.GetFileName(entry);
        if (string.Equals(fileName, "main.js", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return fileName.StartsWith("main", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".js", StringComparison.OrdinalIgnoreCase);
    }
}
