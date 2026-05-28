using Microsoft.Extensions.Options;
using POS_UPDATER_SYSTEM.Api.Models;
using POS_UPDATER_SYSTEM.Api.Options;

namespace POS_UPDATER_SYSTEM.Api.Services;

public sealed class LiveAppValidator : ILiveAppValidator
{
    private readonly HttpClient _httpClient;
    private readonly StoragePaths _paths;
    private readonly UpdaterOptions _options;

    public LiveAppValidator(HttpClient httpClient, StoragePaths paths, IOptions<UpdaterOptions> options)
    {
        _httpClient = httpClient;
        _paths = paths;
        _options = options.Value;
    }

    public async Task ValidateAsync(ILogger logger, CancellationToken cancellationToken)
    {
        logger.LogInformation("Post-activation validation started for Current path {CurrentPath}.", _paths.Current);

        if (!File.Exists(Path.Combine(_paths.Current, "index.html")))
        {
            logger.LogError("Post-activation validation failed. Current/index.html is missing under {CurrentPath}.", _paths.Current);
            throw new InvalidOperationException("Post-activation validation failed. Current/index.html is missing.");
        }

        var mainScriptExists = Directory.EnumerateFiles(_paths.Current, "*.js", SearchOption.AllDirectories)
            .Any(path => Path.GetFileName(path).StartsWith("main", StringComparison.OrdinalIgnoreCase));

        if (!mainScriptExists)
        {
            logger.LogError("Post-activation validation failed. Current main script is missing under {CurrentPath}.", _paths.Current);
            throw new InvalidOperationException("Post-activation validation failed. Current main script is missing.");
        }

        if (!string.IsNullOrWhiteSpace(_options.LiveAppBaseUrl))
        {
            var indexUri = new Uri(new Uri(_options.LiveAppBaseUrl.TrimEnd('/') + "/"), "index.html");
            using var response = await _httpClient.GetAsync(indexUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Post-activation validation failed. {IndexUri} returned HTTP {StatusCode}.", indexUri, (int)response.StatusCode);
                throw new InvalidOperationException($"Post-activation validation failed. {indexUri} returned {(int)response.StatusCode}.");
            }
        }

        logger.LogInformation("Post-activation validation successful.");
    }
}
