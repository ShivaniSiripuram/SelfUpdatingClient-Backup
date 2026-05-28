namespace POS_UPDATER_SYSTEM.Api.Models;

public sealed class UpdateCheckResult
{
    public required string CurrentVersion { get; init; }

    public required string LatestVersion { get; init; }

    public bool IsUpdateAvailable { get; init; }

    public LatestUpdateInfo? Latest { get; init; }

    public string? LogFile { get; init; }

    public string? RejectedVersion { get; init; }

    public string? RejectedReason { get; init; }

    public bool IsLatestRejected { get; init; }

    public string? CorruptedVersion { get; init; }

    public string? CorruptedReason { get; init; }

    public bool IsLatestCorrupted { get; init; }

    public string? RemoteLatestVersion { get; init; }

    public bool IsLatestSuppressed { get; init; }
}
