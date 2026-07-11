using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mako;

record FoundryTarget(
    string Id,
    string Name,
    string Platform,
    string Artifact,
    bool Available,
    string Status,
    string Description);

sealed class FoundryProject
{
    public string Root { get; init; } = "";
    public string ManifestPath { get; init; } = "";
    public string Name { get; set; } = "My MAKO Game";
    public string Version { get; set; } = "0.1.0";
    public string Entry { get; set; } = "main.mko";
    public string Output { get; set; } = "dist";
    public string? Icon { get; set; }
    public List<string> Include { get; set; } = [];
    public string DefaultTarget { get; set; } = "linux-x64";
    public string Physics3DBackend { get; set; } = "mako";
    public bool IncludeSiblingScripts { get; init; }

    public string EntryPath => Path.GetFullPath(Path.Combine(Root, Entry));
    public string OutputPath => Path.GetFullPath(Path.Combine(Root, Output));
}

record FoundryBuildResult(bool Success, string? ArtifactPath, string Message);

sealed class FoundryManifest
{
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Entry { get; set; }
    public string? Output { get; set; }
    public string? Icon { get; set; }
    public List<string>? Include { get; set; }
    public string? Target { get; set; }
    public string? Physics3D { get; set; }
}

/// Foundry's non-graphical backend. The GUI and CLI both use this exact path so
/// exports never depend on which surface initiated the build.
static class Foundry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static IReadOnlyList<FoundryTarget> Targets { get; } =
    [
        new("linux-x64", "Linux Portable", "Linux x64", "Executable folder", true, "Ready",
            "Self-contained MAKO runtime, game scripts, assets, and launcher."),
        new("windows-x64", "Windows", "Windows x64", "Executable folder", true, "Ready",
            "Self-contained MAKO runtime, game scripts, assets, and .exe launcher."),
        new("appimage", "AppImage", "Linux", ".AppImage", false, "Planned",
            "Single-file Linux desktop distribution."),
        new("android", "Android", "Android", ".apk", false, "Planned",
            "Android package with touch, lifecycle, and native runtime glue."),
        new("macos", "macOS", "macOS", ".app", true, "Ready (unsigned)",
            "Unsigned application bundle for Apple Silicon. Launches fine locally; " +
            "first-run needs \"Open Anyway\" in System Settings since it isn't signed " +
            "with an Apple Developer certificate."),
        new("web", "Web", "Browser / WASM", "Web bundle", true, "Ready (language-only)",
            "WebAssembly runtime, HTML shell, and browser asset bundle. Graphics/audio/" +
            "input/net packages aren't available in this build yet — language core and " +
            "Physics2D/Physics3D run for real."),
        new("vr", "VR", "OpenXR", "Platform package", false, "Later",
            "OpenXR build profiles layered on desktop and Android targets."),
        new("console", "Consoles", "Licensed SDKs", "Platform package", false, "Later",
            "Exporter adapters enabled only when an authorized SDK is installed."),
    ];

    public static FoundryProject LoadProject(string input)
    {
        string full = Path.GetFullPath(string.IsNullOrWhiteSpace(input) ? "." : input);
        string root;
        string? explicitEntry = null;
        if (File.Exists(full))
        {
            if (!full.EndsWith(".mko", StringComparison.OrdinalIgnoreCase))
                throw new MakoError($"Foundry: expected a .mko entry script, got '{full}'");
            root = Path.GetDirectoryName(full) ?? ".";
            explicitEntry = Path.GetFileName(full);
        }
        else if (Directory.Exists(full)) root = full;
        else throw new MakoError($"Foundry: project not found: {input}");

        string manifestPath = Path.Combine(root, "foundry.json");
        FoundryManifest? manifest = null;
        if (File.Exists(manifestPath))
        {
            try
            {
                manifest = JsonSerializer.Deserialize<FoundryManifest>(File.ReadAllText(manifestPath), JsonOptions)
                    ?? throw new MakoError("Foundry: foundry.json is empty");
            }
            catch (JsonException ex) { throw new MakoError($"Foundry: invalid foundry.json: {ex.Message}"); }
        }

        string entry = explicitEntry ?? manifest?.Entry ?? FindDefaultEntry(root)
            ?? throw new MakoError("Foundry: no entry script found (add main.mko or set 'entry' in foundry.json)");
        string fallbackName = Humanize(Path.GetFileNameWithoutExtension(entry));
        return new FoundryProject
        {
            Root = root,
            ManifestPath = manifestPath,
            Name = manifest?.Name ?? fallbackName,
            Version = manifest?.Version ?? "0.1.0",
            Entry = entry,
            Output = manifest?.Output ?? "dist",
            Icon = manifest?.Icon,
            Include = manifest?.Include ?? [],
            DefaultTarget = manifest?.Target ?? "linux-x64",
            Physics3DBackend = manifest?.Physics3D ?? "mako",
            IncludeSiblingScripts = explicitEntry == null,
        };
    }

    public static void SaveProject(FoundryProject project)
    {
        var manifest = new FoundryManifest
        {
            Name = project.Name,
            Version = project.Version,
            Entry = project.Entry,
            Output = project.Output,
            Icon = project.Icon,
            Include = project.Include.Count == 0 ? null : project.Include,
            Target = project.DefaultTarget,
            Physics3D = project.Physics3DBackend == "mako" ? null : project.Physics3DBackend,
        };
        File.WriteAllText(project.ManifestPath, JsonSerializer.Serialize(manifest, JsonOptions) + "\n");
    }

    public static List<string> Validate(FoundryProject project, string targetId)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(project.Name)) errors.Add("Project name is required.");
        if (string.IsNullOrWhiteSpace(project.Version)) errors.Add("Version is required.");
        if (!File.Exists(project.EntryPath)) errors.Add($"Entry script does not exist: {project.Entry}");
        var target = Targets.FirstOrDefault(t => t.Id == targetId);
        if (target == null) errors.Add($"Unknown target: {targetId}");
        else if (!target.Available) errors.Add($"{target.Name} target is {target.Status.ToLowerInvariant()}.");
        var physics = PhysicsBackends.Find(project.Physics3DBackend);
        if (physics.Dimension != "3d" && physics.Id != "mako") errors.Add($"Physics3D: {physics.Status}");
        else if (!physics.Installed) errors.Add($"Physics3D {physics.Name}: {physics.Status}");
        else if (!physics.BuiltIn) errors.Add($"Physics3D {physics.Name}: adapter is installed but this MAKO runtime was not compiled with its bridge.");
        foreach (string include in project.Include)
        {
            string full = Path.GetFullPath(Path.Combine(project.Root, include));
            string rootPrefix = Path.GetFullPath(project.Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(rootPrefix, StringComparison.Ordinal))
                errors.Add($"Included path must stay inside the project: {include}");
            else if (!File.Exists(full) && !Directory.Exists(full))
                errors.Add($"Included path does not exist: {include}");
        }
        return errors;
    }

    public static FoundryBuildResult Build(FoundryProject project, string targetId, Action<string>? log = null)
    {
        void Log(string message) { log?.Invoke(message); }
        var errors = Validate(project, targetId);
        if (errors.Count > 0) return new(false, null, string.Join("\n", errors));
        try
        {
            return targetId switch
            {
                "linux-x64" => BuildLinuxPortable(project, Log),
                "windows-x64" => BuildWindowsPortable(project, Log),
                "macos" => BuildMacBundle(project, Log),
                "web" => BuildWebBundle(project, Log),
                _ => new(false, null, $"Foundry target '{targetId}' is not implemented yet."),
            };
        }
        catch (Exception ex)
        {
            Log($"FAILED: {ex.Message}");
            return new(false, null, ex.Message);
        }
    }

    private static FoundryBuildResult BuildLinuxPortable(FoundryProject project, Action<string> log)
    {
        string slug = Slug(project.Name);
        string outputRoot = project.OutputPath;
        string artifact = Path.Combine(outputRoot, $"{slug}-linux-x64");
        string staging = artifact + ".building";
        log($"Foundry: building {project.Name} {project.Version}");
        log("Target: Linux x64 portable folder");

        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        string runtimeDir = Path.Combine(staging, "runtime");
        string gameDir = Path.Combine(staging, "game");
        Directory.CreateDirectory(runtimeDir);
        Directory.CreateDirectory(gameDir);

        string? csproj = FindMakoProject();
        if (csproj != null)
        {
            log("Publishing self-contained MAKO runtime...");
            RunProcess("dotnet",
            [
                "publish", csproj, "-c", "Release", "-r", "linux-x64", "--self-contained",
                "-p:PublishSingleFile=true", "-p:DebugType=None", "-o", runtimeDir,
            ], log);
        }
        else
        {
            log("Source project unavailable; bundling the current installed runtime.");
            CopyInstalledRuntime(runtimeDir);
        }

        string runtimeExe = Path.Combine(runtimeDir, "mko");
        if (!File.Exists(runtimeExe))
        {
            var candidate = Directory.GetFiles(runtimeDir, "mko*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => !path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase));
            if (candidate == null) throw new InvalidOperationException("published MAKO runtime was not produced");
            File.Copy(candidate, runtimeExe, true);
        }

        log("Bundling game scripts and assets...");
        BundleGameFiles(project, gameDir);

        string launcher = Path.Combine(staging, slug);
        File.WriteAllText(launcher,
            "#!/usr/bin/env bash\n" +
            "set -e\n" +
            "HERE=\"$(cd \"$(dirname \"$0\")\" && pwd)\"\n" +
            "export LD_LIBRARY_PATH=\"$HERE/runtime${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}\"\n" +
            "exec \"$HERE/runtime/mko\" \"$HERE/game/main.mko\" \"$@\"\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(launcher, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            File.SetUnixFileMode(runtimeExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        File.WriteAllText(Path.Combine(staging, "foundry-build.json"), JsonSerializer.Serialize(new
        {
            name = project.Name, version = project.Version, target = "linux-x64",
            entry = "game/main.mko", built_at = DateTimeOffset.UtcNow,
        }, JsonOptions) + "\n");

        Directory.CreateDirectory(outputRoot);
        if (Directory.Exists(artifact)) Directory.Delete(artifact, true);
        Directory.Move(staging, artifact);
        log($"Build complete: {artifact}");
        return new(true, artifact, "Build completed successfully.");
    }

    private static FoundryBuildResult BuildWindowsPortable(FoundryProject project, Action<string> log)
    {
        string slug = Slug(project.Name);
        string outputRoot = project.OutputPath;
        string artifact = Path.Combine(outputRoot, $"{slug}-windows-x64");
        string staging = artifact + ".building";
        log($"Foundry: building {project.Name} {project.Version}");
        log("Target: Windows x64 portable folder");

        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        string runtimeDir = Path.Combine(staging, "runtime");
        string gameDir = Path.Combine(staging, "game");
        Directory.CreateDirectory(runtimeDir);
        Directory.CreateDirectory(gameDir);

        string? csproj = FindMakoProject();
        if (csproj == null)
            throw new InvalidOperationException(
                "MAKO source project not found — cross-compiling a Windows build needs the " +
                "source, either from a repo checkout or the copy build.sh install ships to " +
                "~/.local/share/mko/src/Mako.");

        log("Publishing self-contained MAKO runtime (win-x64)...");
        RunProcess("dotnet",
        [
            "publish", csproj, "-c", "Release", "-r", "win-x64", "--self-contained",
            "-p:PublishSingleFile=true", "-p:DebugType=None", "-o", runtimeDir,
        ], log);

        string runtimeExe = Path.Combine(runtimeDir, "mko.exe");
        if (!File.Exists(runtimeExe))
            throw new InvalidOperationException("published MAKO runtime (mko.exe) was not produced");

        log("Bundling game scripts and assets...");
        BundleGameFiles(project, gameDir);

        string launcher = Path.Combine(staging, slug + ".bat");
        File.WriteAllText(launcher,
            "@echo off\r\n" +
            "set HERE=%~dp0\r\n" +
            "\"%HERE%runtime\\mko.exe\" \"%HERE%game\\main.mko\" %*\r\n");

        File.WriteAllText(Path.Combine(staging, "foundry-build.json"), JsonSerializer.Serialize(new
        {
            name = project.Name, version = project.Version, target = "windows-x64",
            entry = "game/main.mko", built_at = DateTimeOffset.UtcNow,
        }, JsonOptions) + "\n");

        Directory.CreateDirectory(outputRoot);
        if (Directory.Exists(artifact)) Directory.Delete(artifact, true);
        Directory.Move(staging, artifact);
        log($"Build complete: {artifact}");
        return new(true, artifact, "Build completed successfully.");
    }

    private static FoundryBuildResult BuildMacBundle(FoundryProject project, Action<string> log)
    {
        string slug = Slug(project.Name);
        string outputRoot = project.OutputPath;
        string artifact = Path.Combine(outputRoot, $"{slug}-macos");
        string staging = artifact + ".building";
        log($"Foundry: building {project.Name} {project.Version}");
        log("Target: macOS (Apple Silicon), unsigned .app bundle");

        if (Directory.Exists(staging)) Directory.Delete(staging, true);

        string? csproj = FindMakoProject();
        if (csproj == null)
            throw new InvalidOperationException(
                "MAKO source project not found — cross-compiling a macOS build needs the " +
                "source, either from a repo checkout or the copy build.sh install ships to " +
                "~/.local/share/mko/src/Mako.");

        // Standard macOS .app bundle layout:
        //   Slug.app/Contents/{MacOS/<runtime + game>, Info.plist}
        string contents = Path.Combine(staging, $"{slug}.app", "Contents");
        string macosDir = Path.Combine(contents, "MacOS");
        string runtimeDir = Path.Combine(macosDir, "runtime");
        string gameDir = Path.Combine(macosDir, "game");
        Directory.CreateDirectory(runtimeDir);
        Directory.CreateDirectory(gameDir);

        log("Publishing self-contained MAKO runtime (osx-arm64)...");
        RunProcess("dotnet",
        [
            "publish", csproj, "-c", "Release", "-r", "osx-arm64", "--self-contained",
            "-p:PublishSingleFile=true", "-p:DebugType=None", "-o", runtimeDir,
        ], log);

        string runtimeExe = Path.Combine(runtimeDir, "mko");
        if (!File.Exists(runtimeExe))
            throw new InvalidOperationException("published MAKO runtime was not produced");

        log("Bundling game scripts and assets...");
        BundleGameFiles(project, gameDir);

        string launcher = Path.Combine(macosDir, slug);
        File.WriteAllText(launcher,
            "#!/usr/bin/env bash\n" +
            "set -e\n" +
            "HERE=\"$(cd \"$(dirname \"$0\")\" && pwd)\"\n" +
            "exec \"$HERE/runtime/mko\" \"$HERE/game/main.mko\" \"$@\"\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(launcher, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            File.SetUnixFileMode(runtimeExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        File.WriteAllText(Path.Combine(contents, "Info.plist"),
            $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>CFBundleName</key><string>{project.Name}</string>
                <key>CFBundleIdentifier</key><string>com.mako.{slug}</string>
                <key>CFBundleVersion</key><string>{project.Version}</string>
                <key>CFBundleExecutable</key><string>{slug}</string>
                <key>CFBundlePackageType</key><string>APPL</string>
                <key>LSMinimumSystemVersion</key><string>11.0</string>
            </dict>
            </plist>
            """);

        File.WriteAllText(Path.Combine(macosDir, "foundry-build.json"), JsonSerializer.Serialize(new
        {
            name = project.Name, version = project.Version, target = "macos",
            entry = "game/main.mko", built_at = DateTimeOffset.UtcNow, signed = false,
        }, JsonOptions) + "\n");

        Directory.CreateDirectory(outputRoot);
        if (Directory.Exists(artifact)) Directory.Delete(artifact, true);
        Directory.Move(staging, artifact);
        log($"Build complete: {artifact}");
        log("Note: this bundle isn't code-signed. On first launch, macOS Gatekeeper will " +
            "block it — right-click the .app, choose Open, then Open Anyway.");
        return new(true, artifact, "Build completed successfully.");
    }

    private static void BundleGameFiles(FoundryProject project, string gameDir)
    {
        if (project.IncludeSiblingScripts)
            foreach (string script in Directory.GetFiles(project.Root, "*.mko", SearchOption.TopDirectoryOnly))
                File.Copy(script, Path.Combine(gameDir,
                    Path.GetFileName(script).Equals(project.Entry, StringComparison.OrdinalIgnoreCase)
                        ? "main.mko" : Path.GetFileName(script)), true);
        if (!File.Exists(Path.Combine(gameDir, "main.mko")))
            File.Copy(project.EntryPath, Path.Combine(gameDir, "main.mko"), true);

        string defaultAssets = Path.Combine(project.Root, "assets");
        if (Directory.Exists(defaultAssets)) CopyDirectory(defaultAssets, Path.Combine(gameDir, "assets"));
        foreach (string include in project.Include)
        {
            string source = Path.GetFullPath(Path.Combine(project.Root, include));
            string destination = Path.Combine(gameDir, Path.GetRelativePath(project.Root, source));
            if (Directory.Exists(source)) CopyDirectory(source, destination);
            else { Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination, true); }
        }
    }

    private static FoundryBuildResult BuildWebBundle(FoundryProject project, Action<string> log)
    {
        string slug = Slug(project.Name);
        string outputRoot = project.OutputPath;
        string artifact = Path.Combine(outputRoot, $"{slug}-web");
        string staging = artifact + ".building";
        log($"Foundry: building {project.Name} {project.Version}");
        log("Target: Web (WASM, language-only)");

        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);

        string? csproj = FindMakoWebProject();
        if (csproj == null)
            throw new InvalidOperationException("Mako.Web project not found — cannot build a web target from this install.");

        log("Publishing MAKO web runtime (dotnet publish, Release)...");
        RunProcess("dotnet", ["publish", csproj, "-c", "Release", "-o", staging], log);

        // dotnet publish for a BlazorWebAssembly project writes the actual
        // static site (index.html, _framework/, etc.) into a "wwwroot"
        // subfolder of the publish output, not the publish root itself.
        string wwwroot = Path.Combine(staging, "wwwroot");
        if (!Directory.Exists(wwwroot))
            throw new InvalidOperationException($"expected published output at {wwwroot}, but it was not produced");

        log("Bundling game script as game.mko...");
        File.Copy(project.EntryPath, Path.Combine(wwwroot, "game.mko"), true);

        File.WriteAllText(Path.Combine(wwwroot, "foundry-build.json"), JsonSerializer.Serialize(new
        {
            name = project.Name, version = project.Version, target = "web",
            entry = "game.mko", built_at = DateTimeOffset.UtcNow,
        }, JsonOptions) + "\n");

        Directory.CreateDirectory(outputRoot);
        if (Directory.Exists(artifact)) Directory.Delete(artifact, true);
        Directory.Move(wwwroot, artifact);
        Directory.Delete(staging, true);
        log($"Build complete: {artifact}");
        log("Serve it with any static file server, e.g.: python3 -m http.server --directory " + artifact);
        return new(true, artifact, "Build completed successfully.");
    }

    private static string? FindMakoWebProject()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++, dir = Directory.GetParent(dir)?.FullName)
        {
            string nested = Path.Combine(dir, "src", "Mako.Web", "Mako.Web.csproj");
            if (File.Exists(nested)) return nested;
        }

        // Not running from inside the repo checkout (e.g. the self-contained
        // release binary installed by build.sh's `install` command, which
        // runs from ~/.local/share/mko/bin, well outside the source tree).
        // build.sh also ships a copy of src/Mako.Web alongside the examples
        // it already installs to ~/.local/share/mako — check there too.
        string installed = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "mko", "src", "Mako.Web", "Mako.Web.csproj");
        if (File.Exists(installed)) return installed;

        return null;
    }

    private static string? FindDefaultEntry(string root)
    {
        foreach (string name in new[] { "main.mko", "game.mko" })
            if (File.Exists(Path.Combine(root, name))) return name;
        var scripts = Directory.GetFiles(root, "*.mko", SearchOption.TopDirectoryOnly);
        return scripts.Length == 1 ? Path.GetFileName(scripts[0]) : null;
    }

    private static string? FindMakoProject()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++, dir = Directory.GetParent(dir)?.FullName)
        {
            string direct = Path.Combine(dir, "Mako.csproj");
            if (File.Exists(direct)) return direct;
            string nested = Path.Combine(dir, "src", "Mako", "Mako.csproj");
            if (File.Exists(nested)) return nested;
        }

        // Not running from inside the repo checkout (e.g. the self-contained
        // release binary installed by build.sh's `install` command, which
        // runs from ~/.local/share/mko/bin, well outside the source tree).
        // Cross-compiling a Windows/macOS build needs the actual source
        // project — CopyInstalledRuntime only works for the linux-x64
        // target, where the already-running binary IS the target. build.sh
        // ships a copy of src/Mako alongside src/Mako.Web at
        // ~/.local/share/mko/src (see FindMakoWebProject) — check there too.
        string installed = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "mko", "src", "Mako", "Mako.csproj");
        if (File.Exists(installed)) return installed;

        return null;
    }

    private static void CopyInstalledRuntime(string destination)
    {
        string baseDir = AppContext.BaseDirectory;
        foreach (string file in Directory.GetFiles(baseDir))
            if (!file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        string? process = Environment.ProcessPath;
        if (process != null && File.Exists(process) && Path.GetFileName(process) != "dotnet")
            File.Copy(process, Path.Combine(destination, "mko"), true);
    }

    private static void RunProcess(string file, IEnumerable<string> args, Action<string> log)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"could not start {file}");
        process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log(e.Data); };
        process.BeginOutputReadLine(); process.BeginErrorReadLine(); process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{file} exited with code {process.ExitCode}");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (string dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    private static string Humanize(string value) =>
        string.Join(' ', value.Replace('-', '_').Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static string Slug(string value)
    {
        var chars = value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        string slug = new(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-') is { Length: > 0 } result ? result : "mako-game";
    }
}
