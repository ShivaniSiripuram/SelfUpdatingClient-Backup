namespace POS_UPDATER_SYSTEM.Api.Models
{
    public sealed class DeploymentState
    {
        public string CurrentVersion { get; set; } = "0.0.0";
        public string? LastBackupPath { get; set; }
        public string LastKnownGoodVersion { get; set; } = "0.0.0";

        public bool IsUpdating { get; set; }
        public bool IsRollbackAvailable => !string.IsNullOrWhiteSpace(LastBackupPath);

        public DeploymentStatus Status { get; set; } = DeploymentStatus.LIVE;

        public string? LastError { get; set; }
        public DateTimeOffset? LastCheckTime { get; set; }
        public DateTimeOffset? LastUpdateTime { get; set; }
        public DateTimeOffset? LastRollbackTime { get; set; }
        public string? LastOperationLogFile { get; set; }
        public string? BlockedVersion { get; set; }
        public DateTimeOffset? BlockedVersionAt { get; set; }
        public string? RejectedVersion { get; set; }
        public string? RejectedReason { get; set; }
        public DateTimeOffset? RejectedVersionAt { get; set; }
        public string? CorruptedVersion { get; set; }
        public string? CorruptedReason { get; set; }
        public DateTimeOffset? CorruptedVersionAt { get; set; }
    }
}
