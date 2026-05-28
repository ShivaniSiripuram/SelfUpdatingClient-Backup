using System;
using System.Threading;
using System.Threading.Tasks;
using POS_UPDATER_SYSTEM.Api.Models;

namespace POS_UPDATER_SYSTEM.Api.Services
{
    public interface IDeploymentStateStore
    {
        Task<DeploymentState> GetAsync(CancellationToken ct);
        Task<string> GetCurrentVersionAsync(CancellationToken ct);
        Task SaveVersionAsync(string version, CancellationToken ct);
        Task<string?> GetLastBackupAsync(CancellationToken ct);
        Task SaveLastBackupAsync(string? path, CancellationToken ct);
        Task UpdateAsync(Func<DeploymentState, DeploymentState> update, CancellationToken ct);
    }
}
