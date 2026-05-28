namespace POS_UPDATER_SYSTEM.Api.Models;

public sealed class BackupMetadata
{
    public required string Version { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public string? SourcePath { get; init; }
}
