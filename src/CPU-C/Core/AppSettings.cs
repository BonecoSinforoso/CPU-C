using System.Text.Json;

namespace CpuC.Core;

/// <summary>Color theme of the interface.</summary>
public enum AppTheme
{
    /// <summary>Follows the Windows light/dark setting.</summary>
    System,
    Light,
    Dark,
}

/// <summary>Preferences saved in %AppData%\CPU-C\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Language code ("en", "pt"). Empty = follow the Windows language.</summary>
    public string Language { get; set; } = "";

    /// <summary>Interface theme. Unknown values in the file fall back to <see cref="AppTheme.System"/>.</summary>
    public string Theme { get; set; } = "";

    public AppTheme ThemeChoice
    {
        get => Enum.TryParse(Theme, ignoreCase: true, out AppTheme theme) ? theme : AppTheme.System;
        set => Theme = value.ToString().ToLowerInvariant();
    }

    /// <summary>Reads settings.json; a missing or broken file gives the defaults.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile)
                && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), AppPaths.Json) is { } saved)
                return saved;
        }
        catch
        {
            // A broken file just means the defaults.
        }
        return new AppSettings();
    }

    /// <summary>Reads, changes and writes settings.json. Returns false if it could not be written.</summary>
    public static bool Update(Action<AppSettings> change)
    {
        try
        {
            AppSettings settings = Load();
            change(settings);
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(settings, AppPaths.Json));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
