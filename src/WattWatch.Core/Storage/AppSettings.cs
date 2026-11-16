using System.Text.Json;
using System.Text.Json.Serialization;

namespace WattWatch.Core.Storage;

public sealed class AppSettings
{
    public int PollIntervalMinutes { get; set; } = 30;

    public int LowBatteryThreshold { get; set; } = 20;

    public bool StartWithWindows { get; set; }

    public bool NotifyLowBattery { get; set; } = true;

    /// <summary>When true, the window close button hides the window instead of exiting.</summary>
    public bool CloseToTray { get; set; } = true;

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WattWatch");
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;

    public SettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppSettings.DataDirectory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
        }
        catch
        {
            // Settings are not worth crashing a tray app over.
        }
    }
}
