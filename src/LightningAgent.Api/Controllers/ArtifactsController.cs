using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace LightningAgent.Api.Controllers;

/// <summary>
/// Manages file/artifact uploads and downloads for tasks.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/artifacts")]
[Route("api/v{version:apiVersion}/artifacts")]
[Produces("application/json")]
public class ArtifactsController : ControllerBase
{
    private readonly IArtifactRepository _artifactRepository;
    private readonly ILogger<ArtifactsController> _logger;
    private const long MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB

    public ArtifactsController(
        IArtifactRepository artifactRepository,
        ILogger<ArtifactsController> logger)
    {
        _artifactRepository = artifactRepository;
        _logger = logger;
    }

    /// <summary>
    /// Upload a file artifact for a task. Max 50 MB.
    /// </summary>
    [HttpPost("upload")]
    [DisableRequestSizeLimit]
    [ProducesResponseType(typeof(Artifact), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Artifact>> Upload(
        [FromForm] IFormFile file,
        [FromForm] int taskId,
        [FromForm] int? milestoneId,
        [FromForm] int? agentId,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("File is required.");

        if (file.Length > MaxFileSizeBytes)
            return BadRequest($"File exceeds maximum size of {MaxFileSizeBytes / (1024 * 1024)} MB.");

        if (taskId <= 0)
            return BadRequest("taskId is required.");

        // Sanitize the filename to prevent directory traversal
        var safeFileName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
            safeFileName = $"upload_{DateTime.UtcNow:yyyyMMddHHmmss}";

        // Save to local artifacts directory
        var artifactsDir = Path.Combine(".", "artifacts", taskId.ToString());
        Directory.CreateDirectory(artifactsDir);

        // Make filename unique to avoid collisions
        var uniqueFileName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{safeFileName}";
        var filePath = Path.Combine(artifactsDir, uniqueFileName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream, ct);
        }

        var artifact = new Artifact
        {
            TaskId = taskId,
            MilestoneId = milestoneId,
            AgentId = agentId,
            FileName = safeFileName,
            ContentType = file.ContentType ?? "application/octet-stream",
            SizeBytes = file.Length,
            StoragePath = filePath,
            CreatedAt = DateTime.UtcNow
        };

        var id = await _artifactRepository.CreateAsync(artifact, ct);
        artifact.Id = id;

        _logger.LogInformation(
            "Artifact uploaded: {ArtifactId} ({FileName}, {SizeBytes} bytes) for task {TaskId}",
            id, safeFileName, file.Length, taskId);

        return Ok(artifact);
    }

    /// <summary>
    /// Download an artifact by ID.
    /// </summary>
    [HttpGet("{id:int}/download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(int id, CancellationToken ct)
    {
        var artifact = await _artifactRepository.GetByIdAsync(id, ct);
        if (artifact is null)
            return NotFound($"Artifact {id} not found.");

        if (!System.IO.File.Exists(artifact.StoragePath))
            return NotFound("Artifact file not found on disk.");

        var stream = new FileStream(artifact.StoragePath, FileMode.Open, FileAccess.Read);
        return File(stream, artifact.ContentType, artifact.FileName);
    }

    /// <summary>
    /// List all artifacts for a given task.
    /// </summary>
    [HttpGet("task/{taskId:int}")]
    [ProducesResponseType(typeof(IReadOnlyList<Artifact>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<Artifact>>> GetByTask(int taskId, CancellationToken ct)
    {
        var artifacts = await _artifactRepository.GetByTaskIdAsync(taskId, ct);
        return Ok(artifacts);
    }
}
