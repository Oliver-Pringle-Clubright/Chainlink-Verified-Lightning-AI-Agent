using LightningAgentMarketPlace.Api.Helpers;
using LightningAgentMarketPlace.Core.Configuration;
using LightningAgentMarketPlace.Core.Interfaces.Data;
using LightningAgentMarketPlace.Core.Models;
using LightningAgentMarketPlace.Data;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LightningAgentMarketPlace.Api.Controllers;

/// <summary>
/// Admin-only endpoints for database backup and restore operations.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/admin/backup")]
[Route("api/v{version:apiVersion}/admin/backup")]
[Produces("application/json")]
public class BackupController : ControllerBase
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly BackupSettings _backupSettings;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BackupController> _logger;
    private readonly IAuditLogRepository _auditLogRepository;

    public BackupController(
        SqliteConnectionFactory connectionFactory,
        IOptions<BackupSettings> backupSettings,
        IConfiguration configuration,
        ILogger<BackupController> logger,
        IAuditLogRepository auditLogRepository)
    {
        _connectionFactory = connectionFactory;
        _backupSettings = backupSettings.Value;
        _configuration = configuration;
        _logger = logger;
        _auditLogRepository = auditLogRepository;
    }

    /// <summary>
    /// Create a backup of the SQLite database using VACUUM INTO.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateBackup()
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Forbid();

        var backupDir = Path.GetFullPath(_backupSettings.BackupDirectory);

        if (!Directory.Exists(backupDir))
        {
            Directory.CreateDirectory(backupDir);
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var backupFileName = $"lightningagent_backup_{timestamp}.db";
        var backupFilePath = Path.Combine(backupDir, backupFileName);

        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"VACUUM INTO @backupPath";
            cmd.Parameters.AddWithValue("@backupPath", backupFilePath);
            cmd.ExecuteNonQuery();

            var fileInfo = new FileInfo(backupFilePath);

            _logger.LogInformation(
                "Database backup created: {BackupPath} ({Size} bytes)",
                backupFilePath, fileInfo.Length);

            await _auditLogRepository.CreateAsync(new AuditLogEntry
            {
                EventType = "BackupCreated",
                EntityType = "Database",
                EntityId = 0,
                Action = "CreateBackup",
                Details = $"Backup created: {backupFileName} ({fileInfo.Length} bytes)",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = HttpContext.Request.Headers.UserAgent.ToString(),
                CreatedAt = DateTime.UtcNow
            });

            return Ok(new
            {
                message = "Database backup created successfully.",
                backupFileName,
                backupFilePath,
                sizeBytes = fileInfo.Length,
                createdAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create database backup");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = $"Failed to create backup: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// List available backup files.
    /// </summary>
    [HttpGet("/api/admin/backups")]
    [HttpGet("/api/v{version:apiVersion}/admin/backups")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult ListBackups()
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Forbid();

        var backupDir = Path.GetFullPath(_backupSettings.BackupDirectory);

        if (!Directory.Exists(backupDir))
        {
            return Ok(new { backups = Array.Empty<object>() });
        }

        var files = Directory.GetFiles(backupDir, "*.db")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new
            {
                fileName = f.Name,
                sizeBytes = f.Length,
                createdAt = f.CreationTimeUtc
            })
            .ToList();

        return Ok(new { backups = files });
    }

    /// <summary>
    /// Restore the database from a named backup file. Requires application restart.
    /// </summary>
    [HttpPost("restore")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RestoreBackup([FromBody] RestoreBackupRequest request)
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.BackupFileName))
            return BadRequest("BackupFileName is required.");

        // Prevent directory traversal attacks
        var sanitizedFileName = Path.GetFileName(request.BackupFileName);
        if (sanitizedFileName != request.BackupFileName)
            return BadRequest("Invalid backup file name.");

        var backupDir = Path.GetFullPath(_backupSettings.BackupDirectory);
        var backupFilePath = Path.Combine(backupDir, sanitizedFileName);

        if (!System.IO.File.Exists(backupFilePath))
            return NotFound($"Backup file '{sanitizedFileName}' not found.");

        // Determine the main database file path from the connection string
        var connectionString = _configuration.GetConnectionString("Sqlite")
            ?? "Data Source=lightningagent.db;Cache=Shared";

        var dbPath = ExtractDataSource(connectionString);
        if (string.IsNullOrEmpty(dbPath))
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Unable to determine database file path from connection string." });

        try
        {
            // Copy the backup file over the main database file
            System.IO.File.Copy(backupFilePath, dbPath, overwrite: true);

            _logger.LogWarning(
                "Database restored from backup: {BackupPath}. Application restart is required.",
                backupFilePath);

            await _auditLogRepository.CreateAsync(new AuditLogEntry
            {
                EventType = "BackupRestored",
                EntityType = "Database",
                EntityId = 0,
                Action = "RestoreBackup",
                Details = $"Database restored from backup: {sanitizedFileName}",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = HttpContext.Request.Headers.UserAgent.ToString(),
                CreatedAt = DateTime.UtcNow
            });

            return Ok(new
            {
                message = "Database restored successfully from backup. An application restart is required for changes to take effect.",
                restoredFrom = sanitizedFileName,
                warning = "The application must be restarted for the restored database to be fully active. Active connections may still reference the old data."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore database from backup {BackupPath}", backupFilePath);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = $"Failed to restore backup: {ex.Message}"
            });
        }
    }

    private static string ExtractDataSource(string connectionString)
    {
        // Parse "Data Source=<path>" from the connection string
        foreach (var part in connectionString.Split(';'))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed["Data Source=".Length..].Trim();
            }
        }

        return string.Empty;
    }
}

public class RestoreBackupRequest
{
    public string BackupFileName { get; set; } = string.Empty;
}
