using System;
using System.IO;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn;

/// <summary>
/// Loads and migrates configuration as one guarded transaction.
///
/// A migration must never make the plugin unloadable. If deserialization or migration throws,
/// preserve the raw Dalamud config file when available, report the failure in English logs and a
/// Japanese notification, then start from a normalized safe default configuration.
/// </summary>
public static class ConfigRecovery
{
    public static Configuration Load(IDalamudPluginInterface pluginInterface)
    {
        Configuration? loaded = null;
        try
        {
            loaded = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            if (ConfigMigration.Apply(loaded))
                ConfigSaver.Save(loaded);
            return loaded;
        }
        catch (Exception ex)
        {
            var backup = TryBackupOriginal(pluginInterface, loaded);
            Svc.Log.Error(ex,
                $"[BozjaBuddyReborn] Configuration load/migration failed. " +
                $"Backup={(backup ?? "unavailable")}. Falling back to safe defaults.");

            NotifyRecovery(backup);

            var fallback = new Configuration();
            try
            {
                ConfigMigration.Apply(fallback);
            }
            catch (Exception normalizeEx)
            {
                // Current defaults should already be valid. A second failure is logged, but the
                // plugin still receives the plain constructor defaults rather than failing load.
                Svc.Log.Error(normalizeEx,
                    "[BozjaBuddyReborn] Normalising fallback configuration also failed; using constructor defaults.");
            }

            ConfigSaver.Save(fallback);
            return fallback;
        }
    }

    private static string? TryBackupOriginal(IDalamudPluginInterface pluginInterface, Configuration? loaded)
    {
        try
        {
            Directory.CreateDirectory(pluginInterface.ConfigDirectory.FullName);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var destination = Path.Combine(
                pluginInterface.ConfigDirectory.FullName,
                $"migration-backup-{stamp}.json");

            if (pluginInterface.ConfigFile.Exists)
            {
                File.Copy(pluginInterface.ConfigFile.FullName, destination, overwrite: false);
                return destination;
            }

            // If Dalamud had no raw file but did deserialize an object before migration failed,
            // preserving a diagnostic copy is still better than losing the pre-fallback state.
            if (loaded != null)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(
                    loaded,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        IncludeFields = true,
                        WriteIndented = true,
                    });
                File.WriteAllText(destination, json);
                return destination;
            }
        }
        catch (Exception backupEx)
        {
            Svc.Log.Warning(backupEx,
                "[BozjaBuddyReborn] Configuration recovery could not create a migration backup.");
        }

        return null;
    }

    private static void NotifyRecovery(string? backup)
    {
        try
        {
            var suffix = backup == null
                ? "元設定のバックアップは作成できませんでした。"
                : "元設定はバックアップ済みです。";
            Svc.NotificationManager.AddNotification(new Notification
            {
                Title = "Bozja Buddy Reborn - 設定復旧",
                Content = $"設定の読み込みまたは移行に失敗したため、安全な初期設定で起動しました。{suffix}",
            });
        }
        catch (Exception notificationEx)
        {
            Svc.Log.Debug($"[BozjaBuddyReborn] Could not show configuration recovery notification: {notificationEx.Message}");
        }
    }
}
