using System.Globalization;
using CpuC.Core;

namespace CpuC.Localization;

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
        // Not being able to remember the language is not worth an error message.
        AppSettings.Update(s => s.Language = language.Code);
    }

    private static Strings Initial()
    {
        if (Find(AppSettings.Load().Language) is { } saved) return saved;
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt" ? Strings.Portuguese : Strings.English;
    }
}
