using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using POS_UPDATER_SYSTEM.Api.Models;
using POS_UPDATER_SYSTEM.Api.Options;

namespace POS_UPDATER_SYSTEM.Api.Services;

public sealed class UpdateClient : IUpdateClient
{
    private readonly HttpClient _httpClient;
    private readonly StoragePaths _paths;
    private readonly UpdaterOptions _options;

    public UpdateClient(HttpClient httpClient, StoragePaths paths, IOptions<UpdaterOptions> options)
    {
        _httpClient = httpClient;
        _paths = paths;
        _options = options.Value;
    }

    public async Task<LatestUpdateInfo> GetLatestAsync(CancellationToken cancellationToken)
    {
        var latest = await _httpClient.GetFromJsonAsync<LatestUpdateInfo>(_options.LatestJsonUrl, cancellationToken);
        return latest ?? throw new InvalidOperationException("latest.json was empty or invalid.");
    }

    public async Task<string> GetLatestVersionAsync(CancellationToken cancellationToken)
    {
        // Fetch the manifest but only parse the version field to keep this lightweight.
        using var response = await _httpClient.GetAsync(_options.LatestJsonUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var doc = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (doc.RootElement.TryGetProperty("version", out var versionProp))
        {
            return versionProp.GetString() ?? string.Empty;
        }

        return string.Empty;
    }



    public async Task<string> DownloadPackageAsync(LatestUpdateInfo latest, ILogger logger, CancellationToken cancellationToken)
    {
        _paths.EnsureInitialized();

        var packageUri = ResolvePackageUri(latest.Package);
        var targetPath = Path.Combine(_paths.Downloads, latest.Package);

        logger.LogInformation("[DOWNLOAD] Package download initiated");
        logger.LogInformation("[DOWNLOAD] Source: {PackageUri}", packageUri);
        logger.LogInformation("[DOWNLOAD] Writing package to {TargetPath}", ToDisplayPath(targetPath));

        try
        {
            using var response = await _httpClient.GetAsync(packageUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("[DOWNLOAD] Package download failed");
                logger.LogError("[DOWNLOAD] Remote server returned HTTP {StatusCode}", (int)response.StatusCode);
                throw new HttpRequestException($"Remote server returned HTTP {(int)response.StatusCode}", null, response.StatusCode);
            }

            logger.LogInformation("[DOWNLOAD] Package stream established");
            await using var packageStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = File.Create(targetPath);
            await packageStream.CopyToAsync(fileStream, cancellationToken);

            var sizeBytes = new FileInfo(targetPath).Length;
            logger.LogInformation("[DOWNLOAD] Package download completed successfully");
            logger.LogInformation("[DOWNLOAD] Total package size: {Size}", FormatSize(sizeBytes));
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("[DOWNLOAD] Package download failed");
            logger.LogError("[DOWNLOAD] {Message}", ex.Message);
            throw;
        }

        return targetPath;
    }

    private Uri ResolvePackageUri(string package)
    {
        if (Uri.TryCreate(package, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri;
        }

        var latestUri = new Uri(_options.LatestJsonUrl, UriKind.Absolute);
        return new Uri(latestUri, package);
    }

    private static string ToDisplayPath(string path)
    {
        var storageIndex = path.IndexOf($"{Path.DirectorySeparatorChar}Storage{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
        if (storageIndex < 0)
        {
            return path;
        }

        return path[(storageIndex + 1)..];
    }

    private static string FormatSize(long bytes)
    {
        var mb = bytes / 1024d / 1024d;
        return $"{mb:0.##} MB";
    }
}
