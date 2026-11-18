using System.Text.Encodings.Web;
using System.Text.Json;
using CpuC.Localization;

namespace CpuC.Core;

/// <summary>A limit profile: CPU speed, allowed cores and the programs affected.</summary>
public sealed class Profile
{
    public string Name { get; set; } = Lang.T.NewProfileName;
    public int MinPercent { get; set; } = 5;
    public int MaxPercent { get; set; } = 50;

    /// <summary>Logical CPUs the programs may use. Empty = no core restriction.</summary>
    public List<int> Cores { get; set; } = [];

    /// <summary>Process names, without ".exe".</summary>
    public List<string> Processes { get; set; } = [];

    public Profile Clone() => new()
    {
        Name = Name,
        MinPercent = MinPercent,
        MaxPercent = MaxPercent,
        Cores = [.. Cores],
        Processes = [.. Processes],
    };

    /// <summary>Fixes out-of-range values and keeps the minimum at or below the maximum.</summary>
    public void Normalize()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? Lang.T.FallbackProfileName : Name.Trim();
        MaxPercent = Math.Clamp(MaxPercent, 1, 100);
        MinPercent = Math.Clamp(MinPercent, 0, MaxPercent);
        Cores = Cores.Where(c => c is >= 0 and < 64).Distinct().Order().ToList();
        Processes = Processes.Select(CleanProcessName)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string CleanProcessName(string name)
    {
        name = (name ?? "").Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}

public static class AppPaths
{
    public static string DataDir
    {
        get
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CPU-C");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ProfilesFile => Path.Combine(DataDir, "profiles.json");
    public static string StateFile => Path.Combine(DataDir, "state.json");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}

/// <summary>Profiles saved in %AppData%\CPU-C\profiles.json.</summary>
public sealed class ProfileStore
{
    public List<Profile> Profiles { get; private set; } = [];

    public static ProfileStore Load(CpuTopology topology)
    {
        ProfileStore store = new();
        try
        {
            if (File.Exists(AppPaths.ProfilesFile))
                store.Profiles = ReadFile(AppPaths.ProfilesFile);
        }
        catch
        {
            // Corrupt file: keep a copy and start over with the default profiles.
            try { File.Copy(AppPaths.ProfilesFile, AppPaths.ProfilesFile + ".bak", true); } catch { }
        }

        if (store.Profiles.Count == 0)
        {
            store.Profiles = Defaults(topology);
            store.Save();
        }
        return store;
    }

    public void Save()
    {
        File.WriteAllText(AppPaths.ProfilesFile, JsonSerializer.Serialize(Profiles, AppPaths.Json));
    }

    public Profile? Find(string name) =>
        Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public string UniqueName(string wanted)
    {
        wanted = string.IsNullOrWhiteSpace(wanted) ? Lang.T.FallbackProfileName : wanted.Trim();
        if (Find(wanted) is null) return wanted;
        for (int i = 2; ; i++)
        {
            string candidate = $"{wanted} ({i})";
            if (Find(candidate) is null) return candidate;
        }
    }

    /// <summary>Imports a file holding one profile or a list of profiles. Returns the profiles added.</summary>
    public List<Profile> Import(string file)
    {
        List<Profile> imported = ReadFile(file);
        foreach (Profile profile in imported)
        {
            profile.Name = UniqueName(profile.Name);
            Profiles.Add(profile);
        }
        if (imported.Count > 0) Save();
        return imported;
    }

    public static void Export(Profile profile, string file)
    {
        File.WriteAllText(file, JsonSerializer.Serialize(profile, AppPaths.Json));
    }

    private static List<Profile> ReadFile(string file)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        List<Profile> list = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.Deserialize<List<Profile>>(AppPaths.Json) ?? []
            : [doc.RootElement.Deserialize<Profile>(AppPaths.Json) ?? throw new InvalidDataException(Lang.T.EmptyProfileFile)];

        list.RemoveAll(p => p is null);
        foreach (Profile profile in list) profile.Normalize();
        return list;
    }

    private static List<Profile> Defaults(CpuTopology topology)
    {
        // On hybrid CPUs the default profiles use the slow cores (E-cores).
        List<int> pool = topology.IsHybrid
            ? topology.Cpus.Where(c => c.Kind == CoreKind.Efficiency).Select(c => c.Index).ToList()
            : topology.Cpus.Select(c => c.Index).ToList();

        return
        [
            new Profile { Name = Lang.T.DefaultWeakPc, MinPercent = 5, MaxPercent = 40, Cores = pool.Take(4).ToList() },
            new Profile { Name = Lang.T.DefaultVeryWeakPc, MinPercent = 5, MaxPercent = 20, Cores = pool.Take(2).ToList() },
        ];
    }
}
