using System.Runtime.InteropServices;
using CpuC.Core;
using CpuC.Ui;

namespace CpuC;

internal static class Program
{
    private const string MutexName = "CPU-C.SingleInstance";
    private static Mutex? _instance;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] is "--status" or "--apply" or "--restore" or "--help")
            return RunCli(args);

        ApplicationConfiguration.Initialize();

        // Internal use: renders the window image for the docs, without changing anything.
        if (args.Length == 2 && args[0] == "--screenshot")
        {
            using var form = new MainForm(askAboutLeftover: false);
            form.SaveScreenshot(args[1]);
            return 0;
        }

        if (!AcquireInstance())
        {
            MessageBox.Show("CPU-C is already running. Look for its icon in the system tray.", "CPU-C",
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
            var mutex = new Mutex(true, MutexName, out bool created);
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
            var topology = CpuTopology.Detect();
            var limiter = new Limiter(topology);
            limiter.Log += Console.WriteLine;

            switch (args[0])
            {
                case "--apply":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Give the profile name: CPU-C.exe --apply \"Weak PC\"");
                        return 2;
                    }
                    var store = ProfileStore.Load(topology);
                    var profile = store.Find(args[1]);
                    if (profile is null)
                    {
                        Console.WriteLine($"Profile \"{args[1]}\" not found. Profiles: {string.Join(", ", store.Profiles.Select(p => p.Name))}");
                        return 2;
                    }
                    limiter.Apply(profile);
                    Console.WriteLine("The limit stays active until you run: CPU-C.exe --restore");
                    return 0;

                case "--restore":
                    if (!limiter.IsActive) Console.WriteLine("No active limit.");
                    limiter.Restore();
                    return 0;

                case "--status":
                    var (min, max) = PowerPlan.Current();
                    Console.WriteLine($"Power plan now: minimum {min}%, maximum {max}%.");
                    Console.WriteLine(limiter.IsActive
                        ? $"Limit active: profile \"{limiter.ActiveProfile!.Name}\"."
                        : "No active limit.");
                    var kinds = topology.IsHybrid
                        ? $" ({topology.Cpus.Count(c => c.Kind == CoreKind.Performance)} P, {topology.Cpus.Count(c => c.Kind == CoreKind.Efficiency)} E)"
                        : "";
                    Console.WriteLine($"Logical CPUs: {topology.Cpus.Count}{kinds}.");
                    return 0;

                default:
                    Console.WriteLine("Usage: CPU-C.exe [--apply \"profile\" | --restore | --status]");
                    Console.WriteLine("With no arguments, opens the window.");
                    return 0;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
            return 1;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
