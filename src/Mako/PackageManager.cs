using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mako;

/// One package's recorded install state — what's actually on disk right
/// now, not just what a script asked for. Written after every successful
/// clone/checkout; read by Ensure() so a fresh machine (or a re-run after
/// `mko cache clear`) reproduces the exact same commit instead of
/// silently drifting to whatever a branch/tag currently points at.
sealed class LockEntry
{
    public string Url { get; set; } = "";
    public string? Ref { get; set; }           // the ref a script/mko get asked for, if any (tag/branch/SHA)
    public string ResolvedCommit { get; set; } = "";
}

sealed class LockFile
{
    public Dictionary<string, LockEntry> Packages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

static class PackageManager
{
    public static readonly string PackagesDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "mko", "packages");

    private static readonly string LockPath = Path.Combine(PackagesDir, "mako.lock");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Native packages are built into the interpreter — no files to clone.
    public static readonly HashSet<string> NativePackages =
        new(StringComparer.OrdinalIgnoreCase) { "MakoUI", "IMGUI", "MakoRay", "Mako2D", "Mako3D", "Physics2D", "Physics3D", "JoltPhysics", "PhysX", "BulletPhysics", "Box2D", "Inputs", "Audio", "Net", "System" };

    // Public registry: name → clone source (URL, optionally with an
    // unresolved "@ref" suffix still attached — ResolveUrl/ParseRef split
    // it apart at clone time, not at registration time, so RegisterSource
    // stays a dumb string store and all the ref logic lives in one place).
    private static readonly Dictionary<string, string> Registry =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["MakoUI"] = "https://github.com/AnimatedGTVR/MakoUI",
            ["IMGUI"]  = "https://github.com/AnimatedGTVR/MakoUI",
        };

    /// Resolve a source string like "github:User/Repo" to a full clone URL.
    /// Strips a trailing "@ref" first if present — see ParseRef.
    public static string ResolveUrl(string source)
    {
        var (bare, _) = ParseRef(source);
        return bare.StartsWith("github:", StringComparison.OrdinalIgnoreCase)
            ? "https://github.com/" + bare["github:".Length..]
            : bare; // treat anything else as a raw URL
    }

    /// Splits an optional "@ref" pin off the end of a source string:
    /// "github:User/Repo@v1.2.0" -> ("github:User/Repo", "v1.2.0").
    /// ref may be a tag, branch, or commit SHA — git's own checkout
    /// doesn't distinguish between these, so neither do we. A bare
    /// "user@host:path"-style SSH URL would misparse here, but MAKO's
    /// source strings are always "github:User/Repo" or a plain https URL
    /// today, neither of which legitimately contains '@' on its own.
    public static (string Source, string? Ref) ParseRef(string source)
    {
        int at = source.LastIndexOf('@');
        return at > 0 ? (source[..at], source[(at + 1)..]) : (source, null);
    }

    /// Register a package from a 'using Name from "source"' declaration.
    public static void RegisterSource(string name, string source)
    {
        if (!NativePackages.Contains(name))
            Registry[name] = source; // kept raw (with any "@ref" intact) — split at clone time
    }

    /// Ensures the package is locally available, cloning it if needed.
    /// Native packages are a no-op. If a lockfile entry already exists for
    /// this package, that recorded commit is what gets checked out on a
    /// fresh install — not whatever the ref currently resolves to — so a
    /// second machine (or a cache wipe + reinstall) reproduces the exact
    /// same code, not just "the same ref, whatever that means today."
    public static void Ensure(string name)
    {
        if (NativePackages.Contains(name)) return;

        var dir = Path.Combine(PackagesDir, name);
        if (Directory.Exists(dir)) return;

        var lockFile = ReadLock();
        if (lockFile.Packages.TryGetValue(name, out var locked))
        {
            Clone(name, locked.Url, dir, locked.ResolvedCommit);
            return;
        }

        if (!Registry.TryGetValue(name, out var rawSource))
        {
            var known = PackageRegistry.Find(name);
            var tip = known != null
                ? $"  Tip: run 'mko info {name}' — it's in the package registry ({known.Status})"
                : $"  Tip: run 'mko search {name}' to look for something close, or use " +
                  $"'using {name} from \"github:User/Repo\";' to specify where to get it";
            throw new MakoError($"unknown package '{name}' — not in the registry and not installed locally\n{tip}");
        }

        var (bareSource, pin) = ParseRef(rawSource);
        var url = ResolveUrl(bareSource);
        Clone(name, url, dir, pin);
    }

    /// Re-installs a package from scratch, ignoring any existing lockfile
    /// entry or cached clone — the only sanctioned way to intentionally
    /// move a package forward. Re-resolves the pin (or HEAD, if unpinned)
    /// to whatever it points at right now, and records the new result.
    public static void Update(string name)
    {
        if (NativePackages.Contains(name))
            throw new MakoError($"'{name}' is a native package — nothing to update.");

        var lockFile = ReadLock();
        string? source = null;
        if (lockFile.Packages.TryGetValue(name, out var locked))
            source = locked.Ref != null ? $"{locked.Url}@{locked.Ref}" : locked.Url;
        else if (Registry.TryGetValue(name, out var registered))
            source = registered;

        if (source == null)
            throw new MakoError($"'{name}' isn't installed and has no known source to update from.");

        var dir = Path.Combine(PackagesDir, name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);

        var (bareSource, pin) = ParseRef(source);
        var url = ResolveUrl(bareSource);
        Clone(name, url, dir, pin);
    }

    private static void Clone(string name, string url, string dir, string? pin)
    {
        Console.Error.WriteLine(pin != null
            ? $"mko: installing '{name}' from {url}@{pin} ..."
            : $"mko: installing '{name}' from {url} ...");
        Directory.CreateDirectory(PackagesDir);

        RunGit(name, dir, ["clone", url, dir]);

        if (pin != null)
        {
            try
            {
                RunGit(name, dir, ["-C", dir, "checkout", pin]);
            }
            catch (MakoError)
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
                throw new MakoError($"failed to check out '{pin}' for '{name}' — is it a real tag, branch, or commit?");
            }
        }

        string commit = RunGitCapture(dir, ["-C", dir, "rev-parse", "HEAD"]).Trim();
        WriteLockEntry(name, new LockEntry { Url = url, Ref = pin, ResolvedCommit = commit });

        Console.Error.WriteLine($"mko: '{name}' installed to {dir} ({commit[..Math.Min(8, commit.Length)]})");
    }

    private static void RunGit(string name, string dir, IEnumerable<string> args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git") { UseShellExecute = false };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new MakoError($"failed to launch git to install '{name}'");
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new MakoError($"git exited {proc.ExitCode} while installing '{name}'");
    }

    private static string RunGitCapture(string dir, IEnumerable<string> args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new MakoError("failed to launch git");
        string output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        return proc.ExitCode == 0 ? output : "";
    }

    // ── Lockfile ──────────────────────────────────────────────────────────

    private static LockFile ReadLock()
    {
        if (!File.Exists(LockPath)) return new LockFile();
        try
        {
            return JsonSerializer.Deserialize<LockFile>(File.ReadAllText(LockPath), JsonOptions) ?? new LockFile();
        }
        catch
        {
            return new LockFile(); // a corrupt lockfile shouldn't block installs — treat as absent
        }
    }

    private static void WriteLockEntry(string name, LockEntry entry)
    {
        var lockFile = ReadLock();
        lockFile.Packages[name] = entry;
        Directory.CreateDirectory(PackagesDir);
        File.WriteAllText(LockPath, JsonSerializer.Serialize(lockFile, JsonOptions));
    }

    // ── Introspection ────────────────────────────────────────────────────

    /// Path to the package's entry point, or null for native packages.
    public static string? IndexPath(string name)
    {
        if (NativePackages.Contains(name)) return null;
        var p = Path.Combine(PackagesDir, name, "index.mko");
        return File.Exists(p) ? p : null;
    }

    /// List all locally installed non-native packages.
    public static IEnumerable<(string Name, string Path)> ListInstalled()
    {
        if (!Directory.Exists(PackagesDir)) yield break;
        foreach (var dir in Directory.GetDirectories(PackagesDir))
            yield return (Path.GetFileName(dir), dir);
    }

    /// The recorded lock entry for an installed package, if any.
    public static LockEntry? GetLockEntry(string name) =>
        ReadLock().Packages.TryGetValue(name, out var entry) ? entry : null;

    /// Remove a cached package so it gets re-cloned on next use.
    public static bool Remove(string name)
    {
        var dir = Path.Combine(PackagesDir, name);
        var lockFile = ReadLock();
        bool hadLockEntry = lockFile.Packages.Remove(name);
        if (hadLockEntry)
            File.WriteAllText(LockPath, JsonSerializer.Serialize(lockFile, JsonOptions));

        if (!Directory.Exists(dir)) return hadLockEntry;
        Directory.Delete(dir, recursive: true);
        return true;
    }

    public static void Register(string name, string url) => Registry[name] = url;
}
