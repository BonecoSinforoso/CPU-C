using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CpuC.Core;

/// <summary>One power plan setting. Windows keeps two values: AC (plugged in) and DC (on battery).</summary>
public sealed class PowerValue
{
    public Guid Setting { get; set; }
    public uint Ac { get; set; }
    public uint Dc { get; set; }
}

/// <summary>Original power plan values, kept so they can be restored later.</summary>
public sealed class PowerSnapshot
{
    public Guid Scheme { get; set; }
    public List<PowerValue> Values { get; set; } = [];
}

/// <summary>Reads and changes the minimum/maximum processor state of the active power plan.</summary>
public static class PowerPlan
{
    private static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");

    // On hybrid CPUs Windows has one pair of settings per efficiency class
    // (the "1" suffixes). We change every one that exists on the machine.
    private static readonly Guid Min0 = new("893dee8e-2bef-41e0-89c6-b55d0929964c");
    private static readonly Guid Min1 = new("893dee8e-2bef-41e0-89c6-b55d0929964d");
    private static readonly Guid Max0 = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
    private static readonly Guid Max1 = new("bc5038f7-23e0-4960-96da-33abaf5935ed");

    private static readonly Guid[] MinSettings = [Min0, Min1];
    private static readonly Guid[] MaxSettings = [Max0, Max1];

    public static Guid ActiveScheme()
    {
        Check(PowerGetActiveScheme(IntPtr.Zero, out var ptr), "read the active power plan");
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { LocalFree(ptr); }
    }

    /// <summary>Captures the current values of every setting the app changes.</summary>
    public static PowerSnapshot Capture()
    {
        var scheme = ActiveScheme();
        var snapshot = new PowerSnapshot { Scheme = scheme };
        foreach (var setting in MinSettings.Concat(MaxSettings))
        {
            if (TryRead(scheme, setting, out var ac, out var dc))
                snapshot.Values.Add(new PowerValue { Setting = setting, Ac = ac, Dc = dc });
        }
        if (snapshot.Values.Count == 0)
            throw new InvalidOperationException("This power plan does not expose the processor state settings.");
        return snapshot;
    }

    /// <summary>Sets the minimum and maximum state (in %) on the active plan, for both AC and battery.</summary>
    public static void SetThrottle(int minPercent, int maxPercent)
    {
        var scheme = ActiveScheme();
        foreach (var setting in MinSettings) WriteIfPresent(scheme, setting, (uint)minPercent);
        foreach (var setting in MaxSettings) WriteIfPresent(scheme, setting, (uint)maxPercent);
        Activate();
    }

    public static void Restore(PowerSnapshot snapshot)
    {
        var scheme = snapshot.Scheme;
        var sub = SubProcessor;
        foreach (var value in snapshot.Values)
        {
            var setting = value.Setting;
            Check(PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, value.Ac), "restore the power plan");
            Check(PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, value.Dc), "restore the power plan");
        }
        Activate();
    }

    /// <summary>Minimum and maximum state in effect right now, for the current power source.</summary>
    public static (int Min, int Max) Current()
    {
        var scheme = ActiveScheme();
        bool onBattery = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;
        TryRead(scheme, Min0, out var minAc, out var minDc);
        TryRead(scheme, Max0, out var maxAc, out var maxDc);
        return onBattery ? ((int)minDc, (int)maxDc) : ((int)minAc, (int)maxAc);
    }

    // Changes only take effect once the plan is re-activated.
    private static void Activate()
    {
        var scheme = ActiveScheme();
        Check(PowerSetActiveScheme(IntPtr.Zero, ref scheme), "activate the power plan");
    }

    private static bool TryRead(Guid scheme, Guid setting, out uint ac, out uint dc)
    {
        var sub = SubProcessor;
        dc = 0;
        return PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out ac) == 0
            && PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out dc) == 0;
    }

    private static void WriteIfPresent(Guid scheme, Guid setting, uint value)
    {
        if (!TryRead(scheme, setting, out _, out _)) return;
        var sub = SubProcessor;
        Check(PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, value), "change the power plan");
        Check(PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, value), "change the power plan");
    }

    private static void Check(uint result, string action)
    {
        if (result == 0) return;
        var reason = new Win32Exception((int)result).Message;
        throw new InvalidOperationException($"Could not {action}: {reason} (code {result}).");
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid scheme, ref Guid subGroup, ref Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid scheme, ref Guid subGroup, ref Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid scheme, ref Guid subGroup, ref Guid setting, uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid scheme, ref Guid subGroup, ref Guid setting, uint value);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);
}
