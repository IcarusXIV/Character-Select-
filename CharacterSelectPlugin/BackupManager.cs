using System;
using System.IO;
using Newtonsoft.Json;
using System.Linq;
using System.Collections.Generic;

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

                // Serialize current config with type information
                var settings = new JsonSerializerSettings 
                {
                    TypeNameHandling = TypeNameHandling.Objects,
                    Formatting = Formatting.Indented
                };
                string configJson = JsonConvert.SerializeObject(config, settings);

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

        // Remove old backup files, keeping only the most recent 5 AUTOMATIC backups
        // Manual and emergency backups are never automatically cleaned
        private static void CleanOldBackups()
        {
            try
            {
                if (!Directory.Exists(BackupDirectory))
                    return;

                // Only clean automatic backups (config_backup_*.json)
                // Exclude manual_backup_* and emergency_* backups from cleanup
                var automaticBackupFiles = Directory.GetFiles(BackupDirectory, "config_backup_*.json")
                    .Where(f => !Path.GetFileName(f).Contains("manual_backup_") && !Path.GetFileName(f).Contains("emergency_"))
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .ToArray();

                // Keep the 5 most recent automatic backups
                var filesToDelete = automaticBackupFiles.Skip(5);

                foreach (var file in filesToDelete)
                {
                    try
                    {
                        file.Delete();
                        Plugin.Log.Debug($"[Backup] Cleaned old automatic backup: {file.Name}");
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Warning($"[Backup] Failed to delete old automatic backup {file.Name}: {ex.Message}");
                    }
                }

                Plugin.Log.Debug($"[Backup] Cleanup complete. Kept {Math.Min(5, automaticBackupFiles.Length)} automatic backups. Manual/emergency backups preserved.");
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
                var settings = new JsonSerializerSettings 
                {
                    TypeNameHandling = TypeNameHandling.Objects,
                    Formatting = Formatting.Indented
                };
                string configJson = JsonConvert.SerializeObject(config, settings);
                File.WriteAllText(emergencyBackup, configJson);

                Plugin.Log.Info($"[Backup] Emergency backup created: {emergencyBackup}");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Backup] Failed to create emergency backup: {ex.Message}");
            }
        }

        // Create a manual backup with custom name
        public static string? CreateManualBackup(Configuration config, string? customName = null)
        {
            try
            {
                Directory.CreateDirectory(BackupDirectory);

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string filename = string.IsNullOrWhiteSpace(customName) 
                    ? $"manual_backup_{timestamp}.json"
                    : $"manual_backup_{customName}_{timestamp}.json";
                
                string backupPath = Path.Combine(BackupDirectory, filename);

                var settings = new JsonSerializerSettings 
                {
                    TypeNameHandling = TypeNameHandling.Objects,
                    Formatting = Formatting.Indented
                };
                string configJson = JsonConvert.SerializeObject(config, settings);
                File.WriteAllText(backupPath, configJson);

                Plugin.Log.Info($"[Backup] Manual backup created: {backupPath}");
                return backupPath;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Backup] Failed to create manual backup: {ex.Message}");
                return null;
            }
        }

        // Export configuration to a specific file path
        public static bool ExportConfiguration(Configuration config, string filePath)
        {
            try
            {
                var settings = new JsonSerializerSettings 
                {
                    TypeNameHandling = TypeNameHandling.Objects,
                    Formatting = Formatting.Indented
                };
                string configJson = JsonConvert.SerializeObject(config, settings);
                File.WriteAllText(filePath, configJson);

                Plugin.Log.Info($"[Backup] Configuration exported to: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Backup] Failed to export configuration: {ex.Message}");
                return false;
            }
        }

        // Import configuration from a file path
        public static Configuration? ImportConfiguration(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Plugin.Log.Warning($"[Backup] Import file not found: {filePath}");
                    return null;
                }

                string configJson = File.ReadAllText(filePath);
                var settings = new JsonSerializerSettings 
                {
                    TypeNameHandling = TypeNameHandling.Objects
                };
                var importedConfig = JsonConvert.DeserializeObject<Configuration>(configJson, settings);

                if (importedConfig != null)
                {
                    Plugin.Log.Info($"[Backup] Configuration imported from: {filePath}");
                    return importedConfig;
                }
                else
                {
                    Plugin.Log.Error($"[Backup] Failed to deserialize imported configuration from: {filePath}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Backup] Failed to import configuration from {filePath}: {ex.Message}");
                return null;
            }
        }

        // Get list of available backup files
        public static List<BackupFileInfo> GetAvailableBackups()
        {
            var backups = new List<BackupFileInfo>();

            try
            {
                if (!Directory.Exists(BackupDirectory))
                    return backups;

                var backupFiles = Directory.GetFiles(BackupDirectory, "*.json")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .ToArray();

                foreach (var file in backupFiles)
                {
                    var backupInfo = new BackupFileInfo
                    {
                        FileName = file.Name,
                        FilePath = file.FullName,
                        CreatedDate = file.LastWriteTime,
                        FileSize = file.Length,
                        IsManual = file.Name.Contains("manual_backup_") || file.Name.Contains("emergency_")
                    };
                    backups.Add(backupInfo);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Backup] Error getting available backups: {ex.Message}");
            }

            return backups;
        }
    }

    public class BackupInfo
    {
        public bool BackupExists { get; set; }
        public DateTime? LastBackupDate { get; set; }
        public string? LastBackupVersion { get; set; }
        public int BackupCount { get; set; }
    }

    public class BackupFileInfo
    {
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public DateTime CreatedDate { get; set; }
        public long FileSize { get; set; }
        public bool IsManual { get; set; }

        public string GetDisplayName()
        {
            return $"{FileName} ({CreatedDate:yyyy-MM-dd HH:mm}) - {GetFileSizeString()}";
        }

        public string GetFileSizeString()
        {
            if (FileSize < 1024)
                return $"{FileSize} B";
            else if (FileSize < 1024 * 1024)
                return $"{FileSize / 1024:F1} KB";
            else
                return $"{FileSize / (1024 * 1024):F1} MB";
        }
    }
}
