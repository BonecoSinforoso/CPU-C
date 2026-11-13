using System.Runtime.InteropServices;

namespace CpuC.Core;

public enum CoreKind { Normal, Performance, Efficiency }

/// <summary>One logical CPU (what Task Manager shows as "CPU 0", "CPU 1"...).</summary>
public sealed record LogicalCpu(int Index, int PhysicalCore, CoreKind Kind)
{
    public string KindLabel => Kind switch
    {
        CoreKind.Performance => "P",
        CoreKind.Efficiency => "E",
        _ => "",
    };
}

/// <summary>The machine's cores, split into P-cores and E-cores when the CPU is hybrid.</summary>
public sealed class CpuTopology
{
    public IReadOnlyList<LogicalCpu> Cpus { get; }
    public bool IsHybrid { get; }
    public long AllMask { get; }

    private CpuTopology(List<LogicalCpu> cpus)
    {
        Cpus = cpus;
        IsHybrid = cpus.Any(c => c.Kind == CoreKind.Efficiency);
        AllMask = MaskOf(cpus.Select(c => c.Index));
    }

    public static long MaskOf(IEnumerable<int> indexes)
    {
        long mask = 0;
        foreach (var i in indexes)
            if (i is >= 0 and < 64) mask |= 1L << i;
        return mask;
    }

    /// <summary>Affinity mask for the requested cores; 0 when there is no restriction.</summary>
    public long MaskFor(IEnumerable<int> cores)
    {
        long mask = MaskOf(cores) & AllMask;
        return mask == AllMask ? 0 : mask;
    }

    public static CpuTopology Detect()
    {
        try
        {
            var cpus = ReadFromWindows();
            if (cpus.Count > 0) return new CpuTopology(cpus);
        }
        catch
        {
            // Fall back to the simple mode below.
        }
        int count = Math.Min(Environment.ProcessorCount, 64);
        return new CpuTopology(Enumerable.Range(0, count).Select(i => new LogicalCpu(i, i, CoreKind.Normal)).ToList());
    }

    private static List<LogicalCpu> ReadFromWindows()
    {
        const int relationProcessorCore = 0;
        uint length = 0;
        GetLogicalProcessorInformationEx(relationProcessorCore, IntPtr.Zero, ref length);
        if (length == 0) return [];

        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetLogicalProcessorInformationEx(relationProcessorCore, buffer, ref length)) return [];

            // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX: Relationship(4) Size(4) followed by
            // PROCESSOR_RELATIONSHIP: Flags(1) EfficiencyClass(1) Reserved(20) GroupCount(2) GroupMask[]
            var cores = new List<(byte Efficiency, long Mask)>();
            int offset = 0;
            while (offset < length)
            {
                int size = Marshal.ReadInt32(buffer, offset + 4);
                if (size <= 0) break;
                byte efficiency = Marshal.ReadByte(buffer, offset + 9);
                short groupCount = Marshal.ReadInt16(buffer, offset + 30);
                long mask = Marshal.ReadInt64(buffer, offset + 32);
                short group = Marshal.ReadInt16(buffer, offset + 40);
                // Only group 0 is supported (machines with up to 64 logical CPUs).
                if (groupCount >= 1 && group == 0) cores.Add((efficiency, mask));
                offset += size;
            }

            byte top = cores.Count > 0 ? cores.Max(c => c.Efficiency) : (byte)0;
            bool hybrid = cores.Any(c => c.Efficiency != top);

            var cpus = new List<LogicalCpu>();
            int physical = 0;
            foreach (var core in cores.OrderBy(c => LowestBit(c.Mask)))
            {
                var kind = !hybrid ? CoreKind.Normal
                    : core.Efficiency == top ? CoreKind.Performance
                    : CoreKind.Efficiency;
                for (int i = 0; i < 64; i++)
                    if ((core.Mask & (1L << i)) != 0) cpus.Add(new LogicalCpu(i, physical, kind));
                physical++;
            }
            return cpus.OrderBy(c => c.Index).ToList();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int LowestBit(long mask)
    {
        for (int i = 0; i < 64; i++)
            if ((mask & (1L << i)) != 0) return i;
        return 64;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformationEx(int relationshipType, IntPtr buffer, ref uint returnedLength);
}
