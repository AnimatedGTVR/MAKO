using Mako;

// ── CLI entry point ───────────────────────────────────────────────────────────
//
// Usage:
//   mako run <file.mko>      run a MAKO script
//   mako version             print version
//   mako help                print help

if (args.Length == 0 || args[0] == "help" || args[0] == "--help" || args[0] == "-h")
{
    PrintHelp();
    return 0;
}

if (args[0] == "version" || args[0] == "--version" || args[0] == "-v")
{
    Console.WriteLine("MAKO 0.02");
    return 0;
}

if (args[0] == "run")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: mako run <file.mko>");
        return 1;
    }

    string path = args[1];
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"mako: file not found: {path}");
        return 1;
    }
    if (!path.EndsWith(".mko", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine($"mako: file must have a .mko extension");
        return 1;
    }

    string source;
    try { source = File.ReadAllText(path); }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"mako: could not read file: {ex.Message}");
        return 1;
    }

    try
    {
        var tokens     = new Lexer(source).Tokenize();
        var program    = new Parser(tokens).Parse();
        var baseDir    = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
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
        Console.Error.WriteLine($"mako: internal error: {ex.Message}");
        return 1;
    }
}

Console.Error.WriteLine($"mako: unknown command '{args[0]}'. Run 'mako help'.");
return 1;

// ─────────────────────────────────────────────────────────────────────────────

// Renders an error as:
//
//     print greet("World")
//                         ^
//   mako: error (line 21): missing ';' (got start of 'print' statement)
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
        Console.Error.WriteLine($"mako: error: {ex.RawMessage}");
        return;
    }

    var lines = source?.Split('\n');
    if (lines != null && ex.Line <= lines.Length)
    {
        // Tabs become single spaces so caret columns still line up.
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

    Console.Error.WriteLine($"mako: error ({where}): {ex.RawMessage}\n");
}

static void PrintHelp()
{
    Console.WriteLine("""
    MAKO 0.02 — a simple, sharp programming language

    Usage:
      mako run <file.mko>   Run a MAKO script
      mako version          Show version
      mako help             Show this help

    Language features:
      Variables, arithmetic (+  -  *  /  %)
      Compound assignment (+=  -=  *=  /=)
      Strings, numbers, booleans, lists, none
      if / else if / else
      while  /  for item in list
      break  /  continue
      fn / return  (user-defined functions, recursive)
      and / or / not  (short-circuit logical)
      print  /  printnl  /  input  /  run
      String: len  upper  lower  trim  contains
              starts_with  ends_with  replace  split  join
      List:   len  push  pop  first  last  reverse  has
      Math:   abs  floor  ceil  sqrt  pow  max  min  round
      Util:   type  to_num  to_str

    Example:
      mako run examples/hello.mko

    MAKO files use the .mko extension.
    """);
}
