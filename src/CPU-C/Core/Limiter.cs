using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace CpuC.Core;

/// <summary>What is kept on disk while a limit is active, so it can be restored.</summary>
public sealed class ActiveState
{
    public PowerSnapshot Original { get; set; } = new();
    public Profile Profile { get; set; } = new();
}

/// <summary>Applies and restores a profile: speed limit in the power plan and core pinning of programs.</summary>
public sealed class Limiter
{
    private readonly CpuTopology _topology;
    private readonly HashSet<int> _failedPids = [];
    private ActiveState? _state;

    public event Action<string>? Log;

    public bool IsActive => _state is not null;
    public Profile? ActiveProfile => _state?.Profile;

    public Limiter(CpuTopology topology)
    {
        _topology = topology;
        _state = LoadState();
    }

    public void Apply(Profile profile)
    {
        profile = profile.Clone();
        profile.Normalize();

        // If a limit was already active, the original values are still the ones from the first time.
        var original = _state?.Original ?? PowerPlan.Capture();
        if (_state is not null) ReleaseAffinity(_state.Profile.Processes);

        // Saved before touching anything: if the app dies halfway, it can still restore.
        _state = new ActiveState { Original = original, Profile = profile };
        File.WriteAllText(AppPaths.StateFile, JsonSerializer.Serialize(_state, AppPaths.Json));

        PowerPlan.SetThrottle(profile.MinPercent, profile.MaxPercent);
        Log?.Invoke($"CPU limited: minimum {profile.MinPercent}%, maximum {profile.MaxPercent}%.");

        _failedPids.Clear();
        long mask = _topology.MaskFor(profile.Cores);
        if (mask == 0)
        {
            Log?.Invoke("Cores: no restriction.");
        }
        else if (profile.Processes.Count == 0)
        {
            Log?.Invoke("Cores: no programs in the list, nothing to pin.");
        }
        else
        {
            int found = ApplyAffinity();
            if (found == 0) Log?.Invoke("None of the listed programs is running. Cores will be pinned when they start.");
        }
    }

    public void Restore()
    {
        if (_state is null) return;
        var state = _state;

        PowerPlan.Restore(state.Original);
        var (min, max) = PowerPlan.Current();
        Log?.Invoke($"CPU restored: minimum {min}%, maximum {max}%.");

        ReleaseAffinity(state.Profile.Processes);
        _state = null;
        _failedPids.Clear();
        try { File.Delete(AppPaths.StateFile); } catch { }
    }

    /// <summary>Called periodically: pins the cores of programs that started afterwards.</summary>
    public void Tick()
    {
        if (_state is not null) ApplyAffinity();
    }

    private int ApplyAffinity()
    {
        if (_state is null) return 0;
        long mask = _topology.MaskFor(_state.Profile.Cores);
        if (mask == 0) return 0;
        return SetAffinity(_state.Profile.Processes, mask, "cores pinned");
    }

    private void ReleaseAffinity(IEnumerable<string> processes)
    {
        SetAffinity(processes, _topology.AllMask, "cores released");
    }

    private int SetAffinity(IEnumerable<string> processes, long mask, string what)
    {
        int found = 0;
        foreach (var name in processes)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    found++;
                    try
                    {
                        if ((long)process.ProcessorAffinity == mask) continue;
                        process.ProcessorAffinity = (IntPtr)mask;
                        _failedPids.Remove(process.Id);
                        Log?.Invoke($"{process.ProcessName} (PID {process.Id}): {what}.");
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                    {
                        // The process already exited, or belongs to another user / runs elevated.
                        if (process.HasExitedSafe() || !_failedPids.Add(process.Id)) continue;
                        Log?.Invoke($"{name} (PID {process.Id}): failed ({ex.Message.Trim()}). Try running CPU-C as administrator.");
                    }
                }
            }
        }
        return found;
    }

    private static ActiveState? LoadState()
    {
        try
        {
            if (!File.Exists(AppPaths.StateFile)) return null;
            var state = JsonSerializer.Deserialize<ActiveState>(File.ReadAllText(AppPaths.StateFile), AppPaths.Json);
            return state is { Original.Values.Count: > 0 } ? state : null;
        }
        catch
        {
            return null;
        }
    }
}

internal static class ProcessExtensions
{
    public static bool HasExitedSafe(this Process process)
    {
        try { return process.HasExited; }
        catch { return false; }
    }
}
