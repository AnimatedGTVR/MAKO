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
    Console.WriteLine("MAKO 0.1.0");
    return 0;
}

if (args[0] == "modumako")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: mko modumako <file.modumako> [-o output.moducpp]");
        return 1;
    }
    string inputPath = args[1];
    if (!File.Exists(inputPath))
    {
        Console.Error.WriteLine($"mko: file not found: {inputPath}");
        return 1;
    }
    try
    {
        string generated = ModuMakoCompiler.Compile(File.ReadAllText(inputPath), inputPath);
        int outputIndex = Array.IndexOf(args, "-o");
        if (outputIndex >= 0)
        {
            if (outputIndex + 1 >= args.Length)
            {
                Console.Error.WriteLine("mko modumako: -o requires an output path");
                return 1;
            }
            File.WriteAllText(args[outputIndex + 1], generated);
            Console.WriteLine($"emitted ModuCPP: {args[outputIndex + 1]}");
        }
        else Console.Write(generated);
        return 0;
    }
    catch (MakoError ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
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

    string? path = ResolvePath(args[1]);
    if (path == null)
    {
        Console.Error.WriteLine($"mko: file not found: {args[1]}");
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
        var typeIssues = SystemsTypeChecker.Check(program);
        if (typeIssues.Count > 0)
        {
            foreach (var issue in typeIssues.OrderBy(i => i.Line))
                Console.Error.WriteLine($"{path}:{issue.Line}: type error: {issue.Message}");
            return 1;
        }
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var interp  = new Interpreter { ScriptArgs = args.Skip(2).ToList() };
        interp.Execute(program, baseDir);
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

if (args[0] == "check")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: mko check <file.mko>");
        return 1;
    }

    string checkPath = args[1];
    if (!File.Exists(checkPath))
    {
        Console.Error.WriteLine($"mko: file not found: {checkPath}");
        return 1;
    }

    string checkSource;
    try { checkSource = File.ReadAllText(checkPath); }
    catch (Exception ex) { Console.Error.WriteLine($"mko: could not read: {ex.Message}"); return 1; }

    List<CheckIssue> issues;
    try
    {
        var tokens  = new Lexer(checkSource).Tokenize();
        var program = args.Contains("--kernel")
            ? NativeModuleLoader.Load(checkPath)
            : new Parser(tokens).Parse();
        issues = Checker.Check(program);
        if (args.Contains("--kernel"))
            issues.AddRange(KernelProfileChecker.Check(program,
                SystemsTypeChecker.Analyze(program)));
    }
    catch (MakoError ex) { ReportError(ex, checkSource); return 1; }

    if (issues.Count == 0)
    {
        Console.WriteLine($"{checkPath}: no issues found");
        return 0;
    }

    foreach (var issue in issues.OrderBy(i => i.Line))
        Console.WriteLine($"{checkPath}:{issue.Line}: {issue.Message}");
    Console.WriteLine($"{issues.Count} issue{(issues.Count == 1 ? "" : "s")} found");
    return 1;
}

if (args[0] == "ir")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: mko ir <file.mko>");
        return 1;
    }

    string? irPath = ResolvePath(args[1]);
    if (irPath == null)
    {
        Console.Error.WriteLine($"mko: file not found: {args[1]}");
        return 1;
    }

    string irSource;
    try { irSource = File.ReadAllText(irPath); }
    catch (Exception ex) { Console.Error.WriteLine($"mko: could not read: {ex.Message}"); return 1; }
    if (irSource.StartsWith("#!"))
        irSource = irSource[(irSource.IndexOf('\n') + 1)..];

    try
    {
        var program = new Parser(new Lexer(irSource).Tokenize()).Parse();
        var analysis = SystemsTypeChecker.Analyze(program);
        if (analysis.Issues.Count > 0)
        {
            foreach (var issue in analysis.Issues.OrderBy(i => i.Line))
                Console.Error.WriteLine($"{irPath}:{issue.Line}: type error: {issue.Message}");
            return 1;
        }
        Console.Write(TypedHirFormatter.Format(TypedHirLowerer.Lower(program, analysis)));
        return 0;
    }
    catch (MakoError ex) { ReportError(ex, irSource); return 1; }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"mko ir: internal error: {ex.Message}");
        return 1;
    }
}

if (args[0] == "mir")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: mko mir <file.mko> [--opt]");
        return 1;
    }

    string? mirPath = ResolvePath(args[1]);
    if (mirPath == null)
    {
        Console.Error.WriteLine($"mko: file not found: {args[1]}");
        return 1;
    }

    string mirSource;
    try { mirSource = File.ReadAllText(mirPath); }
    catch (Exception ex) { Console.Error.WriteLine($"mko: could not read: {ex.Message}"); return 1; }
    if (mirSource.StartsWith("#!"))
        mirSource = mirSource[(mirSource.IndexOf('\n') + 1)..];

    try
    {
        var program = new Parser(new Lexer(mirSource).Tokenize()).Parse();
        var analysis = SystemsTypeChecker.Analyze(program);
        if (analysis.Issues.Count > 0)
        {
            foreach (var issue in analysis.Issues.OrderBy(i => i.Line))
                Console.Error.WriteLine($"{mirPath}:{issue.Line}: type error: {issue.Message}");
            return 1;
        }
        var hir = TypedHirLowerer.Lower(program, analysis);
        var mir = MirLowerer.Lower(hir);
        var validation = MirValidator.Validate(mir);
        if (validation.Count > 0)
        {
            foreach (var issue in validation)
                Console.Error.WriteLine($"mko mir: validation error: {issue}");
            return 1;
        }
        if (args.Contains("--opt"))
        {
            mir = MirOptimizer.Optimize(mir).Program;
            validation = MirValidator.Validate(mir);
            if (validation.Count > 0)
            {
                foreach (var issue in validation)
                    Console.Error.WriteLine($"mko mir: optimized MIR error: {issue}");
                return 1;
            }
        }
        Console.Write(MirFormatter.Format(mir));
        return 0;
    }
    catch (MakoError ex) { ReportError(ex, mirSource); return 1; }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"mko mir: internal error: {ex.Message}");
        return 1;
    }
}

if (args[0] == "native")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: mko native <file.mko> -o <file.S> [--kernel]");
        return 1;
    }

    string? nativePath = ResolvePath(args[1]);
    var outputIndex = Array.IndexOf(args, "-o");
    if (nativePath == null)
    {
        Console.Error.WriteLine($"mko: file not found: {args[1]}");
        return 1;
    }
    if (outputIndex < 0 || outputIndex + 1 >= args.Length)
    {
        Console.Error.WriteLine("mko native: an output path is required with -o");
        return 1;
    }

    string nativeSource;
    try { nativeSource = File.ReadAllText(nativePath); }
    catch (Exception ex) { Console.Error.WriteLine($"mko: could not read: {ex.Message}"); return 1; }
    if (nativeSource.StartsWith("#!"))
        nativeSource = nativeSource[(nativeSource.IndexOf('\n') + 1)..];

    try
    {
        var program = NativeModuleLoader.Load(nativePath);
        var analysis = SystemsTypeChecker.Analyze(program);
        var issues = new List<CheckIssue>(analysis.Issues);
        if (args.Contains("--kernel"))
            issues.AddRange(KernelProfileChecker.Check(program, analysis));
        if (issues.Count > 0)
        {
            foreach (var issue in issues.OrderBy(i => i.Line))
                Console.Error.WriteLine($"{nativePath}:{issue.Line}: {issue.Message}");
            return 1;
        }

        var mir = MirOptimizer.Optimize(MirLowerer.Lower(
            TypedHirLowerer.Lower(program, analysis))).Program;
        var validation = MirValidator.Validate(mir);
        if (validation.Count > 0)
        {
            foreach (var issue in validation)
                Console.Error.WriteLine($"mko native: MIR validation error: {issue}");
            return 1;
        }
        File.WriteAllText(args[outputIndex + 1], X64AssemblyEmitter.Emit(mir));
        Console.WriteLine($"emitted x86_64 assembly: {args[outputIndex + 1]}");
        return 0;
    }
    catch (MakoError ex) { ReportError(ex, nativeSource); return 1; }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"mko native: {ex.Message}");
        return 1;
    }
}

if (args[0] == "repl")
{
    RunRepl();
    return 0;
}

if (args[0] == "test")
{
    string testDir = args.Length > 1 ? args[1] : "tests";
    if (!Directory.Exists(testDir))
    {
        Console.Error.WriteLine($"mko: test directory not found: {testDir}");
        return 1;
    }

    var files = Directory.GetFiles(testDir, "*.mko", SearchOption.AllDirectories)
        .OrderBy(f => f, StringComparer.Ordinal).ToList();
    if (files.Count == 0)
    {
        Console.WriteLine($"mko: no *.mko files found in {testDir}");
        return 0;
    }

    int passed = 0, failed = 0;
    foreach (var file in files)
    {
        var rel = Path.GetRelativePath(".", file);
        string src;
        try { src = File.ReadAllText(file); }
        catch (Exception ex) { Console.WriteLine($"FAIL  {rel}: could not read: {ex.Message}"); failed++; continue; }

        try
        {
            var tokens  = new Lexer(src).Tokenize();
            var program = new Parser(tokens).Parse();
            var analysis = SystemsTypeChecker.Analyze(program);
            if (analysis.Issues.Count > 0)
                throw new MakoError($"static analysis failed: {analysis.Issues[0].Message}",
                    analysis.Issues[0].Line);
            var loweredHir = TypedHirLowerer.Lower(program, analysis);
            var originalHir = TypedHirFormatter.Format(loweredHir);
            var loweredMir = MirLowerer.Lower(loweredHir);
            var mirIssues = MirValidator.Validate(loweredMir);
            if (mirIssues.Count > 0)
                throw new MakoError($"MIR validation failed: {mirIssues[0]}");
            var originalMir = MirFormatter.Format(loweredMir);
            var optimizedMirProgram = MirOptimizer.Optimize(loweredMir).Program;
            mirIssues = MirValidator.Validate(optimizedMirProgram);
            if (mirIssues.Count > 0)
                throw new MakoError($"optimized MIR validation failed: {mirIssues[0]}");
            var originalOptimizedMir = MirFormatter.Format(optimizedMirProgram);
            var baseDir = Path.GetDirectoryName(Path.GetFullPath(file)) ?? ".";
            new Interpreter().Execute(program, baseDir);

            // Re-run through mko fmt too: a formatter bug that silently changes
            // semantics (e.g. dropping precedence-changing parens) won't show up
            // running the original source — only running the *formatted* output.
            var formatted     = Formatter.Format(src);
            var formattedToks = new Lexer(formatted).Tokenize();
            var formattedProg = new Parser(formattedToks).Parse();
            var formattedAnalysis = SystemsTypeChecker.Analyze(formattedProg);
            if (formattedAnalysis.Issues.Count > 0)
                throw new MakoError($"formatted source failed static analysis: {formattedAnalysis.Issues[0].Message}",
                    formattedAnalysis.Issues[0].Line);
            var formattedLoweredHir = TypedHirLowerer.Lower(formattedProg, formattedAnalysis);
            var formattedHir = TypedHirFormatter.Format(formattedLoweredHir);
            if (originalHir != formattedHir)
                throw new MakoError("formatter changed the program's typed HIR");
            var formattedLoweredMir = MirLowerer.Lower(formattedLoweredHir);
            mirIssues = MirValidator.Validate(formattedLoweredMir);
            if (mirIssues.Count > 0)
                throw new MakoError($"formatted MIR validation failed: {mirIssues[0]}");
            var formattedMir = MirFormatter.Format(formattedLoweredMir);
            if (originalMir != formattedMir)
                throw new MakoError("formatter changed the program's basic-block MIR");
            var formattedOptimizedMir = MirOptimizer.Optimize(formattedLoweredMir).Program;
            mirIssues = MirValidator.Validate(formattedOptimizedMir);
            if (mirIssues.Count > 0)
                throw new MakoError($"formatted optimized MIR validation failed: {mirIssues[0]}");
            if (originalOptimizedMir != MirFormatter.Format(formattedOptimizedMir))
                throw new MakoError("formatter changed the program's optimized MIR");
            new Interpreter().Execute(formattedProg, baseDir);

            Console.WriteLine($"PASS  {rel}");
            passed++;
        }
        catch (MakoError ex)
        {
            var where = ex.Line > 0 ? $" (line {ex.Line})" : "";
            Console.WriteLine($"FAIL  {rel}: {ex.RawMessage}{where}");
            failed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL  {rel}: internal error: {ex.Message}");
            failed++;
        }
    }

    var moduFiles = Directory.GetFiles(testDir, "*.modumako", SearchOption.AllDirectories)
        .OrderBy(f => f, StringComparer.Ordinal);
    foreach (var file in moduFiles)
    {
        var rel = Path.GetRelativePath(".", file);
        try
        {
            string generated = ModuMakoCompiler.Compile(File.ReadAllText(file), rel);
            if (!generated.Contains(": ModuNode", StringComparison.Ordinal))
                throw new MakoError("compiler did not emit a ModuNode class");
            if (!generated.Contains("void Begin(", StringComparison.Ordinal) &&
                !generated.Contains("void TickUpdate(", StringComparison.Ordinal))
                throw new MakoError("compiler did not emit a lifecycle hook");
            Console.WriteLine($"PASS  {rel} (ModuMAKO)");
            passed++;
        }
        catch (MakoError ex)
        {
            var where = ex.Line > 0 ? $" (line {ex.Line})" : "";
            Console.WriteLine($"FAIL  {rel}: {ex.RawMessage}{where}");
            failed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL  {rel}: internal error: {ex.Message}");
            failed++;
        }
    }

    Console.WriteLine();
    Console.WriteLine(failed == 0
        ? $"{passed} passed, {failed} failed"
        : $"{passed} passed, {failed} failed — see FAIL lines above");
    return failed == 0 ? 0 : 1;
}

if (args[0] == "foundry")
{
    string projectArg = args.Where(a => !a.StartsWith('-')).Skip(1).FirstOrDefault() ?? ".";
    bool termOnly = args.Contains("--term");
    try
    {
        var project = Foundry.LoadProject(projectArg);
        if (termOnly)
        {
            Console.WriteLine($"MAKO Foundry — {project.Name} {project.Version}");
            Console.WriteLine($"Entry:  {project.EntryPath}");
            Console.WriteLine($"Output: {project.OutputPath}");
            Console.WriteLine();
            Console.WriteLine("Targets:");
            foreach (var target in Foundry.Targets)
                Console.WriteLine($"  {target.Id,-14} {target.Status,-8} {target.Name} ({target.Artifact})");
            Console.WriteLine();
            Console.WriteLine("Build with: mko build <project> --target linux-x64");
            return 0;
        }
        FoundryGui.Run(projectArg);
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"mko foundry: {ex.Message}");
        return 1;
    }
}

if (args[0] == "build")
{
    string projectArg = ".";
    string? targetArg = null;
    string? outputArg = null;
    for (int i = 1; i < args.Length; i++)
    {
        if (args[i] == "--target" && i + 1 < args.Length) { targetArg = args[++i]; continue; }
        if (args[i] == "--output" && i + 1 < args.Length) { outputArg = args[++i]; continue; }
        if (!args[i].StartsWith('-')) projectArg = args[i];
    }
    try
    {
        var project = Foundry.LoadProject(projectArg);
        if (outputArg != null) project.Output = Path.GetFullPath(outputArg);
        string target = targetArg ?? project.DefaultTarget;
        var result = Foundry.Build(project, target, Console.WriteLine);
        if (!result.Success)
        {
            Console.Error.WriteLine($"mko build: {result.Message}");
            return 1;
        }
        Console.WriteLine($"Artifact: {result.ArtifactPath}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"mko build: {ex.Message}");
        return 1;
    }
}

if (args[0] == "get")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: mko get <package>  [or]  mko get <package> <github:User/Repo[@ref]>");
        return 1;
    }
    var pkgName = args[1];
    if (args.Length >= 3)
        PackageManager.Register(pkgName, args[2]); // raw source, "@ref" (if any) split at clone time
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

if (args[0] == "update")
{
    var targets = args.Length >= 2
        ? new[] { args[1] }
        : PackageManager.ListInstalled().Select(p => p.Name).ToArray();

    if (targets.Length == 0)
    {
        Console.WriteLine("mko: no packages installed.");
        return 0;
    }

    bool anyFailed = false;
    foreach (var name in targets)
    {
        try
        {
            PackageManager.Update(name);
            Console.WriteLine($"mko: '{name}' updated.");
        }
        catch (MakoError ex)
        {
            Console.Error.WriteLine($"mko: {ex.RawMessage}");
            anyFailed = true;
        }
    }
    return anyFailed ? 1 : 0;
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
        {
            var locked = PackageManager.GetLockEntry(name);
            string suffix = locked != null
                ? locked.Ref != null
                    ? $"  @{locked.Ref} ({locked.ResolvedCommit[..Math.Min(8, locked.ResolvedCommit.Length)]})"
                    : $"  ({locked.ResolvedCommit[..Math.Min(8, locked.ResolvedCommit.Length)]})"
                : "";
            Console.WriteLine($"  {name}{suffix}  ({path})");
        }
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

if (args[0] == "search")
{
    bool termOnly = args.Contains("--term");
    string? query = args.Where(a => !a.StartsWith('-')).Skip(1).FirstOrDefault();

    // A "github:User/Repo" query is a targeted lookup of one specific repo's
    // mako.json manifest, not a fuzzy match against the local registry —
    // same live-fetch path as `mko info github:...` below.
    if (query != null && query.StartsWith("github:", StringComparison.OrdinalIgnoreCase))
        return RunGithubLookup(query, termOnly);

    if (!termOnly)
    {
        try { PackageBrowserGui.Run(query); return 0; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"mko: couldn't open the graphical browser ({ex.Message}) — falling back to text.");
        }
    }

    PrintSearchResults(query);
    return 0;
}

if (args[0] == "info")
{
    if (args.Length < 2 || args[1].StartsWith('-'))
    {
        Console.Error.WriteLine("Usage: mko info <package> [--term]");
        return 1;
    }
    string pkgQuery = args[1];
    bool termOnly = args.Contains("--term");

    if (pkgQuery.StartsWith("github:", StringComparison.OrdinalIgnoreCase))
        return RunGithubLookup(pkgQuery, termOnly);

    var entry = PackageRegistry.Find(pkgQuery);
    if (entry == null)
    {
        Console.Error.WriteLine($"mko: no package named '{pkgQuery}' in the registry.");
        Console.Error.WriteLine($"  Tip: run 'mko search {pkgQuery}' to look for something close, " +
                                 "or 'mko info github:User/Repo' to look up a specific repo.");
        return 1;
    }

    if (!termOnly)
    {
        try { PackageBrowserGui.Run(null, selected: entry.Name); return 0; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"mko: couldn't open the graphical browser ({ex.Message}) — falling back to text.");
        }
    }

    PrintPackageInfo(entry);
    return 0;
}

Console.Error.WriteLine($"mko: unknown command '{args[0]}'. Run 'mko help'.");
return 1;

// ─────────────────────────────────────────────────────────────────────────────

static void RunRepl()
{
    Console.WriteLine("MAKO 0.1.0 REPL  (Ctrl+C or 'exit' to quit)");
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

        // 'fn Name(...)'/'struct Name {...}' declarations must reach the
        // parser unwrapped — they're only valid at top level, never inside a
        // main() body — so they parse and register into the interpreter's
        // persistent _funcs/_structs, visible on every later line, same as a
        // real REPL session. 'fn(x) => ...' lambdas are expressions, not
        // declarations (no name before the '('), so they still go through
        // the wrap path below along with everything else (print, if, while,
        // assignments, ...). Tokenizing first (rather than checking string
        // prefixes) means the real lexer decides what's a keyword — no risk
        // of a variable named e.g. "fnord" being misread as an "fn" decl.
        var lineTokens = new Lexer(trimmed).Tokenize();
        bool isTopLevelDecl =
            (lineTokens.Count > 0 && lineTokens[0].Type == TokenType.Struct) ||
            (lineTokens.Count > 2 && lineTokens[0].Type == TokenType.Fn
                                   && lineTokens[1].Type == TokenType.Identifier);
        var source = isTopLevelDecl ? trimmed : WrapForRepl(trimmed);

        try
        {
            var tokens  = isTopLevelDecl ? lineTokens : new Lexer(source).Tokenize();
            var program = new Parser(tokens, interpreter.KnownStructNames).Parse();
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

// Cross-checks a registry entry against what's actually installed/available
// locally (PackageManager) so "available" doesn't just mean "in the registry."
static string InstallStatusLine(RegistryEntry e)
{
    if (e.Status == "planned") return "not yet available";
    if (PackageManager.NativePackages.Contains(e.Name)) return "built in — no install needed";
    var installed = PackageManager.ListInstalled().Any(p =>
        string.Equals(p.Name, e.Name, StringComparison.OrdinalIgnoreCase));
    if (installed) return "installed";
    var getCmd = e.Kind == "github" && e.Source != null
        ? $"mko get {e.Name} {e.Source}"
        : $"mko get {e.Name}";
    return $"not installed — run '{getCmd}'";
}

// Fetches a github:User/Repo package's mako.json manifest live and shows it,
// same as a registry entry — the GUI/--term split mirrors search/info above.
static int RunGithubLookup(string source, bool termOnly)
{
    RegistryEntry entry;
    Console.Error.WriteLine($"mko: fetching {source} ...");
    try
    {
        entry = GithubPackageLookup.Fetch(source);
    }
    catch (MakoError ex)
    {
        Console.Error.WriteLine($"mko: {ex.RawMessage}");
        return 1;
    }

    if (!termOnly)
    {
        try { PackageBrowserGui.RunSingle(entry); return 0; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"mko: couldn't open the graphical browser ({ex.Message}) — falling back to text.");
        }
    }

    PrintPackageInfo(entry);
    return 0;
}

static void PrintSearchResults(string? query)
{
    var results = PackageRegistry.Search(query).ToList();
    if (results.Count == 0)
    {
        Console.WriteLine(query == null
            ? "mko: the registry is empty."
            : $"mko: no packages found matching '{query}'.");
        return;
    }

    Console.WriteLine(query == null ? "All known packages:" : $"Packages matching '{query}':");
    Console.WriteLine();
    foreach (var e in results)
    {
        var badge = e.Status == "planned" ? " [planned]" : "";
        Console.WriteLine($"  {e.Name}{badge}");
        Console.WriteLine($"    {e.Description}");
    }
    Console.WriteLine();
    Console.WriteLine("Run 'mko info <name>' for details on any of these.");
}

static void PrintPackageInfo(RegistryEntry e)
{
    Console.WriteLine($"{e.Name}  ({e.Kind}, {e.Status})");
    Console.WriteLine();
    Console.WriteLine(e.Description);
    Console.WriteLine();
    Console.WriteLine($"Status: {InstallStatusLine(e)}");
    if (e.Usage != null) Console.WriteLine($"Usage:  {e.Usage}");
    if (e.Source != null) Console.WriteLine($"Source: {e.Source}");
    if (e.Docs != null) Console.WriteLine($"Docs:   {e.Docs}");
    if (e.Note != null) Console.WriteLine($"Note:   {e.Note}");

    if (e.Versions != null && e.Versions.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Versions:");
        foreach (var v in e.Versions)
        {
            var badge = v.Status == "planned" ? " [planned]" : "";
            Console.WriteLine($"  {v.Name}{badge}");
            Console.WriteLine($"    {v.Description}");
            if (v.Usage != null) Console.WriteLine($"    Usage: {v.Usage}");
            if (v.Note != null) Console.WriteLine($"    Note:  {v.Note}");
        }
    }
}

static void PrintHelp()
{
    Console.WriteLine("""
    MAKO 0.1.0 — a simple, sharp programming language

    Usage:
      mko run <file.mko>            Run a MAKO script
      mko fmt <file.mko>            Format a MAKO file in-place
      mko fmt <file.mko> --check   Check if a file is formatted
      mko check <file.mko>          Check static types and lint a file
      mko modumako <file> [-o out]  Compile ModuMAKO to ModuCPP
        --kernel                    Enforce the freestanding kernel-safe subset
      mko ir <file.mko>             Print typed high-level compiler IR
      mko mir <file.mko> [--opt]    Print validated basic-block IR; optionally optimize
      mko native <file.mko> -o <file.S> [--kernel]
                                     Emit freestanding System V x86_64 assembly
      mko repl                      Start interactive REPL
      mko test [dir]                 Run *.mko files under tests/ (or [dir])
      mko foundry [project]          Open the MakoUI game builder
      mko foundry [project] --term   Show Foundry project and target status
      mko build [project]            Build with the project's Foundry target
        --target linux-x64           Override the target
        --output <dir>               Override the output directory
      mko get <pkg> [github:U/R[@ref]]  Install a package, optionally pinned to a
                                     tag, branch, or commit
      mko update [pkg]              Re-fetch a package (or all of them) to
                                     whatever its pin/branch currently points at
      mko list                      List installed packages, with pin/commit
      mko cache clear [pkg]         Remove cached package(s)
      mko search [query] [--term]   Browse/search known packages (opens a GUI window;
                                     --term prints plain text instead)
      mko info <pkg> [--term]       Show details on one package
      mko search github:U/R         Fetch a repo's mako.json and preview it
      mko info github:U/R           (same, single-package view) — no install
      mko version                   Show version
      mko help                      Show this help

    Language features:
      Variables, arithmetic (+  -  *  /  %)
      Compound assignment (+=  -=  *=  /=)
      Strings, numbers, booleans, none, lists, dicts
      const  — immutable top-level or block constants
      if / else if / else
      while  /  for item in list  /  for key in dict
      break  /  continue
      fn / return  (recursive, closures via lambdas)
      fn(x) => expr  /  fn(x) { ... }   — lambdas
      try / catch  — error handling
      and / or / not  (short-circuit logical)
      print  /  printnl  /  input  /  run

    Standard library:
      String: len  upper  lower  trim  contains  slice
              starts_with  ends_with  replace  split  join
      List:   len  push  pop  first  last  reverse  has  slice
      Dict:   keys  values  has  get  remove  merge
      Higher-order: map  filter  reduce  sort_by  each  any  all
      Math:   abs  floor  ceil  sqrt  pow  max  min  round
              clamp  lerp  sign  sin  cos  tan  atan2  pi
      Game:   dist  rects_overlap  circles_overlap  point_in_rect
              find_path  line_of_sight  (A* + visibility for AI)
      I/O:    read  write  append  exists  delete  lines
      System: time  random  random_int  sleep  env  args
      Data:   json_encode  json_decode
      Util:   type  to_num  to_str  assert

    Packages:
      using MakoUI;                          desktop UI (Dear ImGui) — also usable
                                              standalone as "MakoGUI" (no game loop)
      using Mako2D; / using Mako3D;          2D / 3D game rendering
      using Physics2D;                       2D rigid bodies and collision
      using Inputs; / using Audio;           input, sound + synth
      using Net;                             HTTP requests + JSON
      using mylib from "github:User/Repo";   fetch from GitHub

      Not sure what's available? Run 'mko search' to browse.

    Example:
      mko hello.mko
      mko run ~/scripts/hello.mko
    """);
}

// Resolve a script path: try as-is, then search ~/.local/share/mko/
static string? ResolvePath(string input)
{
    if (File.Exists(input)) return input;

    var mkoData = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".local", "share", "mko");

    // Search subdirectories: mko/<input>, mko/examples/<input>, mko/scripts/<input>
    foreach (var prefix in new[] { "", "examples", "scripts" })
    {
        var candidate = prefix == ""
            ? Path.Combine(mkoData, input)
            : Path.Combine(mkoData, prefix, input);
        if (File.Exists(candidate)) return candidate;

        // Also try with .mko extension appended if not already present
        if (!input.EndsWith(".mko", StringComparison.OrdinalIgnoreCase))
        {
            var withExt = candidate + ".mko";
            if (File.Exists(withExt)) return withExt;
        }
    }
    return null;
}
