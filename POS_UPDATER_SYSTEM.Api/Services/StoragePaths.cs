using Microsoft.Extensions.Options;
using POS_UPDATER_SYSTEM.Api.Options;

namespace POS_UPDATER_SYSTEM.Api.Services;

public sealed class StoragePaths
{
    public StoragePaths(
        IWebHostEnvironment environment,
        IOptions<UpdaterOptions> options)
    {
        if (environment == null)
            throw new ArgumentNullException(nameof(environment));

        if (options == null)
            throw new ArgumentNullException(nameof(options));

        if (options.Value == null)
            throw new ArgumentNullException(nameof(options.Value));

        var configuredRoot = options.Value.StorageRoot;

        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            configuredRoot = "Storage";
        }

        // IIS-safe storage root resolution
        Root = Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(environment.ContentRootPath, configuredRoot);

        Current = Path.Combine(Root, "Current");
        Downloads = Path.Combine(Root, "Downloads");
        Staging = Path.Combine(Root, "Staging");
        Backups = Path.Combine(Root, "Backups");
        Logs = Path.Combine(Root, "Logs");
        Registry = Path.Combine(Root, "Registry");

        DeploymentStateFile =
            Path.Combine(Registry, "deployment-state.json");

        // Log resolved paths for diagnostics
        Console.WriteLine($"CONTENT ROOT: {environment.ContentRootPath}");
        Console.WriteLine($"STORAGE ROOT: {Root}");
        Console.WriteLine($"CURRENT PATH: {Current}");
        Console.WriteLine($"STAGING PATH: {Staging}");
    }

    public string Root { get; }

    public string Current { get; }

    public string Downloads { get; }

    public string Staging { get; }

    public string Backups { get; }

    public string Logs { get; }

    public string Registry { get; }

    public string DeploymentStateFile { get; }

    public void EnsureInitialized()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Current);
        Directory.CreateDirectory(Downloads);
        Directory.CreateDirectory(Staging);
        Directory.CreateDirectory(Backups);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Registry);

        Console.WriteLine("Storage directories initialized.");
    }
}