using System.Text.Json;
using System.Text.Json.Serialization; // For JsonStringEnumConverter object to json, JSON to object
using POS_UPDATER_SYSTEM.Api.Models;

namespace POS_UPDATER_SYSTEM.Api.Services;

public sealed class DeploymentStateStore : IDeploymentStateStore 
{
    private readonly StoragePaths _paths;
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DeploymentStateStore(StoragePaths paths)
    {
        _paths = paths;
        _filePath = paths.DeploymentStateFile;

        Directory.CreateDirectory(_paths.Registry);
    }

    public async Task<string> GetCurrentVersionAsync(CancellationToken ct)
    {
        var state = await ReadAsync(ct);
        return state.CurrentVersion ?? "0.0.0";
    }

    public async Task SaveVersionAsync(string version, CancellationToken ct)
    {
        var state = await ReadAsync(ct);
        state.CurrentVersion = version;

        await WriteAsync(state, ct);
    }

    public async Task<string?> GetLastBackupAsync(CancellationToken ct)
    {
        var state = await ReadAsync(ct);
        return state.LastBackupPath;
    }

    public async Task SaveLastBackupAsync(string? path, CancellationToken ct)
    {
        var state = await ReadAsync(ct);
        state.LastBackupPath = path;

        await WriteAsync(state, ct);
    }

    public async Task UpdateAsync(Func<DeploymentState, DeploymentState> update, CancellationToken ct)
    {
        if (update is null) throw new ArgumentNullException(nameof(update));

        var state = await ReadAsync(ct);
        var newState = update(state) ?? state;

        await WriteAsync(newState, ct);
    }

    // Added to satisfy IDeploymentStateStore
    public Task<DeploymentState> GetAsync(CancellationToken ct) => ReadAsync(ct);

    private async Task<DeploymentState> ReadAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_filePath))
            {
                return new DeploymentState
                {
                    CurrentVersion = "0.0.0",
                    LastBackupPath = null
                };
            }

            var json = await File.ReadAllTextAsync(_filePath, ct);

            return JsonSerializer.Deserialize<DeploymentState>(json, JsonOptions())
                   ?? new DeploymentState();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task WriteAsync(DeploymentState state, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var json = JsonSerializer.Serialize(state, JsonOptions());

            var tempFile = _filePath + ".tmp";

            await File.WriteAllTextAsync(tempFile, json, ct);

            // atomic replace
            File.Copy(tempFile, _filePath, true);
            File.Delete(tempFile);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static JsonSerializerOptions JsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
