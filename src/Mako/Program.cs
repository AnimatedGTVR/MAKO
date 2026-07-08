using Mako;

// ── CLI entry point ───────────────────────────────────────────────────────────
//
// Usage:
//   mko run <file.mko>      run a MAKO script
//   mko repl                interactive REPL
//   mko get <package>       install a package
//   mko list                list installed packages
//   mko cache clear         clear package cache
//   mko version             print version
//   mko help                print help

if (args.Length == 0 || args[0] == "help" || args[0] == "--help" || args[0] == "-h")
{
    PrintHelp();
    return 0;
}

if (args[0] == "version" || args[0] == "--version" || args[0] == "-v")
{
    Console.WriteLine("MAKO 0.03");
    return 0;
}

// Shorthand: `mko script.mko` or `mko ./script.mko` — no 'run' needed.
// Also handles shebang invocation: `#!/usr/bin/env mko`
if (args[0].EndsWith(".mko", StringComparison.OrdinalIgnoreCase) || File.Exists(args[0]))
    args = new[] { "run" }.Concat(args).ToArray();

if (args[0] == "run")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: mko run <file.mko>");
        return 1;
    }

    string path = args[1];
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"mko: file not found: {path}");
        return 1;
    }
    // Allow extension-less files when invoked via shebang (#! line).
    if (!path.EndsWith(".mko", StringComparison.OrdinalIgnoreCase) && !File.Exists(path))
    {
        Console.Error.WriteLine($"mko: file not found: {path}");
        return 1;
    }

    string source;
    try { source = File.ReadAllText(path); }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"mko: could not read file: {ex.Message}");
        return 1;
    }

    // Strip shebang line so `#!/usr/bin/env mko` doesn't cause a parse error.
    if (source.StartsWith("#!"))
        source = source[(source.IndexOf('\n') + 1)..];

    try
    {
        var tokens  = new Lexer(source).Tokenize();
        var program = new Parser(tokens).Parse();
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        new Interpreter().Execute(program, baseDir);
        return 0;
    }
    catch (MakoError ex)
    {
        ReportError(ex, source);
        return 1;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"mko: internal error: {ex.Message}");
        return 1;
    }
}

if (args[0] == "fmt")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: mko fmt <file.mko> [--check]");
        return 1;
    }

    bool checkOnly = args.Contains("--check");
    string fmtPath = args.Where(a => !a.StartsWith('-')).Skip(1).FirstOrDefault() ?? "";

    if (!File.Exists(fmtPath))
    {
        Console.Error.WriteLine($"mko: file not found: {fmtPath}");
        return 1;
    }

    string fmtSource;
    try { fmtSource = File.ReadAllText(fmtPath); }
    catch (Exception ex) { Console.Error.WriteLine($"mko: could not read: {ex.Message}"); return 1; }

    string formatted;
    try { formatted = Formatter.Format(fmtSource); }
    catch (MakoError ex) { ReportError(ex, fmtSource); return 1; }

    if (checkOnly)
    {
        if (formatted == fmtSource)
        {
            Console.WriteLine($"{fmtPath}: already formatted");
            return 0;
        }
        Console.WriteLine($"{fmtPath}: needs formatting");
        return 1;
    }

    try { File.WriteAllText(fmtPath, formatted); }
    catch (Exception ex) { Console.Error.WriteLine($"mko: could not write: {ex.Message}"); return 1; }

    Console.WriteLine($"formatted {fmtPath}");
    return 0;
}

if (args[0] == "repl")
{
    RunRepl();
    return 0;
}

if (args[0] == "get")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: mko get <package>  [or]  mko get <package> <github:User/Repo>");
        return 1;
    }
    var pkgName = args[1];
    if (args.Length >= 3)
        PackageManager.Register(pkgName, PackageManager.ResolveUrl(args[2]));
    try
    {
        PackageManager.Ensure(pkgName);
        Console.WriteLine($"mko: '{pkgName}' is ready.");
        return 0;
    }
    catch (MakoError ex)
    {
        Console.Error.WriteLine($"mko: {ex.RawMessage}");
        return 1;
    }
}

if (args[0] == "list")
{
    var pkgs = PackageManager.ListInstalled().ToList();
    if (pkgs.Count == 0)
    {
        Console.WriteLine("No packages installed.");
    }
    else
    {
        Console.WriteLine("Installed packages:");
        foreach (var (name, path) in pkgs)
            Console.WriteLine($"  {name}  ({path})");
    }
    return 0;
}

if (args[0] == "cache")
{
    if (args.Length >= 2 && args[1] == "clear")
    {
        if (args.Length >= 3)
        {
            if (PackageManager.Remove(args[2]))
                Console.WriteLine($"mko: removed '{args[2]}' from cache.");
            else
                Console.WriteLine($"mko: '{args[2]}' was not in the cache.");
        }
        else
        {
            foreach (var (name, _) in PackageManager.ListInstalled())
                PackageManager.Remove(name);
            Console.WriteLine("mko: package cache cleared.");
        }
        return 0;
    }
    Console.Error.WriteLine("Usage: mko cache clear [package]");
    return 1;
}

Console.Error.WriteLine($"mko: unknown command '{args[0]}'. Run 'mko help'.");
return 1;

// ─────────────────────────────────────────────────────────────────────────────

static void RunRepl()
{
    Console.WriteLine("MAKO 0.03 REPL  (Ctrl+C or 'exit' to quit)");
    Console.WriteLine();

    var interpreter = new Interpreter();
    var history     = new List<string>();

    while (true)
    {
        Console.Write("> ");
        string? line;
        try { line = Console.ReadLine(); }
        catch (Exception) { break; }

        if (line == null) break;

        var trimmed = line.Trim();
        if (trimmed == "exit" || trimmed == "quit") break;
        if (trimmed == "") continue;

        history.Add(trimmed);

        // Wrap bare expressions so we can print their value, but allow
        // statements (print, if, while, fn, etc.) to pass through.
        // Strategy: try parsing as a full script; if that fails, try wrapping.
        var source = WrapForRepl(trimmed);

        try
        {
            var tokens  = new Lexer(source).Tokenize();
            var program = new Parser(tokens).Parse();
            interpreter.ExecuteRepl(program);
        }
        catch (MakoError ex)
        {
            ReportReplError(ex, trimmed, source);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"mko: internal error: {ex.Message}");
        }
    }

    Console.WriteLine("\nBye!");
}

static string WrapForRepl(string line)
{
    // If the line looks like a statement keyword, pass through as main body.
    // Otherwise wrap in main() so the interpreter can evaluate it.
    return $"main() {{ {line}{(line.TrimEnd().EndsWith(';') ? "" : ";")} }}";
}

static void ReportReplError(MakoError ex, string userLine, string wrapped)
{
    if (ex.Line <= 0)
    {
        Console.Error.WriteLine($"mko: error: {ex.RawMessage}");
        return;
    }
    // Show the user's original input, not the wrapped version.
    var display = userLine.Trim();
    Console.Error.WriteLine($"\n  {display}");
    if (ex.Col > 0)
    {
        int adj = Math.Max(0, ex.Col - "main() { ".Length);
        int len = Math.Clamp(ex.Length, 1, display.Length + 2 - adj);
        Console.Error.WriteLine("  " + new string(' ', adj) + new string('^', len));
    }
    Console.Error.WriteLine($"mko: error: {ex.RawMessage}\n");
}

// Renders an error as:
//
//     print greet("World")
//                         ^
//   mko: error (line 21): missing ';' (got start of 'print' statement)
//
// The caret lands on the error's column (or underlines the whole line when the
// column is unknown). Errors from imported modules show the module's source.
static void ReportError(MakoError ex, string mainSource)
{
    string? source = mainSource;
    string  where  = $"line {ex.Line}";

    if (ex.SourcePath != null)
    {
        where = $"line {ex.Line} in {Path.GetFileName(ex.SourcePath)}";
        try { source = File.ReadAllText(ex.SourcePath); }
        catch { source = null; }
    }

    if (ex.Line <= 0)
    {
        Console.Error.WriteLine($"mko: error: {ex.RawMessage}");
        return;
    }

    var lines = source?.Split('\n');
    if (lines != null && ex.Line <= lines.Length)
    {
        var raw     = lines[ex.Line - 1].TrimEnd('\r', ' ').Replace('\t', ' ');
        var srcLine = raw.TrimStart();
        int indent  = raw.Length - srcLine.Length;

        Console.Error.WriteLine($"\n  {srcLine}");

        string pointer;
        if (ex.Col > 0)
        {
            int caretCol = Math.Clamp(ex.Col - indent, 1, srcLine.Length + 1);
            int caretLen = Math.Clamp(ex.Length, 1, srcLine.Length + 2 - caretCol);
            pointer = new string(' ', caretCol - 1) + new string('^', caretLen);
        }
        else
        {
            pointer = new string('^', Math.Max(1, srcLine.Length));
        }
        Console.Error.WriteLine($"  {pointer}");
    }
    else
    {
        Console.Error.WriteLine();
    }

    // Suggestion on same line as error for compact output
    string hint = ex.Hint != null ? $"\n  hint: {ex.Hint}" : "";
    Console.Error.WriteLine($"mko: error ({where}): {ex.RawMessage}{hint}\n");
}

static void PrintHelp()
{
    Console.WriteLine("""
    MAKO 0.03 — a simple, sharp programming language

    Usage:
      mko run <file.mko>            Run a MAKO script
      mko fmt <file.mko>            Format a MAKO file in-place
      mko fmt <file.mko> --check   Check if a file is formatted
      mko repl                      Start interactive REPL
      mko get <pkg> [github:U/R]    Install a package
      mko list                      List installed packages
      mko cache clear [pkg]         Remove cached package(s)
      mko version                   Show version
      mko help                      Show this help

    Language features:
      Variables, arithmetic (+  -  *  /  %)
      Compound assignment (+=  -=  *=  /=)
      Strings, numbers, booleans, lists, none
      const  — immutable top-level or block constants
      if / else if / else
      while  /  for item in list
      break  /  continue
      fn / return  (user-defined functions, recursive)
      and / or / not  (short-circuit logical)
      print  /  printnl  /  input  /  run

    Standard library:
      String: len  upper  lower  trim  contains
              starts_with  ends_with  replace  split  join
      List:   len  push  pop  first  last  reverse  has
      Math:   abs  floor  ceil  sqrt  pow  max  min  round
      I/O:    read  write  append  exists  delete  lines
      System: time  random  random_int  sleep  env
      Util:   type  to_num  to_str  assert

    Packages:
      using MakoUI;                          built-in GUI (Dear ImGui)
      using mylib from "github:User/Repo";   fetch from GitHub

    Example:
      mko run examples/hello.mko
    """);
}
