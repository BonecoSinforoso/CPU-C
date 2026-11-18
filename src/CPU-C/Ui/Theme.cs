using CpuC.Core;

namespace CpuC.Ui;

/// <summary>
/// Light/dark theme. Windows Forms picks its colors when windows are created, so the
/// theme is set once at startup; changing it takes effect after a restart.
/// </summary>
internal static class Theme
{
    /// <summary>Theme the running app started with.</summary>
    public static AppTheme Applied { get; private set; } = AppTheme.System;

    /// <summary>Must run before the first window is created.</summary>
    public static void Apply(AppTheme theme)
    {
        Applied = theme;
        bool dark = theme switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => WindowsUsesDarkApps(),
        };
#pragma warning disable WFO5001 // Dark mode in Windows Forms is still marked experimental.
        Application.SetColorMode(dark ? SystemColorMode.Dark : SystemColorMode.Classic);
#pragma warning restore WFO5001
    }

    /// <summary>
    /// The "app mode" chosen in Windows settings. Read directly because
    /// <c>SystemColorMode.System</c> only detects dark mode on Windows 11.
    /// </summary>
    private static bool WindowsUsesDarkApps()
    {
        try
        {
            using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsDark => Application.IsDarkModeEnabled;

    // Fixed colors used by the app, readable on both backgrounds.
    public static Color Warning => IsDark ? Color.FromArgb(255, 180, 90) : Color.FromArgb(176, 92, 0);
    public static Color StatusLimited => IsDark ? Color.FromArgb(255, 145, 70) : Color.FromArgb(190, 80, 10);
    public static Color StatusNormal => IsDark ? Color.FromArgb(100, 210, 140) : Color.FromArgb(30, 120, 70);
}
