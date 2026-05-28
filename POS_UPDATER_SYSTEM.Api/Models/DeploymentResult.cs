namespace POS_UPDATER_SYSTEM.Api.Models;

public sealed class DeploymentResult
{
    public bool Succeeded { get; init; }

    public bool RolledBack { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? Version { get; init; }

    public string? LogFile { get; init; }
}
