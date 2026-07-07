namespace Mako;

static class PackageManager
{
    private static readonly string PackagesDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "mko", "packages");

    // Native packages are built into the interpreter — no files to clone.
    public static readonly HashSet<string> NativePackages =
        new(StringComparer.OrdinalIgnoreCase) { "MakoUI", "IMGUI" };

    // Public registry: name → GitHub clone URL.
    private static readonly Dictionary<string, string> Registry =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["MakoUI"] = "https://github.com/AnimatedGTVR/MakoUI",
            ["IMGUI"]  = "https://github.com/AnimatedGTVR/MakoUI",
        };

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
                $"  Install manually: git clone <url> {dir}");

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
            // Clean up partial clone so future runs retry cleanly.
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

    /// Register a custom package URL at runtime.
    public static void Register(string name, string url) => Registry[name] = url;
}
