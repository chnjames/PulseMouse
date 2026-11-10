using System.Text.Json;
using PulseMouse.Core;

namespace PulseMouse.Infrastructure;

public sealed class SettingsStore
{
    public string FilePath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PulseMouse", "settings.json");

    public string BackupPath => Path.Combine(Path.GetDirectoryName(FilePath) ?? "", "settings.v8.bak.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return SeedDefaults(new AppSettings());
            string json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            if (settings.SchemaVersion < AppSettings.CurrentSchemaVersion)
            {
                // Backup the on-disk bytes before mutating, so a failed migrate leaves the original file intact.
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.Copy(FilePath, BackupPath, overwrite: true);
                try
                {
                    SettingsMigrator.MigrateFromV8(settings);
                    Save(settings);
                }
                catch
                {
                    // Keep the original file; caller still gets an in-memory migrated copy if Migrate succeeded partially.
                    // Re-throw after restoring? Spec: on failure keep original file untouched — we already wrote backup;
                    // only Save would overwrite. If Migrate throws before Save, original remains.
                    throw;
                }
            }
            else
            {
                SettingsMigrator.Migrate(settings);
            }
            return SeedDefaults(settings);
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException("无法读取配置，请检查 " + FilePath, ex);
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, overwrite: true);
    }

    private static AppSettings SeedDefaults(AppSettings settings)
    {
        foreach (var command in settings.Commands)
        {
            if (string.IsNullOrEmpty(command.Id)) command.Id = Guid.NewGuid().ToString("N");
            PinPolicy.Apply(command);
        }
        return settings;
    }
}
