using System.Globalization;
using System.Text.Json;
using CpuC.Core;

namespace CpuC.Localization;

/// <summary>Preferences saved in %AppData%\CPU-C\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Language code ("en", "pt"). Empty = follow the Windows language.</summary>
    public string Language { get; set; } = "";
}

/// <summary>
/// Current interface language. The choice saved in settings.json wins; without one,
/// Portuguese is used when Windows is in Portuguese and English otherwise.
/// </summary>
public static class Lang
{
    public static IReadOnlyList<Strings> All { get; } = [Strings.English, Strings.Portuguese];

    /// <summary>Texts of the current language.</summary>
    public static Strings T { get; private set; } = Initial();

    public static Strings? Find(string? code) =>
        All.FirstOrDefault(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>Changes the language. With <paramref name="save"/>, it is remembered for next time.</summary>
    public static void Set(Strings language, bool save)
    {
        T = language;
        if (!save) return;
        try
        {
            AppSettings settings = LoadSettings() ?? new();
            settings.Language = language.Code;
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(settings, AppPaths.Json));
        }
        catch
        {
            // Not being able to remember the language is not worth an error message.
        }
    }

    private static Strings Initial()
    {
        if (Find(LoadSettings()?.Language) is { } saved) return saved;
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt" ? Strings.Portuguese : Strings.English;
    }

    private static AppSettings? LoadSettings()
    {
        try
        {
            return File.Exists(AppPaths.SettingsFile)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), AppPaths.Json)
                : null;
        }
        catch
        {
            return null;
        }
    }
}
