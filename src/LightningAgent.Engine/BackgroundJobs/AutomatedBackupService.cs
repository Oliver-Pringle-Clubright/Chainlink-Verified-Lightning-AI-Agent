using LightningAgent.Core.Configuration;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LightningAgent.Engine.BackgroundJobs;

/// <summary>
/// Runs scheduled SQLite database backups using VACUUM INTO.
/// Automatically prunes old backups beyond the configured retention count.
/// </summary>
public class AutomatedBackupService : BackgroundService
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly BackupSettings _settings;
    private readonly IServiceHealthTracker _healthTracker;
    private readonly ILogger<AutomatedBackupService> _logger;

    public AutomatedBackupService(
        SqliteConnectionFactory connectionFactory,
        IOptions<BackupSettings> settings,
        IServiceHealthTracker healthTracker,
        ILogger<AutomatedBackupService> logger)
    {
        _connectionFactory = connectionFactory;
        _settings = settings.Value;
        _healthTracker = healthTracker;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.AutoBackupEnabled)
        {
            _logger.LogInformation("AutomatedBackupService is disabled (Backup:AutoBackupEnabled=false)");
            return;
        }

        _logger.LogInformation(
            "AutomatedBackupService started (interval={Interval}h, retention={Retention})",
            _settings.BackupIntervalHours, _settings.MaxBackupsToKeep);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                CreateBackup();
                PruneOldBackups();
                _healthTracker.RecordSuccess("AutomatedBackupService");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _healthTracker.RecordFailure("AutomatedBackupService", ex.Message);
                _logger.LogError(ex, "AutomatedBackupService failed to create backup");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(_settings.BackupIntervalHours), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("AutomatedBackupService stopped");
    }

    private void CreateBackup()
    {
        var backupDir = Path.GetFullPath(_settings.BackupDirectory);

        if (!Directory.Exists(backupDir))
        {
            Directory.CreateDirectory(backupDir);
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var backupFileName = $"lightningagent_auto_{timestamp}.db";
        var backupFilePath = Path.Combine(backupDir, backupFileName);

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "VACUUM INTO @backupPath";
        cmd.Parameters.AddWithValue("@backupPath", backupFilePath);
        cmd.ExecuteNonQuery();

        var fileInfo = new FileInfo(backupFilePath);

        _logger.LogInformation(
            "Automated backup created: {BackupPath} ({Size} bytes)",
            backupFilePath, fileInfo.Length);
    }

    private void PruneOldBackups()
    {
        var backupDir = Path.GetFullPath(_settings.BackupDirectory);

        if (!Directory.Exists(backupDir))
            return;

        // Only prune auto-backups (not manual ones)
        var autoBackups = Directory.GetFiles(backupDir, "lightningagent_auto_*.db")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTimeUtc)
            .ToList();

        if (autoBackups.Count <= _settings.MaxBackupsToKeep)
            return;

        var toDelete = autoBackups.Skip(_settings.MaxBackupsToKeep).ToList();

        foreach (var file in toDelete)
        {
            try
            {
                file.Delete();
                _logger.LogInformation("Pruned old backup: {FileName}", file.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to prune backup: {FileName}", file.Name);
            }
        }
    }
}
