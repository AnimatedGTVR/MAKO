namespace Mako;

/// System — directories, processes, and copying, for MAKO scripts.
///
/// Single-file ops (read/write/append/exists/delete/lines) and env/args are
/// already global builtins (see stdlib.md) — this package only adds what
/// those don't cover: directory manipulation, running other processes, and
/// setting environment variables.
///
///   using System;
///
///   main() {
///       if not System.dir_exists("build") { System.make_dir("build"); }
///
///       res = System.exec("gcc", ["-o", "build/app", "main.c"]);
///       if System.ok(res) print "build ok";
///       else print System.stderr(res);
///   }
///
/// A process result is a dict: {"code": 0, "stdout": "...", "stderr": "...", "ok": true}
static class MakoSystem
{
    // ── Files (beyond the global read/write/append/exists/delete) ──────────────

    public static object? CopyFile(List<object?> a)
    {
        var src = a.Count > 0 ? a[0]?.ToString() ?? "" : "";
        var dst = a.Count > 1 ? a[1]?.ToString() ?? "" : "";
        try { File.Copy(src, dst, overwrite: true); return true; }
        catch (Exception ex) { throw new MakoError($"copy_file: {ex.Message}"); }
    }

    // ── Directories ───────────────────────────────────────────────────────────

    public static object? ListDir(List<object?> a)
    {
        var path = a.Count > 0 ? a[0]?.ToString() ?? "." : ".";
        try
        {
            var entries = new List<object?>();
            foreach (var e in Directory.EnumerateFileSystemEntries(path))
                entries.Add(Path.GetFileName(e));
            return entries;
        }
        catch (Exception ex) { throw new MakoError($"list_dir: {ex.Message}"); }
    }

    public static object? MakeDir(List<object?> a)
    {
        var path = a.Count > 0 ? a[0]?.ToString() ?? "" : "";
        try { Directory.CreateDirectory(path); return true; }
        catch (Exception ex) { throw new MakoError($"make_dir: {ex.Message}"); }
    }

    public static object? RemoveDir(List<object?> a)
    {
        var path = a.Count > 0 ? a[0]?.ToString() ?? "" : "";
        var recursive = a.Count > 1 && a[1] is bool b && b;
        try { Directory.Delete(path, recursive); return true; }
        catch (Exception ex) { throw new MakoError($"remove_dir: {ex.Message}"); }
    }

    public static object? DirExists(List<object?> a) =>
        (object?)Directory.Exists(a.Count > 0 ? a[0]?.ToString() ?? "" : "");

    public static object? Cwd(List<object?> _) => Directory.GetCurrentDirectory();

    // ── Process execution ────────────────────────────────────────────────────

    private static Dictionary<string, object?> WrapProc(int code, string stdout, string stderr) =>
        new()
        {
            ["code"]   = (double)code,
            ["stdout"] = stdout,
            ["stderr"] = stderr,
            ["ok"]     = code == 0,
        };

    /// exec(command, args=[]) — run a process, wait for it, capture output.
    public static object? Exec(List<object?> a)
    {
        var cmd = a.Count > 0 ? a[0]?.ToString() ?? "" : "";
        var args = a.Count > 1 && a[1] is List<object?> list
            ? list.Select(x => x?.ToString() ?? "")
            : Enumerable.Empty<string>();

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName               = cmd,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
            };
            foreach (var arg in args) psi.ArgumentList.Add(arg);

            using var proc = System.Diagnostics.Process.Start(psi)
                ?? throw new MakoError($"exec: failed to start '{cmd}'");
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return WrapProc(proc.ExitCode, stdout, stderr);
        }
        catch (MakoError) { throw; }
        catch (Exception ex) { return WrapProc(-1, "", ex.Message); }
    }

    private static Dictionary<string, object?>? AsProc(List<object?> a) =>
        a.Count > 0 ? a[0] as Dictionary<string, object?> : null;

    public static object? ProcOk(List<object?> a) =>
        (object?)(AsProc(a)?.GetValueOrDefault("ok") is true);

    public static object? ProcCode(List<object?> a) =>
        AsProc(a)?.GetValueOrDefault("code") ?? -1d;

    public static object? ProcStdout(List<object?> a) =>
        AsProc(a)?.GetValueOrDefault("stdout") ?? "";

    public static object? ProcStderr(List<object?> a) =>
        AsProc(a)?.GetValueOrDefault("stderr") ?? "";

    // ── Environment (beyond the global env()/args()) ────────────────────────────

    public static object? SetEnv(List<object?> a)
    {
        var name  = a.Count > 0 ? a[0]?.ToString() ?? "" : "";
        var value = a.Count > 1 ? a[1]?.ToString() ?? "" : "";
        Environment.SetEnvironmentVariable(name, value);
        return true;
    }

    public static object? Platform(List<object?> _) =>
        OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsMacOS()   ? "macos"   :
        OperatingSystem.IsLinux()   ? "linux"   : "unknown";

    // ── Dispatch table ────────────────────────────────────────────────────────

    public static readonly Dictionary<string, Func<List<object?>, object?>> Funcs = new()
    {
        ["copy_file"]   = CopyFile,

        ["list_dir"]    = ListDir,
        ["make_dir"]    = MakeDir,
        ["remove_dir"]  = RemoveDir,
        ["dir_exists"]  = DirExists,
        ["cwd"]         = Cwd,

        ["exec"]        = Exec,
        ["ok"]          = ProcOk,
        ["code"]        = ProcCode,
        ["stdout"]      = ProcStdout,
        ["stderr"]      = ProcStderr,

        ["set_env"]     = SetEnv,
        ["platform"]    = Platform,
    };
}
