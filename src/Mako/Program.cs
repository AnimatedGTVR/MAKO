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
    Console.WriteLine("MAKO 0.1.0");
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
        new Interpreter().Execute(program);
        return 0;
    }
    catch (MakoError ex)
    {
        Console.Error.WriteLine($"mako: error: {ex.Message}");
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

static void PrintHelp()
{
    Console.WriteLine("""
    MAKO 0.1.0 — a simple, sharp programming language

    Usage:
      mako run <file.mko>   Run a MAKO script
      mako version          Show version
      mako help             Show this help

    Example:
      mako run examples/hello.mko

    MAKO files use the .mko extension.
    See https://github.com/AnimatedGTVR/MAKO for docs and examples.
    """);
}
