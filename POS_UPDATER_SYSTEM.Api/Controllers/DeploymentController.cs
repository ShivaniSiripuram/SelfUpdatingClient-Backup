using Microsoft.AspNetCore.Mvc;
using POS_UPDATER_SYSTEM.Api.Models;
using POS_UPDATER_SYSTEM.Api.Services;

namespace POS_UPDATER_SYSTEM.Api.Controllers;

[ApiController]
[Route("api/deployment")]
public sealed class DeploymentController : ControllerBase
{
    private readonly IDeploymentOrchestrator _orchestrator;
    private readonly IDeploymentStateStore _stateStore;
    private readonly StoragePaths _paths;
    private readonly ILogger<DeploymentController> _logger;

    public DeploymentController(
        IDeploymentOrchestrator orchestrator,
        IDeploymentStateStore stateStore,
        StoragePaths paths,
        ILogger<DeploymentController> logger)
    {
        _orchestrator = orchestrator;
        _stateStore = stateStore;
        _paths = paths;
        _logger = logger;
    }

    [HttpGet("state")]
    public Task<DeploymentState> GetState(CancellationToken cancellationToken)
    {
        return _stateStore.GetAsync(cancellationToken);
    }

    [HttpPost("check")]
    public async Task<ActionResult<UpdateCheckResult>> CheckForUpdate(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _orchestrator.CheckForUpdateAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Check for update API failed.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
        }
    }

    [HttpPost("deploy")]
    public Task<DeploymentResult> DeployIfAvailable(CancellationToken cancellationToken)
    {
        return _orchestrator.RunDeploymentIfAvailableAsync(cancellationToken);
    }

    [HttpPost("rollback")]
    public Task<DeploymentResult> Rollback(CancellationToken cancellationToken)
    {
        return _orchestrator.RollbackToPreviousVersionAsync(cancellationToken);
    }

    

    [HttpGet("logs")]
    public ActionResult<IEnumerable<DeploymentLogSummary>> GetLogs()
    {
        _paths.EnsureInitialized();
        var logs = Directory.EnumerateFiles(_paths.Logs, "*.log", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(file => !file.Name.StartsWith("check-", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(50)
            .Select(file => new DeploymentLogSummary(file.Name, file.LastWriteTimeUtc, file.Length))
            .ToArray();

        return Ok(logs);
    }

    [HttpGet("logs/{fileName}")]
    public async Task<ActionResult<DeploymentLogContent>> GetLog(string fileName, CancellationToken cancellationToken)
    {
        _paths.EnsureInitialized();
        var safeFileName = Path.GetFileName(fileName);
        if (!string.Equals(safeFileName, fileName, StringComparison.Ordinal))
        {
            return BadRequest(new { message = "Invalid log file name." });
        }

        var path = Path.Combine(_paths.Logs, safeFileName);
        if (!System.IO.File.Exists(path))
        {
            return NotFound(new { message = "Log file was not found." });
        }

        var content = await System.IO.File.ReadAllTextAsync(path, cancellationToken);
        return Ok(new DeploymentLogContent(safeFileName, content));
    }
}

public sealed record DeploymentLogSummary(string FileName, DateTime LastModifiedUtc, long SizeBytes);

public sealed record DeploymentLogContent(string FileName, string Content);
