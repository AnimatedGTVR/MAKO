namespace Mako;

static class PackageManager
{
    public static readonly string PackagesDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "mko", "packages");

    // Native packages are built into the interpreter — no files to clone.
    public static readonly HashSet<string> NativePackages =
        new(StringComparer.OrdinalIgnoreCase) { "MakoUI", "IMGUI", "MakoRay", "Mako2D", "Mako3D", "Inputs", "Audio", "Net", "Hanami" };

    // Public registry: name → clone URL. Populated by 'using Name from "..."' or built-in list.
    private static readonly Dictionary<string, string> Registry =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["MakoUI"] = "https://github.com/AnimatedGTVR/MakoUI",
            ["IMGUI"]  = "https://github.com/AnimatedGTVR/MakoUI",
        };

    /// Resolve a source string like "github:User/Repo" to a full clone URL.
    public static string ResolveUrl(string source) => source.StartsWith("github:", StringComparison.OrdinalIgnoreCase)
        ? "https://github.com/" + source["github:".Length..]
        : source; // treat anything else as a raw URL

    /// Register a package from a 'using Name from "source"' declaration.
    public static void RegisterSource(string name, string source)
    {
        if (!NativePackages.Contains(name))
            Registry[name] = ResolveUrl(source);
    }

    /// Ensures the package is locally available, cloning it if needed.
    /// Native packages are a no-op.
    public static void Ensure(string name)
    {
        if (NativePackages.Contains(name)) return;

        var dir = Path.Combine(PackagesDir, name);
        if (Directory.Exists(dir)) return;

        if (!Registry.TryGetValue(name, out var url))
            throw new MakoError(
                $"unknown package '{name}' — not in the registry and not installed locally\n" +
                $"  Tip: use 'using {name} from \"github:User/Repo\";' to specify where to get it");

        Clone(name, url, dir);
    }

    private static void Clone(string name, string url, string dir)
    {
        Console.Error.WriteLine($"mko: installing '{name}' from {url} ...");
        Directory.CreateDirectory(PackagesDir);

        var psi = new System.Diagnostics.ProcessStartInfo("git", $"clone \"{url}\" \"{dir}\"")
        {
            UseShellExecute = false,
        };
        var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new MakoError($"failed to launch git to install '{name}'");
        proc.WaitForExit();

        if (proc.ExitCode != 0)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
            throw new MakoError($"failed to install '{name}' (git exited {proc.ExitCode})");
        }

        Console.Error.WriteLine($"mko: '{name}' installed to {dir}");
    }

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

    /// Remove a cached package so it gets re-cloned on next use.
    public static bool Remove(string name)
    {
        var dir = Path.Combine(PackagesDir, name);
        if (!Directory.Exists(dir)) return false;
        Directory.Delete(dir, recursive: true);
        return true;
    }

    public static void Register(string name, string url) => Registry[name] = url;
}
