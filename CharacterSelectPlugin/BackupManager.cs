using System;
using System.IO;
using Newtonsoft.Json;
using System.Linq;

namespace CharacterSelectPlugin.Managers
{
    public static class BackupManager
    {
        private static string BackupDirectory => Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "Backups");
        private static string ConfigBackupPath => Path.Combine(BackupDirectory, "characterselectplugin_backup.json");
        private static string VersionFilePath => Path.Combine(BackupDirectory, "last_backup_version.txt");

        // Create a backup of the current configuration before updates
        public static void CreateBackupIfNeeded(Configuration config, string currentVersion)
        {
            try
            {
                // Ensure backup directory exists
                Directory.CreateDirectory(BackupDirectory);

                // Check if we need to create a backup
                bool shouldBackup = ShouldCreateBackup(currentVersion);

                if (shouldBackup)
                {
                    Plugin.Log.Info($"[Backup] Creating configuration backup for version {currentVersion}");

                    // Backup main config file
                    BackupMainConfig(config);

                    // Clean old backups (keep last 5)
                    CleanOldBackups();

                    // Update version file
                    File.WriteAllText(VersionFilePath, currentVersion);

                    Plugin.Log.Info($"[Backup] Backup completed successfully");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Backup] Failed to create backup: {ex.Message}");
            }
        }

        // Determine if a backup should be created based on version changes
        private static bool ShouldCreateBackup(string currentVersion)
        {
            try
            {
                if (!File.Exists(VersionFilePath))
                {
                    // First time running this version of backup system
                    return true;
                }

                string lastBackupVersion = File.ReadAllText(VersionFilePath).Trim();

                // Create backup if version changed
                if (lastBackupVersion != currentVersion)
                {
                    Plugin.Log.Debug($"[Backup] Version changed from {lastBackupVersion} to {currentVersion}");
                    return true;
                }

                // Also create backup if it's been more than 7 days since last backup
                var backupInfo = new FileInfo(ConfigBackupPath);
                if (backupInfo.Exists && DateTime.Now - backupInfo.LastWriteTime > TimeSpan.FromDays(7))
                {
                    Plugin.Log.Debug("[Backup] Creating periodic backup (7+ days since last)");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning($"[Backup] Error checking backup necessity: {ex.Message}");
                return true; // Errr on the side of caution
            }
        }

        // Back up the main configuration file
        private static void BackupMainConfig(Configuration config)
        {
            try
            {
                // Create timestamped backup
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string timestampedBackup = Path.Combine(BackupDirectory, $"config_backup_{timestamp}.json");

                // Serialize current config
                string configJson = JsonConvert.SerializeObject(config, Formatting.Indented);

                // Save timestamped backup
                File.WriteAllText(timestampedBackup, configJson);

                // Also save as the "current" backup
                File.WriteAllText(ConfigBackupPath, configJson);

                Plugin.Log.Debug($"[Backup] Config backed up to: {timestampedBackup}");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Backup] Failed to backup main config: {ex.Message}");
            }
        }

        // Remove old backup files, keeping only the most recent 5
        private static void CleanOldBackups()
        {
            try
            {
                if (!Directory.Exists(BackupDirectory))
                    return;

                var backupFiles = Directory.GetFiles(BackupDirectory, "config_backup_*.json")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .ToArray();

                // Keep the 5 most recent backups
                var filesToDelete = backupFiles.Skip(5);

                foreach (var file in filesToDelete)
                {
                    try
                    {
                        file.Delete();
                        Plugin.Log.Debug($"[Backup] Cleaned old backup: {file.Name}");
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Warning($"[Backup] Failed to delete old backup {file.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning($"[Backup] Failed to clean old backups: {ex.Message}");
            }
        }

        // Restore configuration from backup
        public static Configuration? RestoreFromBackup()
        {
            try
            {
                if (!File.Exists(ConfigBackupPath))
                {
                    Plugin.Log.Warning("[Backup] No backup file found for restoration");
                    return null;
                }

                string backupJson = File.ReadAllText(ConfigBackupPath);
                var restoredConfig = JsonConvert.DeserializeObject<Configuration>(backupJson);

                if (restoredConfig != null)
                {
                    Plugin.Log.Info("[Backup] Configuration restored from backup successfully");
                    return restoredConfig;
                }
                else
                {
                    Plugin.Log.Error("[Backup] Failed to deserialize backup configuration");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Backup] Failed to restore from backup: {ex.Message}");
                return null;
            }
        }

        // Get information about available backups
        public static BackupInfo GetBackupInfo()
        {
            var info = new BackupInfo();

            try
            {
                info.BackupExists = File.Exists(ConfigBackupPath);

                if (info.BackupExists)
                {
                    var backupFile = new FileInfo(ConfigBackupPath);
                    info.LastBackupDate = backupFile.LastWriteTime;
                }

                if (File.Exists(VersionFilePath))
                {
                    info.LastBackupVersion = File.ReadAllText(VersionFilePath).Trim();
                }

                // Count timestamped backups
                if (Directory.Exists(BackupDirectory))
                {
                    info.BackupCount = Directory.GetFiles(BackupDirectory, "config_backup_*.json").Length;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Backup] Error getting backup info: {ex.Message}");
            }

            return info;
        }

        // Create an emergency backup
        public static void CreateEmergencyBackup(Configuration config)
        {
            try
            {
                Directory.CreateDirectory(BackupDirectory);

                string emergencyBackup = Path.Combine(BackupDirectory, $"emergency_backup_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json");
                string configJson = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(emergencyBackup, configJson);

                Plugin.Log.Info($"[Backup] Emergency backup created: {emergencyBackup}");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Backup] Failed to create emergency backup: {ex.Message}");
            }
        }
    }

    public class BackupInfo
    {
        public bool BackupExists { get; set; }
        public DateTime? LastBackupDate { get; set; }
        public string? LastBackupVersion { get; set; }
        public int BackupCount { get; set; }
    }
}
