namespace CpuC.Localization;

/// <summary>
/// Every text the app shows, for one language. All members are <c>required</c>, so a
/// language missing a translation does not compile. Texts with values are functions.
/// </summary>
public sealed partial class Strings
{
    /// <summary>Code saved in settings.json ("en", "pt").</summary>
    public required string Code { get; init; }

    /// <summary>Language name written in the language itself, shown in the selector.</summary>
    public required string NativeName { get; init; }

    public override string ToString() => NativeName;

    // ---------- Profiles (Core) ----------
    public required string NewProfileName { get; init; }
    public required string FallbackProfileName { get; init; }
    public required string UnnamedProfile { get; init; }
    public required string DefaultWeakPc { get; init; }
    public required string DefaultVeryWeakPc { get; init; }
    public required string EmptyProfileFile { get; init; }

    // ---------- Limiter log (Core) ----------
    public required Func<int, int, string> CpuLimited { get; init; }
    public required Func<int, int, string> CpuRestored { get; init; }
    public required string CoresNoRestriction { get; init; }
    public required string CoresNoPrograms { get; init; }
    public required string NoListedProgramRunning { get; init; }
    public required Func<string, int, string> CoresPinned { get; init; }
    public required Func<string, int, string> CoresReleased { get; init; }
    public required Func<string, int, string, string> AffinityFailed { get; init; }

    // ---------- Power plan (Core) ----------
    public required string ActionReadPlan { get; init; }
    public required string ActionChangePlan { get; init; }
    public required string ActionRestorePlan { get; init; }
    public required string ActionActivatePlan { get; init; }
    public required Func<string, string, uint, string> CouldNot { get; init; }
    public required string NoProcessorSettings { get; init; }
    public required Func<int, int, string> PowerPlanNow { get; init; }

    // ---------- Command line ----------
    public required string AlreadyRunning { get; init; }
    public required string CliGiveProfileName { get; init; }
    public required Func<string, string, string> CliProfileNotFound { get; init; }
    public required string CliLimitStaysActive { get; init; }
    public required string CliNoActiveLimit { get; init; }
    public required Func<string, string> CliLimitActive { get; init; }
    public required Func<int, string, string> CliLogicalCpus { get; init; }
    public required string CliUsage { get; init; }
    public required Func<string, string> CliError { get; init; }

    // ---------- Common buttons ----------
    public required string Ok { get; init; }
    public required string Cancel { get; init; }
    public required string Add { get; init; }

    // ---------- Running programs dialog ----------
    public required string RunningProgramsTitle { get; init; }
    public required string FilterPlaceholder { get; init; }

    // ---------- Main window ----------
    public required string WindowTitle { get; init; }
    public required string LanguageLabel { get; init; }
    public required string ProfileLabel { get; init; }
    public required string New { get; init; }
    public required string Save { get; init; }
    public required string Delete { get; init; }
    public required string Import { get; init; }
    public required string Export { get; init; }

    public required string SpeedGroup { get; init; }
    public required string Maximum { get; init; }
    public required string Minimum { get; init; }
    public required Func<int, string> LowLimitWarning { get; init; }

    public required string CoresGroup { get; init; }
    public required string CoreKindFast { get; init; }
    public required string CoreKindSlow { get; init; }
    public required string CoreKindNormal { get; init; }
    public required Func<int, string, int, string> CoreTooltip { get; init; }
    public required string AllCores { get; init; }
    public required string OnlyPCores { get; init; }
    public required string OnlyECores { get; init; }
    public required string NoCores { get; init; }
    public required string CoresHint { get; init; }

    public required string ProgramsGroup { get; init; }
    public required string AddRunning { get; init; }
    public required string AddExe { get; init; }
    public required string TypeName { get; init; }
    public required string Remove { get; init; }

    public required string ApplyLimit { get; init; }
    public required string ReapplyLimit { get; init; }
    public required string RestoreNormal { get; init; }
    public required string RestartAsAdmin { get; init; }

    public required Func<string, string, string> StatusLimitActive { get; init; }
    public required Func<string, string> StatusNormal { get; init; }
    public required Func<string, string> PowerPlanReadFailed { get; init; }

    // ---------- Settings ----------
    public required string SettingsButton { get; init; }
    public required string SettingsTitle { get; init; }
    public required string AppearanceGroup { get; init; }
    public required string ThemeLabel { get; init; }
    public required string ThemeSystem { get; init; }
    public required string ThemeLight { get; init; }
    public required string ThemeDark { get; init; }
    public required string ThemeRestartQuestion { get; init; }
    public required string SettingsSaveFailed { get; init; }

    // ---------- Tray ----------
    public required string TrayOpen { get; init; }
    public required Func<string, string> TrayApply { get; init; }
    public required string TrayExit { get; init; }
    public required Func<string, int, string> TrayLimitActive { get; init; }
    public required string TrayNormal { get; init; }

    // ---------- Profile actions ----------
    public required string NewProfileTitle { get; init; }
    public required string NewProfilePrompt { get; init; }
    public required Func<string, string> ProfileCreated { get; init; }
    public required Func<string, string> ProfileSaved { get; init; }
    public required Func<string, string> DeleteProfileQuestion { get; init; }
    public required Func<string, string> ProfileDeleted { get; init; }
    public required string ProfilesFilterWithAll { get; init; }
    public required string ProfilesFilter { get; init; }
    public required string ImportTitle { get; init; }
    public required string ImportEmpty { get; init; }
    public required Func<string, string> Imported { get; init; }
    public required string ImportFailed { get; init; }
    public required string ExportTitle { get; init; }
    public required Func<string, string> Exported { get; init; }
    public required string ExportFailed { get; init; }
    public required string SaveProfilesFailed { get; init; }

    // ---------- Program actions ----------
    public required string ExeFilter { get; init; }
    public required string ChooseProgramTitle { get; init; }
    public required string AddProgramTitle { get; init; }
    public required string AddProgramPrompt { get; init; }

    // ---------- Apply / restore ----------
    public required string ApplyFailed { get; init; }
    public required string RestoreFailed { get; init; }
    public required string RestoreFailedStillActive { get; init; }
    public required Func<string, string> WatchError { get; init; }
    public required Func<string, string> LeftoverQuestion { get; init; }
}
