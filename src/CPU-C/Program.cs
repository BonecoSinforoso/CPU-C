using System.Runtime.InteropServices;
using CpuC.Core;
using CpuC.Localization;
using CpuC.Ui;

namespace CpuC;

internal static class Program
{
    private const string MutexName = "CPU-C.SingleInstance";
    private static Mutex? _instance;

    [STAThread]
    private static int Main(string[] args)
    {
        args = TakeLanguageOption(args);
        if (args.Length > 0 && args[0] is "--status" or "--apply" or "--restore" or "--help")
            return RunCli(args);

        ApplicationConfiguration.Initialize();

        // Internal use: renders the window image for the docs, without changing anything.
        if (args.Length == 2 && args[0] == "--screenshot")
        {
            using MainForm form = new(askAboutLeftover: false);
            form.SaveScreenshot(args[1]);
            return 0;
        }

        if (!AcquireInstance())
        {
            MessageBox.Show(Lang.T.AlreadyRunning, "CPU-C",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        try
        {
            Application.Run(new MainForm(askAboutLeftover: !args.Contains("--keep")));
        }
        finally
        {
            ReleaseInstance();
        }
        return 0;
    }

    public static bool AcquireInstance()
    {
        // When restarting as administrator the old instance may still be closing.
        for (int attempt = 0; attempt < 20; attempt++)
        {
            Mutex mutex = new(true, MutexName, out bool created);
            if (created)
            {
                _instance = mutex;
                return true;
            }
            mutex.Dispose();
            Thread.Sleep(150);
        }
        return false;
    }

    public static void ReleaseInstance()
    {
        try { _instance?.ReleaseMutex(); } catch { }
        _instance?.Dispose();
        _instance = null;
    }

    private static int RunCli(string[] args)
    {
        AttachConsole(-1);
        try
        {
            CpuTopology topology = CpuTopology.Detect();
            Limiter limiter = new(topology);
            limiter.Log += Console.WriteLine;

            switch (args[0])
            {
                case "--apply":
                    if (args.Length < 2)
                    {
                        Console.WriteLine(Lang.T.CliGiveProfileName);
                        return 2;
                    }
                    ProfileStore store = ProfileStore.Load(topology);
                    Profile? profile = store.Find(args[1]);
                    if (profile is null)
                    {
                        Console.WriteLine(Lang.T.CliProfileNotFound(args[1], string.Join(", ", store.Profiles.Select(p => p.Name))));
                        return 2;
                    }
                    limiter.Apply(profile);
                    Console.WriteLine(Lang.T.CliLimitStaysActive);
                    return 0;

                case "--restore":
                    if (!limiter.IsActive) Console.WriteLine(Lang.T.CliNoActiveLimit);
                    limiter.Restore();
                    return 0;

                case "--status":
                    (int min, int max) = PowerPlan.Current();
                    Console.WriteLine(Lang.T.PowerPlanNow(min, max));
                    Console.WriteLine(limiter.IsActive
                        ? Lang.T.CliLimitActive(limiter.ActiveProfile!.Name)
                        : Lang.T.CliNoActiveLimit);
                    string kinds = topology.IsHybrid
                        ? $" ({topology.Cpus.Count(c => c.Kind == CoreKind.Performance)} P, {topology.Cpus.Count(c => c.Kind == CoreKind.Efficiency)} E)"
                        : "";
                    Console.WriteLine(Lang.T.CliLogicalCpus(topology.Cpus.Count, kinds));
                    return 0;

                default:
                    Console.WriteLine(Lang.T.CliUsage);
                    return 0;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(Lang.T.CliError(ex.Message));
            return 1;
        }
    }

    /// <summary>
    /// "--lang en" or "--lang pt" picks the language for this run only, without saving it.
    /// Returns the remaining arguments.
    /// </summary>
    private static string[] TakeLanguageOption(string[] args)
    {
        int index = Array.IndexOf(args, "--lang");
        if (index < 0 || index + 1 >= args.Length) return args;
        if (Lang.Find(args[index + 1]) is { } language) Lang.Set(language, save: false);
        return [.. args[..index], .. args[(index + 2)..]];
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
