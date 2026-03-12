namespace LightningAgent.Core.Configuration;

public class BackupSettings
{
    public string BackupDirectory { get; set; } = "./backups/";
    public bool AutoBackupEnabled { get; set; } = true;
    public int BackupIntervalHours { get; set; } = 24;
    public int MaxBackupsToKeep { get; set; } = 7;
}
