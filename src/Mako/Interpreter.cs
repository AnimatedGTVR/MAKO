namespace Mako;

/// A first-class lambda / closure value.
sealed class MakoFn(List<string> parms, List<Statement> body, Dictionary<string, object?> captured)
{
    public List<string>             Params   { get; } = parms;
    public List<Statement>          Body     { get; } = body;
    public Dictionary<string,object?> Captured { get; } = captured;
    public override string ToString() => $"fn({string.Join(", ", Params)})"  ;
}

/// The embedding contract: what a host application (e.g. a game engine
/// hosting MAKO as its scripting layer) hands to an Interpreter so scripts
/// can call back into the host. This is "ctx" — the object an embedder
/// builds once, registers its own engine calls onto, and passes into every
/// Interpreter it creates.
///
/// Design mirrors MAKO's own native packages (Physics3D, Mako3D, ...):
/// host functions are registered under "Namespace.function" names and
/// dispatch through the exact same List<object?> -> object? calling
/// convention every built-in native function already uses — a script
/// calling a host function looks identical to calling a built-in one, and
/// a host's namespace behaves like any other `using X;` package (see
/// Interpreter.RegisterHostPackage / the `using` handling in ExecuteCore).
///
///   var ctx = new MakoHostContext();
///   ctx.RegisterFunction("Engine.spawn", (double x, double y) => (double)engine.Spawn(x, y));
///   ctx.RegisterFunction("Engine.delta_time", () => (double)engine.DeltaTime);
///   ctx.RegisterPackage("Engine"); // makes `using Engine;` valid in scripts
///   var interp = new Interpreter(ctx);
///   interp.Execute(program);
public sealed class MakoHostContext
{
    private readonly Dictionary<string, Func<List<object?>, object?>> _functions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _packages = new(StringComparer.OrdinalIgnoreCase);

    /// Registers a host callback under a fully-qualified name, e.g.
    /// "Engine.spawn" — scripts call it exactly like a built-in native
    /// function: Engine.spawn(x, y, z). Arguments arrive as MAKO runtime
    /// values (double | string | bool | List<object?> | Dictionary<string,
    /// object?> | null); return the same types back.
    ///
    /// This is the escape hatch for anything the typed overloads below
    /// don't cover (variadic arity, lists/dicts, custom validation) — for
    /// the common case of "a handful of numbers/strings/bools in, one
    /// value out," prefer a typed overload instead: it reads like an
    /// ordinary C# method and needs no manual (double)args[0]! casting.
    public void RegisterFunction(string qualifiedName, Func<List<object?>, object?> fn)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName) || !qualifiedName.Contains('.'))
            throw new ArgumentException("qualifiedName must be \"Namespace.function\", e.g. \"Engine.spawn\"", nameof(qualifiedName));
        _functions[qualifiedName] = fn ?? throw new ArgumentNullException(nameof(fn));
    }

    // ── Typed overloads ──────────────────────────────────────────────────
    //
    // MAKO's own rule: if it's hard to type, it's hard to understand. The
    // raw RegisterFunction(name, args => (double)args[0]! ...) form makes
    // every host function pay for arity checking and manual casting by
    // hand. These overloads take an ordinary strongly-typed C# delegate —
    // Func<double, double>, Action<string>, etc. — and do the arity check
    // and boxing/unboxing once, here, instead of in every host callback.
    //
    // Covers 0–3 typed parameters (double/string/bool), with or without a
    // return value, which is every host function actually seen in
    // practice so far (spawn(x, y), delta_time(), etc.). A host with a
    // genuinely different shape (more parameters, lists/dicts, variadic)
    // still has the raw RegisterFunction overload above.

    public void RegisterFunction(string qualifiedName, Func<object?> fn) =>
        RegisterFunction(qualifiedName, args => { RequireArity(qualifiedName, args, 0); return fn(); });

    public void RegisterFunction(string qualifiedName, Action fn) =>
        RegisterFunction(qualifiedName, args => { RequireArity(qualifiedName, args, 0); fn(); return null; });

    public void RegisterFunction<T1>(string qualifiedName, Func<T1, object?> fn) =>
        RegisterFunction(qualifiedName, args =>
        {
            RequireArity(qualifiedName, args, 1);
            return fn(Cast<T1>(qualifiedName, args, 0));
        });

    public void RegisterFunction<T1>(string qualifiedName, Action<T1> fn) =>
        RegisterFunction(qualifiedName, args =>
        {
            RequireArity(qualifiedName, args, 1);
            fn(Cast<T1>(qualifiedName, args, 0));
            return null;
        });

    public void RegisterFunction<T1, T2>(string qualifiedName, Func<T1, T2, object?> fn) =>
        RegisterFunction(qualifiedName, args =>
        {
            RequireArity(qualifiedName, args, 2);
            return fn(Cast<T1>(qualifiedName, args, 0), Cast<T2>(qualifiedName, args, 1));
        });

    public void RegisterFunction<T1, T2>(string qualifiedName, Action<T1, T2> fn) =>
        RegisterFunction(qualifiedName, args =>
        {
            RequireArity(qualifiedName, args, 2);
            fn(Cast<T1>(qualifiedName, args, 0), Cast<T2>(qualifiedName, args, 1));
            return null;
        });

    public void RegisterFunction<T1, T2, T3>(string qualifiedName, Func<T1, T2, T3, object?> fn) =>
        RegisterFunction(qualifiedName, args =>
        {
            RequireArity(qualifiedName, args, 3);
            return fn(Cast<T1>(qualifiedName, args, 0), Cast<T2>(qualifiedName, args, 1), Cast<T3>(qualifiedName, args, 2));
        });

    public void RegisterFunction<T1, T2, T3>(string qualifiedName, Action<T1, T2, T3> fn) =>
        RegisterFunction(qualifiedName, args =>
        {
            RequireArity(qualifiedName, args, 3);
            fn(Cast<T1>(qualifiedName, args, 0), Cast<T2>(qualifiedName, args, 1), Cast<T3>(qualifiedName, args, 2));
            return null;
        });

    private static void RequireArity(string qualifiedName, List<object?> args, int expected)
    {
        if (args.Count != expected)
            throw new MakoError($"{qualifiedName}() expects {expected} argument(s), got {args.Count}");
    }

    // Only double/string/bool are supported here — MAKO's own runtime
    // value types minus list/dict/null, which the raw List<object?>
    // overload handles instead. A bad host-side T (or a script passing
    // the wrong argument type) throws a clear, host-attributable error
    // rather than an opaque InvalidCastException from deep inside a
    // lambda.
    private static T Cast<T>(string qualifiedName, List<object?> args, int index)
    {
        object? value = args[index];
        object converted = typeof(T) switch
        {
            var t when t == typeof(double) => value is double d ? d
                : throw new MakoError($"{qualifiedName}() argument {index + 1} must be a number"),
            var t when t == typeof(string) => value is string s ? s
                : throw new MakoError($"{qualifiedName}() argument {index + 1} must be a string"),
            var t when t == typeof(bool) => value is bool b ? b
                : throw new MakoError($"{qualifiedName}() argument {index + 1} must be a bool"),
            _ => throw new ArgumentException(
                $"RegisterFunction typed overloads only support double/string/bool — use the " +
                $"List<object?> overload for '{qualifiedName}' instead", nameof(T)),
        };
        return (T)converted;
    }

    /// Marks a namespace as a valid `using X;` target in scripts, even
    /// though it isn't one of MAKO's own built-in packages. Call this once
    /// per namespace you register functions under — RegisterFunction alone
    /// makes the function callable, but `using Engine;` would still fail
    /// with "package not found" without a matching RegisterPackage("Engine").
    public void RegisterPackage(string name) => _packages.Add(name);

    internal bool IsHostPackage(string name) => _packages.Contains(name);

    internal bool TryGetFunction(string qualifiedName, out Func<List<object?>, object?> fn) =>
        _functions.TryGetValue(qualifiedName, out fn!);
}

/// Tree-walk interpreter.
/// Runtime value types:  string | double | bool | List<object?> | null
public class Interpreter
{
    // Control flow (return/break/continue) is tracked with a plain flag rather
    // than thrown exceptions — in .NET, throwing captures a stack trace on
    // every throw, which is drastically more expensive than a field check
    // and made recursive MAKO functions (e.g. fib) needlessly slow.
    private enum Flow { None, Return, Break, Continue }

    private readonly Dictionary<string, FnDecl> _funcs = new();
    private readonly Dictionary<string, StructDecl> _structs = new();

    /// Struct names known so far — used by the REPL to tell a fresh Parser
    /// about structs declared on earlier lines (see the Parser ctor overload).
    public IEnumerable<string> KnownStructNames => _structs.Keys;
    private MakoUI? _ui;
    private bool    _rayActive;
    private bool    _ray2DActive;
    private bool    _ray3DActive;
    private bool    _physics2DActive;
    private bool    _physics3DActive;
    private bool    _inputsActive;
    private bool    _modelsActive;
    private bool    _playersActive;
    private bool    _controllersActive;
    private bool    _saveActive;
    private bool    _anixActive;
    private bool    _audioActive;
    private bool    _netActive;
    private bool    _roomActive;
    private bool    _systemActive;
    private bool    _fontActive;
    public  List<string> ScriptArgs { get; set; } = [];

    /// The embedding host's context, if this interpreter was constructed by
    /// a host application rather than the mko CLI. Null for ordinary script
    /// runs — every host-function/host-package check below is a no-op when
    /// this is null, so embedding support adds no behavior change for the
    /// standalone CLI path.
    private readonly MakoHostContext? _host;
    private readonly HashSet<string> _hostPackagesActive = new(StringComparer.OrdinalIgnoreCase);

    public Interpreter() { }

    /// Constructs an interpreter bound to a host embedding context — see
    /// MakoHostContext's doc comment for the full embedding contract this
    /// is part of.
    public Interpreter(MakoHostContext host) => _host = host;

    // Control-flow state for return/break/continue — see the Flow enum above.
    private Flow    _flow;
    private object? _returnValue;

    // Each scope holds variable values and a set of const names.
    private sealed class Scope
    {
        public Dictionary<string, object?> Vars   { get; } = new();
        public HashSet<string>             Consts { get; } = new();
    }
    private readonly List<Scope> _scopes = [new()];
    // Index of the first scope owned by each active function/lambda call.
    // Locals in an earlier call frame are not dynamically visible to a
    // callee; only the current frame and the true global scope are visible.
    private readonly Stack<int> _callScopeBases = new();

    // ── Entry point ───────────────────────────────────────────────────────────

    /// Public embedding entry point: parses and runs MAKO source text
    /// directly, so a host application never needs to touch MAKO's
    /// internal AST/lexer/parser types — those stay internal to this
    /// assembly. This is the method a host (e.g. a game engine embedding
    /// MAKO via a MakoHostContext) calls to run a script.
    ///
    ///   var ctx = new MakoHostContext();
    ///   ctx.RegisterFunction("Engine.spawn", (double x, double y) => (double)engine.Spawn(x, y));
    ///   ctx.RegisterPackage("Engine");
    ///   var interp = new Interpreter(ctx);
    ///   interp.Run(scriptSource);
    ///
    /// baseDir resolves relative `use "file.mko"` imports and asset paths
    /// the same way the mko CLI resolves them relative to the script's own
    /// directory — pass the host project's script directory if scripts use
    /// local imports.
    public void Run(string source, string baseDir = "")
    {
        var tokens = new Lexer(source).Tokenize();
        var program = new Parser(tokens).Parse();
        Execute(program, baseDir);
    }

    internal void Execute(ProgramNode program, string baseDir = "")
    {
        try { ExecuteCore(program, baseDir); }
        finally
        {
            _ui?.Dispose(); _ui = null;
            // GPU resources must be freed while the window (GL context) is
            // still alive, and CloseWindow must run at most once per process —
            // raylib tears down GL/GLFW unconditionally, so a second close
            // (script already called close()) segfaults on exit.
            bool hadWindow = _rayActive || _ray2DActive || _ray3DActive;
            if (_ray2DActive) MakoRay2D.UnloadAll();
            if (_ray3DActive) MakoRay3D.UnloadAll();
            MakoModels.Reset();
            MakoSave.Reset();
            MakoAnix.Reset();
            if (_audioActive) MakoAudio.UnloadAll();
            if (_roomActive) MakoRoom.UnloadAll();
            if (_physics2DActive) MakoPhysics2D.ResetAll();
            if (_physics3DActive) MakoPhysics3D.ResetAll();
            if (hadWindow && MakoRay.IsWindowReady())
                MakoRay.CloseWindow();
            _rayActive = _ray2DActive = _ray3DActive = _physics2DActive = _physics3DActive = _audioActive = false;
            _modelsActive = _playersActive = false;
            _controllersActive = _saveActive = false;
            _anixActive = false;
        }
    }

    /// REPL entry — runs in the persistent interpreter state, prints expression results.
    internal void ExecuteRepl(ProgramNode program)
    {
        // Register any new functions declared in this line.
        foreach (var fn in program.Functions)
            _funcs[fn.Name] = fn;

        foreach (var sd in program.Structs)
            _structs[sd.Name] = sd;

        foreach (var (cname, cexpr) in program.Constants)
        {
            var cval = Eval(cexpr);
            SetVar(cname, cval);
            _scopes[0].Consts.Add(cname);
        }

        foreach (var stmt in program.Body)
        {
            // Auto-print expression statements that produce a value.
            if (stmt is ExprStmt es)
            {
                var val = Eval(es.Value);
                if (val != null)
                    Console.WriteLine(Stringify(val));
            }
            else
            {
                RunStatement(stmt);
                if (_flow != Flow.None) break;
            }
        }
        // The REPL reuses this Interpreter across lines, so a stray
        // return/break/continue must not leak into the next line typed.
        if (_flow != Flow.None) ResetFlowAtTopLevel();
    }

    private void ExecuteCore(ProgramNode program, string baseDir)
    {
        MakoAssets.BaseDir = baseDir;

        // ── using PackageName [from "source"] — named packages ───────────────
        foreach (var pkg in program.Packages)
        {
            // Host-registered packages (see MakoHostContext) short-circuit
            // entirely before PackageManager gets involved — they're not on
            // disk and not in the git-clone registry, so Ensure() would
            // otherwise reject them as unknown.
            if (_host != null && _host.IsHostPackage(pkg.Name))
            {
                _hostPackagesActive.Add(pkg.Name);
                continue;
            }

            if (pkg.Source != null)
                PackageManager.RegisterSource(pkg.Name, pkg.Source);

            PackageManager.Ensure(pkg.Name);

            if (PackageManager.NativePackages.Contains(pkg.Name))
            {
                if (pkg.Name.Equals("MakoUI", StringComparison.OrdinalIgnoreCase) ||
                    pkg.Name.Equals("IMGUI",  StringComparison.OrdinalIgnoreCase))
                    _ui ??= new MakoUI();

                if (pkg.Name.Equals("MakoRay", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var (k, v) in MakoRay.Colors)
                        SetVar($"MakoRay.{k}", v);
                    _rayActive = true;
                }

                if (pkg.Name.Equals("Mako2D", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var (k, v) in MakoRay2D.Colors)
                        SetVar($"Mako2D.{k}", v);
                    _ray2DActive = true;
                }

                if (pkg.Name.Equals("Mako3D", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var (k, v) in MakoRay3D.Colors)
                        SetVar($"Mako3D.{k}", v);
                    _ray3DActive = true;
                }

                if (pkg.Name.Equals("Models", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var (k, v) in MakoRay3D.Colors) SetVar($"Mako3D.{k}", v);
                    _modelsActive = true;
                    _ray3DActive = true;
                }

                if (pkg.Name.Equals("Players", StringComparison.OrdinalIgnoreCase))
                    _playersActive = true;

                if (pkg.Name.Equals("Controllers", StringComparison.OrdinalIgnoreCase))
                    _controllersActive = true;

                if (pkg.Name.Equals("Save", StringComparison.OrdinalIgnoreCase))
                    _saveActive = true;

                if (pkg.Name.Equals("ANIX", StringComparison.OrdinalIgnoreCase))
                    _anixActive = true;

                if (pkg.Name.Equals("Physics2D", StringComparison.OrdinalIgnoreCase))
                    _physics2DActive = true;

                if (pkg.Name.Equals("Physics3D", StringComparison.OrdinalIgnoreCase))
                    _physics3DActive = true;

                if (pkg.Name.Equals("JoltPhysics", StringComparison.OrdinalIgnoreCase) ||
                    pkg.Name.Equals("PhysX", StringComparison.OrdinalIgnoreCase) ||
                    pkg.Name.Equals("BulletPhysics", StringComparison.OrdinalIgnoreCase))
                {
                    string backend = pkg.Name.Equals("JoltPhysics", StringComparison.OrdinalIgnoreCase) ? "jolt" :
                        pkg.Name.Equals("PhysX", StringComparison.OrdinalIgnoreCase) ? "physx" : "bullet";
                    MakoPhysics3D.SelectDefaultBackend(pkg.Name, backend);
                    _physics3DActive = true;
                }

                if (pkg.Name.Equals("Box2D", StringComparison.OrdinalIgnoreCase))
                {
                    var backend = PhysicsBackends.Find("box2d");
                    if (!backend.Installed) throw new MakoError($"using Box2D: {backend.Status}");
                    _physics2DActive = true;
                }

                if (pkg.Name.Equals("Inputs", StringComparison.OrdinalIgnoreCase))
                    _inputsActive = true;

                if (pkg.Name.Equals("Audio", StringComparison.OrdinalIgnoreCase))
                    _audioActive = true;

                if (pkg.Name.Equals("Net", StringComparison.OrdinalIgnoreCase))
                    _netActive = true;

                if (pkg.Name.Equals("Room", StringComparison.OrdinalIgnoreCase))
                    _roomActive = true;

                if (pkg.Name.Equals("System", StringComparison.OrdinalIgnoreCase))
                    _systemActive = true;

                if (pkg.Name.Equals("Font", StringComparison.OrdinalIgnoreCase))
                    _fontActive = true;

                continue;
            }

            var indexPath = PackageManager.IndexPath(pkg.Name);
            if (indexPath == null)
                throw new MakoError($"package '{pkg.Name}' has no index.mko");

            var pkgSrc = File.ReadAllText(indexPath);
            ProgramNode pkgAst;
            try { pkgAst = new Parser(new Lexer(pkgSrc).Tokenize()).Parse(); }
            catch (MakoError e) when (e.SourcePath is null)
            { throw new MakoError(e.RawMessage, e.Line, e.Col, e.Length) { SourcePath = indexPath }; }

            var pkgNs = pkgAst.Namespace
                ?? throw new MakoError($"package '{pkg.Name}' (index.mko) must declare a namespace");
            foreach (var fn in pkgAst.Functions)
            {
                fn.Source = indexPath;
                _funcs[$"{pkgNs}.{fn.Name}"] = fn;
            }
        }

        foreach (var importPath in program.Imports)
        {
            var fullPath = Path.IsPathRooted(importPath)
                ? importPath
                : Path.Combine(string.IsNullOrEmpty(baseDir) ? "." : baseDir, importPath);

            if (!File.Exists(fullPath))
                throw new MakoError($"cannot find module '{importPath}'");

            var src = File.ReadAllText(fullPath);
            ProgramNode module;
            try { module = new Parser(new Lexer(src).Tokenize()).Parse(); }
            catch (MakoError e) when (e.SourcePath is null)
            {
                // Tag with the module path so the CLI shows a snippet from the
                // module's source, not the importing file's.
                throw new MakoError(e.RawMessage, e.Line, e.Col, e.Length)
                      { SourcePath = fullPath };
            }
            var modNs = module.Namespace
                ?? throw new MakoError($"module '{importPath}' must declare a namespace");

            foreach (var fn in module.Functions)
            {
                fn.Source = fullPath;
                _funcs[$"{modNs}.{fn.Name}"] = fn;
            }
        }

        foreach (var fn in program.Functions)
        {
            _funcs[fn.Name] = fn;
            if (program.Namespace is { } ns)
                _funcs[$"{ns}.{fn.Name}"] = fn;
        }

        foreach (var sd in program.Structs)
            _structs[sd.Name] = sd;

        // Top-level const declarations — evaluated once, marked immutable globally.
        foreach (var (cname, cexpr) in program.Constants)
        {
            var cval = Eval(cexpr);
            SetVar(cname, cval);
            _scopes[0].Consts.Add(cname);
        }

        RunBlock(program.Body);
        ResetFlowAtTopLevel();
    }

    /// A stray break/continue that escapes all the way to the top of the
    /// script or a function body means it was used outside any loop —
    /// surface that as a real error instead of silently swallowing it.
    private void ResetFlowAtTopLevel()
    {
        var leaked = _flow;
        _flow = Flow.None;
        if (leaked == Flow.Break)    throw new MakoError("'break' used outside of a loop");
        if (leaked == Flow.Continue) throw new MakoError("'continue' used outside of a loop");
    }

    // ── Statements ────────────────────────────────────────────────────────────

    /// Runs a list of statements in sequence, stopping early the moment a
    /// return/break/continue is signalled so it can propagate to the nearest
    /// loop or function-call boundary that handles it.
    private void RunBlock(List<Statement> stmts)
    {
        foreach (var s in stmts)
        {
            RunStatement(s);
            if (_flow != Flow.None) return;
        }
    }

    /// Runs a statement, attaching the statement's source position to any
    /// runtime error that doesn't already carry a more precise one.
    private void RunStatement(Statement stmt)
    {
        try { RunStatementCore(stmt); }
        catch (MakoError e) when (e.Line == 0 && stmt.Line > 0)
        {
            int len = stmt switch
            {
                AssignStmt a       => a.Name.Length,
                IndexAssignStmt ia => ia.Name.Length,
                ForStmt            => 3,
                _                  => 1,
            };
            throw new MakoError(e.RawMessage, stmt.Line, stmt.Col, len) { Hint = e.Hint };
        }
    }

    private void RunStatementCore(Statement stmt)
    {
        switch (stmt)
        {
            case PrintStmt p:
                Console.WriteLine(Stringify(Eval(p.Value)));
                break;

            case PrintnlStmt p:
                Console.Write(Stringify(Eval(p.Value)));
                break;

            case AssignStmt a:
                SetVar(a.Name, Eval(a.Value));
                break;

            case FieldAssignStmt fa:
                var faTarget = Eval(fa.Target);
                if (faTarget is not Dictionary<string, object?> faDict)
                    throw new MakoError($"cannot assign field '{fa.Field}' on {TypeName(faTarget)} — only dicts/structs have fields");
                faDict[fa.Field] = Eval(fa.Value);
                break;

            case ConstStmt c:
                var constVal = Eval(c.Value);
                if (_scopes[^1].Consts.Contains(c.Name))
                    throw new MakoError($"'{c.Name}' is already declared as const");
                _scopes[^1].Consts.Add(c.Name);
                _scopes[^1].Vars[c.Name] = constVal;
                break;

            case IndexAssignStmt ia:
                var indexTarget = GetVar(ia.Name);
                // Walk through all but the last index to reach the innermost container.
                for (int ii = 0; ii < ia.Indices.Count - 1; ii++)
                {
                    var key = Eval(ia.Indices[ii]);
                    indexTarget = indexTarget switch
                    {
                        List<object?> l => l[NormalizeIndex((int)ToNumber(key), l.Count)],
                        Dictionary<string, object?> d => d.TryGetValue(Stringify(key), out var v) ? v
                            : throw new MakoError($"key '{Stringify(key)}' not found in dict"),
                        _ => throw new MakoError(
                            $"cannot index into {TypeName(indexTarget)} — '{ia.Name}' chain must be lists or dicts"),
                    };
                }
                var lastIndex = Eval(ia.Indices[^1]);
                if (indexTarget is List<object?> lst)
                    lst[NormalizeIndex((int)ToNumber(lastIndex), lst.Count)] = Eval(ia.Value);
                else if (indexTarget is Dictionary<string, object?> dictTarget)
                    dictTarget[Stringify(lastIndex)] = Eval(ia.Value);
                else
                    throw new MakoError(
                        $"cannot assign by index into {TypeName(indexTarget)} — '{ia.Name}' must be a list or dict");
                break;

            case IfStmt i:
                if (Truthy(Eval(i.Condition)))
                    RunBlock(i.Then);
                else
                    RunBlock(i.Else);
                break;

            case WhileStmt w:
                while (Truthy(Eval(w.Condition)))
                {
                    RunBlock(w.Body);
                    if (_flow == Flow.Break)    { _flow = Flow.None; break; }
                    if (_flow == Flow.Continue) { _flow = Flow.None; continue; }
                    if (_flow == Flow.Return)   break;   // propagate to the caller
                }
                break;

            case ForStmt f:
                var iterable = Eval(f.Iterable);
                List<object?> items;
                if (iterable is List<object?> lst2)
                    items = lst2;
                else if (iterable is Dictionary<string, object?> iterDict)
                    items = iterDict.Keys.Where(k => k != StructTypeKey).Select(k => (object?)k).ToList();
                else
                    throw new MakoError($"'for' needs a list or dict to loop over, got {TypeName(iterable)}"
                        + (iterable is string ? " — to loop over characters, use split(text, \"\")" : "")
                        + (iterable is double ? " — to loop over numbers, use range(n)" : ""));
                foreach (var item in new List<object?>(items))
                {
                    SetVar(f.Var, item);
                    RunBlock(f.Body);
                    if (_flow == Flow.Break)    { _flow = Flow.None; break; }
                    if (_flow == Flow.Continue) { _flow = Flow.None; continue; }
                    if (_flow == Flow.Return)   break;   // propagate to the caller
                }
                break;

            case BreakStmt:    _flow = Flow.Break;    break;
            case ContinueStmt: _flow = Flow.Continue; break;

            case ReturnStmt r:
                _returnValue = r.Value is null ? null : Eval(r.Value);
                _flow = Flow.Return;
                break;

            case RunStmt r:
                RunShellCommand(Stringify(Eval(r.Command)));
                break;

            case TryStmt ts:
                try
                {
                    PushScope();
                    try   { RunBlock(ts.Try); }
                    finally { PopScope(); }
                }
                catch (MakoError e) when (ts.HasCatch)
                {
                    PushScope();
                    if (ts.CatchVar != null) SetVar(ts.CatchVar, e.RawMessage);
                    try   { RunBlock(ts.Catch); }
                    finally { PopScope(); }
                }
                catch (Exception e) when (ts.HasCatch)
                {
                    PushScope();
                    if (ts.CatchVar != null) SetVar(ts.CatchVar, e.Message);
                    try   { RunBlock(ts.Catch); }
                    finally { PopScope(); }
                }
                break;

            case ExprStmt e:
                Eval(e.Value);
                break;

            case ThrowStmt th:
                throw new MakoError(Stringify(Eval(th.Message)));

            default:
                throw new MakoError($"Unknown statement type: {stmt.GetType().Name}");
        }
    }

    // ── Expressions ───────────────────────────────────────────────────────────

    /// Evaluates an expression, attaching the expression's source position to
    /// any runtime error that doesn't already carry one. The innermost
    /// positioned expression wins, so carets land on the exact name/operator.
    private object? Eval(Expr expr)
    {
        try { return EvalCore(expr); }
        catch (MakoError e) when (e.Line == 0 && expr.Line > 0)
        {
            int len = expr switch
            {
                IdentExpr i          => i.Name.Length,
                CallExpr c           => c.Name.Length,
                NamespacedCallExpr n => n.Ns.Length + 1 + n.Func.Length,
                BinaryExpr b         => b.Op.Length,
                _                    => 1,
            };
            throw new MakoError(e.RawMessage, expr.Line, expr.Col, len) { Hint = e.Hint };
        }
    }

    private object? EvalCore(Expr expr) => expr switch
    {
        StringLit s              => s.Value,
        TemplateStringExpr t     => Eval(t.Expanded),
        NumberLit n          => n.Value,
        BoolLit b            => b.Value,
        NullLit              => null,
        ListLit l            => l.Items.ConvertAll(Eval),
        DictLit d            => EvalDict(d),
        LambdaExpr lam       => EvalLambda(lam),
        IdentExpr id         => GetVar(id.Name),
        IndexExpr ix         => EvalIndex(ix),
        InputExpr inp        => ReadInput(Stringify(Eval(inp.Prompt))),
        UnaryExpr u          => EvalUnary(u),
        BinaryExpr bin       => EvalBinary(bin),
        LogicalExpr l        => EvalLogical(l),
        CallExpr c           => CallFunction(c.Name, c.Args),
        NamespacedCallExpr n => EvalNamespacedCall(n),
        FieldExpr fe         => EvalField(fe),
        MethodCallExpr mc    => EvalMethodCall(mc),
        StructLitExpr sl     => EvalStructLit(sl),
        _                    => throw new MakoError($"Unknown expression type: {expr.GetType().Name}"),
    };

    /// "Ns.func(args)" is ambiguous at parse time between a package call
    /// (Net.get(...)) and a struct method call on a variable (p.dist(...)) —
    /// both parse to the same NamespacedCallExpr shape. Resolve it here,
    /// where the actual runtime value of "Ns" (if it's a variable at all)
    /// is available: a struct-tagged dict dispatches as a method; anything
    /// else falls back to the flat "Ns.func" name (native/package functions).
    private object? EvalNamespacedCall(NamespacedCallExpr n)
    {
        if (TryGetVar(n.Ns, out var maybeSelf) && maybeSelf is Dictionary<string, object?> selfDict)
            return CallMethod(selfDict, n.Func, n.Args);

        return CallFunction($"{n.Ns}.{n.Func}", n.Args);
    }

    private object? EvalField(FieldExpr fe)
    {
        var target = Eval(fe.Target);
        if (target is not Dictionary<string, object?> dict)
            throw new MakoError($"cannot access field '{fe.Field}' on {TypeName(target)} — only dicts/structs have fields");
        if (dict.TryGetValue(fe.Field, out var val)) return val;
        throw new MakoError($"'{TypeName(dict)}' has no field '{fe.Field}'");
    }

    private object? EvalMethodCall(MethodCallExpr mc)
    {
        var target = Eval(mc.Target);
        if (target is not Dictionary<string, object?> dict)
            throw new MakoError($"cannot call method '{mc.Method}' on {TypeName(target)}");
        return CallMethod(dict, mc.Method, mc.Args);
    }

    /// Dispatches instance.method(args) to a 'fn TypeName.method(self, ...)'
    /// declaration, found via the instance's "__type" tag. self is passed
    /// as the method's first parameter, matching how it's written.
    private object? CallMethod(Dictionary<string, object?> self, string method, List<Expr> argExprs)
    {
        if (!self.TryGetValue(StructTypeKey, out var typeVal) || typeVal is not string typeName)
            throw new MakoError($"cannot call method '{method}' on a plain dict — only struct instances have methods");

        var fnName = $"{typeName}.{method}";
        if (!_funcs.TryGetValue(fnName, out var fn))
        {
            var suggestion = Suggest.Closest(fnName, _funcs.Keys);
            throw new MakoError($"'{typeName}' has no method '{method}'")
            {
                Hint = suggestion != null ? $"did you mean '{suggestion}'?" : null
            };
        }

        var args = argExprs.ConvertAll(Eval);
        if (args.Count + 1 != fn.Params.Count)
            throw new MakoError(
                $"'{fnName}' expects {fn.Params.Count - 1} argument(s) (plus self), got {args.Count}");

        PushCallScope();
        _scopes[^1].Vars[fn.Params[0]] = self;
        for (int i = 0; i < args.Count; i++)
            _scopes[^1].Vars[fn.Params[i + 1]] = args[i];

        object? ret = null;
        try
        {
            RunBlock(fn.Body);
            if (_flow == Flow.Return) ret = _returnValue;
            ResetFlowAtTopLevel();
        }
        finally { PopCallScope(); }
        return ret;
    }

    private object? EvalStructLit(StructLitExpr sl)
    {
        if (!_structs.TryGetValue(sl.TypeName, out var decl))
            throw new MakoError($"unknown struct '{sl.TypeName}'");

        var instance = new Dictionary<string, object?> { [StructTypeKey] = sl.TypeName };
        foreach (var (field, valueExpr) in sl.Fields)
        {
            if (!decl.Fields.Contains(field))
            {
                var suggestion = Suggest.Closest(field, decl.Fields);
                throw new MakoError($"'{sl.TypeName}' has no field '{field}'")
                {
                    Hint = suggestion != null ? $"did you mean '{suggestion}'?" : null
                };
            }
            instance[field] = Eval(valueExpr);
        }

        var missing = decl.Fields.Where(f => !instance.ContainsKey(f)).ToList();
        if (missing.Count > 0)
            throw new MakoError($"'{sl.TypeName} {{ ... }}' is missing field(s): {string.Join(", ", missing)}");

        return instance;
    }

    private Dictionary<string, object?> EvalDict(DictLit d)
    {
        var result = new Dictionary<string, object?>();
        foreach (var (keyExpr, valExpr) in d.Entries)
            result[Stringify(Eval(keyExpr))] = Eval(valExpr);
        return result;
    }

    private MakoFn EvalLambda(LambdaExpr lam)
    {
        // Capture the current scope's variables at the point of creation.
        var captured = new Dictionary<string, object?>();
        foreach (var scope in _scopes)
            foreach (var (k, v) in scope.Vars)
                captured[k] = v;
        return new MakoFn(lam.Params, lam.Body, captured);
    }

    private object? CallLambda(MakoFn fn, List<object?> args)
    {
        if (args.Count != fn.Params.Count)
            throw new MakoError($"lambda expects {fn.Params.Count} argument(s), got {args.Count}");

        PushCallScope();
        // Inject captured variables first, then params (params shadow captures).
        foreach (var (k, v) in fn.Captured)
            _scopes[^1].Vars[k] = v;
        for (int i = 0; i < fn.Params.Count; i++)
            _scopes[^1].Vars[fn.Params[i]] = args[i];

        try
        {
            RunBlock(fn.Body);
            var ret = _flow == Flow.Return ? _returnValue : null;
            ResetFlowAtTopLevel();
            return ret;
        }
        finally { PopCallScope(); }
    }

    /// Call any callable value — MakoFn lambda or named function.
    private object? CallValue(string context, object? callee, List<object?> args)
    {
        if (callee is MakoFn fn) return CallLambda(fn, args);
        throw new MakoError($"{context}: expected a function, got {TypeName(callee)}");
    }

    private object? EvalIndex(IndexExpr ix)
    {
        var target = Eval(ix.Target);
        var index  = Eval(ix.Index);
        if (target is Dictionary<string, object?> dict)
        {
            var key = Stringify(index);
            if (!dict.TryGetValue(key, out var dv))
                throw new MakoError($"dict has no key '{key}'");
            return dv;
        }
        if (index is not double dIdx)
            throw new MakoError($"list index must be a number, got {TypeName(index)} '{Short(index)}'");
        var raw = (int)dIdx;
        if (target is List<object?> list) return list[NormalizeIndex(raw, list.Count, "list")];
        if (target is string s)           return s[NormalizeIndex(raw, s.Length, "string")].ToString();
        throw new MakoError($"cannot index into {TypeName(target)} — only lists, dicts, and strings support indexing");
    }

    private object? EvalUnary(UnaryExpr u)
    {
        var val = Eval(u.Operand);
        return u.Op switch
        {
            "!" => !Truthy(val),
            "-" => val is double d ? -d
                   : throw new MakoError($"cannot negate {TypeName(val)} '{Short(val)}' — '-' needs a number"),
            _   => throw new MakoError($"unknown unary operator '{u.Op}'"),
        };
    }

    private object? EvalBinary(BinaryExpr bin)
    {
        var left  = Eval(bin.Left);
        var right = Eval(bin.Right);
        string op = bin.Op;

        if (op == "+" && left is List<object?> la && right is List<object?> lb)
        { var r = new List<object?>(la); r.AddRange(lb); return r; }

        if (op == "+" && (left is string || right is string))
            return Stringify(left) + Stringify(right);

        if (op is "+" or "-" or "*" or "/" or "%")
        {
            double l, r;
            try { l = ToNumber(left); r = ToNumber(right); }
            catch (MakoError)
            {
                string hint = op == "+" && (left is List<object?> || right is List<object?>)
                    ? " — to add an item to a list, use push(list, item)"
                    : " — both sides must be numbers";
                throw new MakoError($"cannot use '{op}' on {TypeName(left)} and {TypeName(right)}{hint}");
            }
            return op switch
            {
                "+" => l + r,
                "-" => l - r,
                "*" => l * r,
                "/" => r == 0 ? throw new MakoError("division by zero") : l / r,
                "%" => r == 0 ? throw new MakoError("modulo by zero")   : l % r,
                _   => throw new MakoError($"unknown operator '{op}'"),
            };
        }

        if (op == "==") return  ValuesEqual(left, right);
        if (op == "!=") return !ValuesEqual(left, right);

        double lc, rc;
        try { lc = ToNumber(left); rc = ToNumber(right); }
        catch (MakoError)
        {
            throw new MakoError($"cannot compare {TypeName(left)} and {TypeName(right)} with '{op}'");
        }
        return op switch
        {
            "<"  => lc < rc, ">"  => lc > rc,
            "<=" => lc <= rc, ">=" => lc >= rc,
            _    => throw new MakoError($"unknown operator '{op}'"),
        };
    }

    private object? EvalLogical(LogicalExpr l)
    {
        var left = Eval(l.Left);
        if (l.Op == "and") return Truthy(left) ? Eval(l.Right) : left;
        if (l.Op == "or")  return Truthy(left) ? left          : Eval(l.Right);
        throw new MakoError($"Unknown logical operator '{l.Op}'");
    }

    // ── Function calls ────────────────────────────────────────────────────────

    private object? CallFunction(string name, List<Expr> argExprs)
    {
        var args = argExprs.ConvertAll(Eval);

        if (TryBuiltin(name, args, out var builtinResult))
            return builtinResult;

        if (!_funcs.TryGetValue(name, out var fn))
        {
            // Check if it's a variable holding a lambda.
            if (TryGetVar(name, out var maybeVar) && maybeVar is MakoFn lambda)
                return CallLambda(lambda, args);

            var suggestion = Suggest.Closest(name, _funcs.Keys.Concat(BuiltinNames));
            throw new MakoError($"function '{name}' wasn't found")
            {
                Hint = suggestion != null ? $"did you mean '{suggestion}'?" : null
            };
        }

        if (args.Count != fn.Params.Count)
            throw new MakoError($"'{name}' expects {fn.Params.Count} argument(s), got {args.Count}");

        PushCallScope();
        for (int i = 0; i < fn.Params.Count; i++)
            _scopes[^1].Vars[fn.Params[i]] = args[i];

        object? ret = null;
        try
        {
            RunBlock(fn.Body);
            if (_flow == Flow.Return) ret = _returnValue;
            ResetFlowAtTopLevel();
        }
        catch (MakoError e) when (fn.Source != null && e.SourcePath is null)
        {
            // Error inside an imported function: its line numbers refer to the
            // module file, so tag the error with that path for the CLI snippet.
            throw new MakoError(e.RawMessage, e.Line, e.Col, e.Length)
                  { SourcePath = fn.Source };
        }
        finally { PopCallScope(); }
        return ret;
    }

    private static readonly string[] BuiltinNames =
    [
        "type", "to_num", "to_str", "exit", "assert",
        "abs", "floor", "ceil", "sqrt", "round", "pow", "max", "min", "range",
        "clamp", "lerp", "sign", "sin", "cos", "tan", "atan2", "pi",
        "dist", "dist3d", "rects_overlap", "circles_overlap", "box3d_overlap", "point_in_rect", "slice",
        "find_path", "line_of_sight",
        "args", "json_encode", "json_decode",
        "len", "upper", "lower", "trim", "contains", "starts_with", "ends_with",
        "replace", "split", "join",
        "push", "pop", "first", "last", "reverse", "has",
        // Dict builtins
        "keys", "values", "remove", "merge", "get",
        // Higher-order functions
        "map", "filter", "reduce", "sort_by", "each", "any", "all",
        // I/O & system stdlib
        "read", "write", "append", "exists", "delete", "lines",
        "time", "random", "random_int", "sleep", "env",
        // MakoUI — lifecycle
        "MakoUI.init", "MakoUI.attach", "MakoUI.running", "MakoUI.begin", "MakoUI.end",
        // MakoUI — windows
        "MakoUI.begin_window", "MakoUI.end_window", "MakoUI.begin_window_menu", "MakoUI.begin_toolbar",
        // MakoUI — widgets
        "MakoUI.text", "MakoUI.text_colored", "MakoUI.button", "MakoUI.small_button",
        "MakoUI.checkbox", "MakoUI.slider", "MakoUI.slider_int",
        "MakoUI.drag", "MakoUI.drag_int", "MakoUI.drag_range",
        "MakoUI.input_text", "MakoUI.input_number", "MakoUI.input_text_multi",
        "MakoUI.combo", "MakoUI.preview",
        "MakoUI.collapsing", "MakoUI.progress",
        // MakoUI — layout
        "MakoUI.separator", "MakoUI.same_line", "MakoUI.spacing", "MakoUI.new_line",
        "MakoUI.set_window_size", "MakoUI.set_window_pos",
        // MakoUI — menus
        "MakoUI.begin_menu_bar", "MakoUI.end_menu_bar",
        "MakoUI.begin_main_menu_bar", "MakoUI.end_main_menu_bar",
        "MakoUI.begin_menu", "MakoUI.end_menu", "MakoUI.menu_item",
        // MakoUI — popups
        "MakoUI.open_popup", "MakoUI.begin_popup", "MakoUI.begin_modal",
        "MakoUI.close_popup", "MakoUI.end_popup",
        // MakoUI — tables
        "MakoUI.begin_table", "MakoUI.table_column", "MakoUI.table_header_row",
        "MakoUI.table_next_row", "MakoUI.table_next_col", "MakoUI.end_table",
        // MakoUI — tooltips
        "MakoUI.tooltip", "MakoUI.set_tooltip",
        // MakoUI — query
        "MakoUI.is_hovered", "MakoUI.is_clicked", "MakoUI.is_key_pressed",
        "MakoUI.get_time", "MakoUI.framerate", "MakoUI.fps_counter", "MakoUI.wants_mouse", "MakoUI.wants_keyboard", "MakoUI.color_picker",
        "MakoUI.begin_tab_bar", "MakoUI.end_tab_bar", "MakoUI.begin_tab_item", "MakoUI.end_tab_item",
        // MakoUI — style & themes
        "MakoUI.push_color", "MakoUI.pop_color", "MakoUI.push_var", "MakoUI.pop_var",
        "MakoUI.theme_dark", "MakoUI.theme_light", "MakoUI.theme_mako",
        // MakoUI — fonts
        "MakoUI.push_font", "MakoUI.pop_font", "MakoUI.set_default_font_size",
        // MakoRay — lifecycle
        "MakoRay.init", "MakoRay.fps", "MakoRay.running", "MakoRay.begin", "MakoRay.end",
        "MakoRay.close", "MakoRay.delta", "MakoRay.get_fps", "MakoRay.get_time",
        "MakoRay.width", "MakoRay.height", "MakoRay.title",
        // MakoRay — drawing
        "MakoRay.clear", "MakoRay.text", "MakoRay.draw_fps",
        // MakoRay — shapes
        "MakoRay.rect", "MakoRay.rect_lines", "MakoRay.rect_round",
        "MakoRay.circle", "MakoRay.circle_lines",
        "MakoRay.line", "MakoRay.triangle",
        // MakoRay — input
        "MakoRay.key_down", "MakoRay.key_pressed", "MakoRay.key_released", "MakoRay.get_key",
        "MakoRay.mouse_x", "MakoRay.mouse_y",
        "MakoRay.mouse_down", "MakoRay.mouse_pressed", "MakoRay.mouse_wheel",
        // MakoRay — color
        "MakoRay.color", "MakoRay.fade",
        // MakoRay — audio
        "MakoRay.init_audio", "MakoRay.close_audio",
        // Net
        "Net.get", "Net.post", "Net.put", "Net.delete",
        "Net.ok", "Net.status", "Net.body", "Net.error", "Net.json",
        "Net.url_encode", "Net.url_decode",
    ];

    private bool TryBuiltin(string name, List<object?> args, out object? result)
    {
        result = null;
        switch (name)
        {
            // ── Type / conversion ─────────────────────────────────────────────
            case "type":    RequireArity(name, args, 1); result = TypeName(args[0]); return true;
            case "to_num":
                RequireArity(name, args, 1);
                try { result = ToNumber(args[0]); }
                catch (MakoError)
                {
                    throw new MakoError(
                        $"to_num() can't convert {TypeName(args[0])} '{Short(args[0])}' to a number"
                        + (args[0] is string ? " — the text isn't numeric" : ""));
                }
                return true;
            case "to_str":  RequireArity(name, args, 1); result = Stringify(args[0]); return true;

            // ── Program control ───────────────────────────────────────────────
            case "exit":
                if (args.Count > 1) throw new MakoError("exit() expects 0 or 1 argument(s)");
                Environment.Exit(args.Count == 1 ? (int)ToNumber(args[0]) : 0);
                return true;

            case "assert":
                if (args.Count < 1 || args.Count > 2)
                    throw new MakoError("assert() expects 1 or 2 argument(s)");
                if (!Truthy(args[0]))
                    throw new MakoError(args.Count == 2
                        ? $"Assertion failed: {Stringify(args[1])}"
                        : "Assertion failed");
                result = null; return true;

            // ── Math ──────────────────────────────────────────────────────────
            case "abs":   RequireArity(name, args, 1); result = Math.Abs(AsNum(name, args[0]));     return true;
            case "floor": RequireArity(name, args, 1); result = Math.Floor(AsNum(name, args[0]));   return true;
            case "ceil":  RequireArity(name, args, 1); result = Math.Ceiling(AsNum(name, args[0])); return true;
            case "sqrt":  RequireArity(name, args, 1); result = Math.Sqrt(AsNum(name, args[0]));    return true;
            case "round": RequireArity(name, args, 1);
                result = Math.Round(AsNum(name, args[0]), MidpointRounding.AwayFromZero); return true;
            case "pow":   RequireArity(name, args, 2); result = Math.Pow(AsNum(name, args[0]), AsNum(name, args[1])); return true;
            case "max":   RequireArity(name, args, 2); result = Math.Max(AsNum(name, args[0]), AsNum(name, args[1])); return true;
            case "min":   RequireArity(name, args, 2); result = Math.Min(AsNum(name, args[0]), AsNum(name, args[1])); return true;
            case "clamp": RequireArity(name, args, 3);
                result = Math.Clamp(AsNum(name, args[0]), AsNum(name, args[1]), AsNum(name, args[2])); return true;
            case "lerp":  RequireArity(name, args, 3);
            {
                double la = AsNum(name, args[0]), lb = AsNum(name, args[1]), lt = AsNum(name, args[2]);
                result = la + (lb - la) * lt; return true;
            }
            case "sign":  RequireArity(name, args, 1); result = (double)Math.Sign(AsNum(name, args[0])); return true;
            case "sin":   RequireArity(name, args, 1); result = Math.Sin(AsNum(name, args[0])); return true;
            case "cos":   RequireArity(name, args, 1); result = Math.Cos(AsNum(name, args[0])); return true;
            case "tan":   RequireArity(name, args, 1); result = Math.Tan(AsNum(name, args[0])); return true;
            case "atan2": RequireArity(name, args, 2); result = Math.Atan2(AsNum(name, args[0]), AsNum(name, args[1])); return true;
            case "pi":    RequireArity(name, args, 0); result = Math.PI; return true;

            // ── Geometry / collision (for games) ─────────────────────────────
            case "dist":  RequireArity(name, args, 4);
            {
                double dxx = AsNum(name, args[2]) - AsNum(name, args[0]);
                double dyy = AsNum(name, args[3]) - AsNum(name, args[1]);
                result = Math.Sqrt(dxx * dxx + dyy * dyy); return true;
            }
            case "rects_overlap": RequireArity(name, args, 8);
            {
                double ax = AsNum(name, args[0]), ay = AsNum(name, args[1]);
                double aw = AsNum(name, args[2]), ah = AsNum(name, args[3]);
                double bx = AsNum(name, args[4]), by = AsNum(name, args[5]);
                double bw = AsNum(name, args[6]), bh = AsNum(name, args[7]);
                result = ax < bx + bw && ax + aw > bx && ay < by + bh && ay + ah > by;
                return true;
            }
            case "circles_overlap": RequireArity(name, args, 6);
            {
                double cdx = AsNum(name, args[3]) - AsNum(name, args[0]);
                double cdy = AsNum(name, args[4]) - AsNum(name, args[1]);
                double rr  = AsNum(name, args[2]) + AsNum(name, args[5]);
                result = cdx * cdx + cdy * cdy <= rr * rr;
                return true;
            }
            case "point_in_rect": RequireArity(name, args, 6);
            {
                double px = AsNum(name, args[0]), py = AsNum(name, args[1]);
                double rx = AsNum(name, args[2]), ry = AsNum(name, args[3]);
                result = px >= rx && px <= rx + AsNum(name, args[4])
                      && py >= ry && py <= ry + AsNum(name, args[5]);
                return true;
            }
            case "dist3d": RequireArity(name, args, 6);
            {
                double dxx = AsNum(name, args[3]) - AsNum(name, args[0]);
                double dyy = AsNum(name, args[4]) - AsNum(name, args[1]);
                double dzz = AsNum(name, args[5]) - AsNum(name, args[2]);
                result = Math.Sqrt(dxx * dxx + dyy * dyy + dzz * dzz); return true;
            }
            // box3d_overlap(min1_x,min1_y,min1_z, max1_x,max1_y,max1_z,
            //               min2_x,min2_y,min2_z, max2_x,max2_y,max2_z) —
            // takes two boxes already in min/max form, matching what
            // Mako3D.object_bounds() returns.
            case "box3d_overlap": RequireArity(name, args, 12);
            {
                double aMinX = AsNum(name, args[0]), aMinY = AsNum(name, args[1]), aMinZ = AsNum(name, args[2]);
                double aMaxX = AsNum(name, args[3]), aMaxY = AsNum(name, args[4]), aMaxZ = AsNum(name, args[5]);
                double bMinX = AsNum(name, args[6]), bMinY = AsNum(name, args[7]), bMinZ = AsNum(name, args[8]);
                double bMaxX = AsNum(name, args[9]), bMaxY = AsNum(name, args[10]), bMaxZ = AsNum(name, args[11]);
                result = aMinX < bMaxX && aMaxX > bMinX
                      && aMinY < bMaxY && aMaxY > bMinY
                      && aMinZ < bMaxZ && aMaxZ > bMinZ;
                return true;
            }

            // ── Pathfinding (for game AI) ─────────────────────────────────────
            // find_path(grid, sx, sy, ex, ey) → list of [x, y] steps (excludes
            // start, includes goal), or [] if unreachable. grid = list of rows,
            // each row a list where 0/false = walkable, anything truthy = wall.
            case "find_path":
            {
                RequireArity(name, args, 5);
                var grid = AsList(name, args[0]);
                int sx = (int)AsNum(name, args[1]), sy = (int)AsNum(name, args[2]);
                int ex = (int)AsNum(name, args[3]), ey = (int)AsNum(name, args[4]);
                result = FindPath(grid, sx, sy, ex, ey);
                return true;
            }

            // line_of_sight(grid, x1, y1, x2, y2) → true if no wall cell blocks
            // the straight line between the two cells (Bresenham).
            case "line_of_sight":
            {
                RequireArity(name, args, 5);
                var grid = AsList(name, args[0]);
                int x1 = (int)AsNum(name, args[1]), y1 = (int)AsNum(name, args[2]);
                int x2 = (int)AsNum(name, args[3]), y2 = (int)AsNum(name, args[4]);
                result = LineOfSight(grid, x1, y1, x2, y2);
                return true;
            }

            // ── slice(list_or_string, start, end) — end exclusive ─────────────
            case "slice":
            {
                RequireArity(name, args, 3);
                int sFrom = (int)AsNum(name, args[1]);
                int sTo   = (int)AsNum(name, args[2]);
                if (args[0] is List<object?> sl)
                {
                    sFrom = Math.Clamp(sFrom, 0, sl.Count);
                    sTo   = Math.Clamp(sTo,   sFrom, sl.Count);
                    result = sl.GetRange(sFrom, sTo - sFrom);
                    return true;
                }
                if (args[0] is string ss)
                {
                    sFrom = Math.Clamp(sFrom, 0, ss.Length);
                    sTo   = Math.Clamp(sTo,   sFrom, ss.Length);
                    result = ss[sFrom..sTo];
                    return true;
                }
                throw new MakoError($"slice() expects a list or string, got {TypeName(args[0])}");
            }

            // ── Range ─────────────────────────────────────────────────────────
            case "range":
                if (args.Count < 1 || args.Count > 3)
                    throw new MakoError("range() expects 1, 2, or 3 argument(s)");
                double rStart = args.Count >= 2 ? ToNumber(args[0]) : 0;
                double rStop  = args.Count >= 2 ? ToNumber(args[1]) : ToNumber(args[0]);
                double rStep  = args.Count == 3 ? ToNumber(args[2]) : 1;
                if (rStep == 0) throw new MakoError("range() step cannot be zero");
                var rList = new List<object?>();
                if (rStep > 0) for (double v = rStart; v < rStop; v += rStep) rList.Add(v);
                else           for (double v = rStart; v > rStop; v += rStep) rList.Add(v);
                result = rList; return true;

            // ── String ────────────────────────────────────────────────────────
            case "len":
                RequireArity(name, args, 1);
                result = args[0] switch
                {
                    string s                      => (double)s.Length,
                    List<object?> l               => (double)l.Count,
                    Dictionary<string, object?> d => (double)(d.ContainsKey(StructTypeKey) ? d.Count - 1 : d.Count),
                    _ => throw new MakoError($"len() expects a string, list, or dict, got '{TypeName(args[0])}'"),
                };
                return true;

            case "upper":       RequireArity(name, args, 1); result = AsStr(name, args[0]).ToUpperInvariant(); return true;
            case "lower":       RequireArity(name, args, 1); result = AsStr(name, args[0]).ToLowerInvariant(); return true;
            case "trim":        RequireArity(name, args, 1); result = AsStr(name, args[0]).Trim(); return true;
            case "contains":    RequireArity(name, args, 2); result = AsStr(name, args[0]).Contains(AsStr(name, args[1])); return true;
            case "starts_with": RequireArity(name, args, 2); result = AsStr(name, args[0]).StartsWith(AsStr(name, args[1])); return true;
            case "ends_with":   RequireArity(name, args, 2); result = AsStr(name, args[0]).EndsWith(AsStr(name, args[1])); return true;
            case "replace":     RequireArity(name, args, 3); result = AsStr(name, args[0]).Replace(AsStr(name, args[1]), AsStr(name, args[2])); return true;

            case "split":
                RequireArity(name, args, 2);
                result = AsStr(name, args[0]).Split(AsStr(name, args[1]))
                            .Select(p => (object?)p).ToList();
                return true;

            case "join":
                RequireArity(name, args, 2);
                result = string.Join(AsStr(name, args[1]),
                            AsList(name, args[0]).Select(Stringify));
                return true;

            // ── List ──────────────────────────────────────────────────────────
            case "push":    RequireArity(name, args, 2); AsList(name, args[0]).Add(args[1]); result = null; return true;

            case "pop":
                RequireArity(name, args, 1);
                var popList = AsList(name, args[0]);
                if (popList.Count == 0) throw new MakoError("pop() called on empty list");
                result = popList[^1]; popList.RemoveAt(popList.Count - 1); return true;

            case "first":
                RequireArity(name, args, 1);
                var fl = AsList(name, args[0]);
                if (fl.Count == 0) throw new MakoError("first() called on empty list");
                result = fl[0]; return true;

            case "last":
                RequireArity(name, args, 1);
                var ll = AsList(name, args[0]);
                if (ll.Count == 0) throw new MakoError("last() called on empty list");
                result = ll[^1]; return true;

            case "reverse":
                RequireArity(name, args, 1);
                var rev = new List<object?>(AsList(name, args[0]));
                rev.Reverse(); result = rev; return true;

            case "has":
                RequireArity(name, args, 2);
                if (args[0] is Dictionary<string, object?> hasDct)
                    result = (object?)hasDct.ContainsKey(Stringify(args[1]));
                else
                    result = (object?)AsList(name, args[0]).Any(v => ValuesEqual(v, args[1]));
                return true;

            // ── Higher-order list functions ───────────────────────────────────
            case "map":
                RequireArity(name, args, 2);
                result = AsList(name, args[0])
                    .Select(item => CallValue("map", args[1], [item]))
                    .ToList();
                return true;

            case "filter":
                RequireArity(name, args, 2);
                result = AsList(name, args[0])
                    .Where(item => Truthy(CallValue("filter", args[1], [item])))
                    .ToList();
                return true;

            case "reduce":
                RequireArity(name, args, 3);
                var reduceList = AsList(name, args[0]);
                var acc = args[2];
                foreach (var item in reduceList)
                    acc = CallValue("reduce", args[1], [acc, item]);
                result = acc; return true;

            case "sort_by":
                if (args.Count < 1 || args.Count > 2)
                    throw new MakoError("sort_by() expects 1 or 2 arguments (list [, key_fn])");
                var sortList = new List<object?>(AsList(name, args[0]));
                if (args.Count == 2)
                    sortList.Sort((a, b) =>
                    {
                        var ka = CallValue("sort_by", args[1], [a]);
                        var kb = CallValue("sort_by", args[1], [b]);
                        return Comparer<object?>.Create((x, y) =>
                        {
                            if (x is double dx && y is double dy) return dx.CompareTo(dy);
                            return string.Compare(Stringify(x), Stringify(y), StringComparison.Ordinal);
                        }).Compare(ka, kb);
                    });
                else
                    sortList.Sort((a, b) =>
                    {
                        if (a is double da && b is double db) return da.CompareTo(db);
                        return string.Compare(Stringify(a), Stringify(b), StringComparison.Ordinal);
                    });
                result = sortList; return true;

            case "each":
                RequireArity(name, args, 2);
                foreach (var item in AsList(name, args[0]))
                    CallValue("each", args[1], [item]);
                result = null; return true;

            case "any":
                RequireArity(name, args, 2);
                result = (object?)AsList(name, args[0]).Any(item => Truthy(CallValue("any", args[1], [item])));
                return true;

            case "all":
                RequireArity(name, args, 2);
                result = (object?)AsList(name, args[0]).All(item => Truthy(CallValue("all", args[1], [item])));
                return true;

            // ── Dict builtins ─────────────────────────────────────────────────
            case "keys":
                RequireArity(name, args, 1);
                result = AsDict(name, args[0]).Keys.Where(k => k != StructTypeKey)
                                                    .Select(k => (object?)k).ToList();
                return true;

            case "values":
                RequireArity(name, args, 1);
                result = AsDict(name, args[0])
                    .Where(kv => kv.Key != StructTypeKey).Select(kv => kv.Value).ToList();
                return true;

            case "remove":
                RequireArity(name, args, 2);
                result = (object?)AsDict(name, args[0]).Remove(Stringify(args[1]));
                return true;

            case "merge":
                if (args.Count < 2) throw new MakoError("merge() expects at least 2 dicts");
                var merged = new Dictionary<string, object?>();
                foreach (var a in args)
                    foreach (var kv in AsDict(name, a))
                        merged[kv.Key] = kv.Value;
                result = merged; return true;

            case "get":
                if (args.Count < 2 || args.Count > 3) throw new MakoError("get() expects 2 or 3 arguments (dict, key [, default])");
                var getDict = AsDict(name, args[0]);
                var getKey  = Stringify(args[1]);
                result = getDict.TryGetValue(getKey, out var getVal) ? getVal : (args.Count == 3 ? args[2] : null);
                return true;

            // ── I/O stdlib ────────────────────────────────────────────────────
            case "read":
                RequireArity(name, args, 1);
                var readPath = AsStr(name, args[0]);
                if (!File.Exists(readPath))
                    throw new MakoError($"read(): file not found: '{readPath}'");
                result = File.ReadAllText(readPath); return true;

            case "write":
                RequireArity(name, args, 2);
                File.WriteAllText(AsStr(name, args[0]), Stringify(args[1]));
                result = null; return true;

            case "append":
                RequireArity(name, args, 2);
                File.AppendAllText(AsStr(name, args[0]), Stringify(args[1]));
                result = null; return true;

            case "exists":
                RequireArity(name, args, 1);
                var exPath = AsStr(name, args[0]);
                result = (object?)(File.Exists(exPath) || Directory.Exists(exPath)); return true;

            case "delete":
                RequireArity(name, args, 1);
                var delPath = AsStr(name, args[0]);
                if (File.Exists(delPath)) File.Delete(delPath);
                else if (Directory.Exists(delPath)) Directory.Delete(delPath, recursive: true);
                result = null; return true;

            case "lines":
                RequireArity(name, args, 1);
                var linesPath = AsStr(name, args[0]);
                if (!File.Exists(linesPath))
                    throw new MakoError($"lines(): file not found: '{linesPath}'");
                result = File.ReadAllLines(linesPath)
                             .Select(l => (object?)l)
                             .ToList(); return true;

            // ── Time / random ─────────────────────────────────────────────────
            case "time":
                RequireArity(name, args, 0);
                result = (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0; return true;

            case "random":
                if (args.Count == 0)
                {
                    result = Random.Shared.NextDouble(); return true;
                }
                RequireArity(name, args, 2);
                var rlo = AsNum(name, args[0]);
                var rhi = AsNum(name, args[1]);
                result = Random.Shared.NextDouble() * (rhi - rlo) + rlo; return true;

            case "random_int":
                RequireArity(name, args, 2);
                var rilo = (int)AsNum(name, args[0]);
                var rihi = (int)AsNum(name, args[1]);
                result = (double)Random.Shared.Next(rilo, rihi + 1); return true;

            case "sleep":
                RequireArity(name, args, 1);
                Thread.Sleep((int)(AsNum(name, args[0]) * 1000));
                result = null; return true;

            case "env":
                RequireArity(name, args, 1);
                result = (object?)(Environment.GetEnvironmentVariable(AsStr(name, args[0])) ?? "");
                return true;

            case "args":
                RequireArity(name, args, 0);
                result = ScriptArgs.Select(a2 => (object?)a2).ToList();
                return true;

            // ── JSON ──────────────────────────────────────────────────────────
            case "json_encode":
                RequireArity(name, args, 1);
                result = Json.Encode(args[0]);
                return true;

            case "json_decode":
                RequireArity(name, args, 1);
                try { result = Json.Decode(AsStr(name, args[0])); }
                catch (Exception ex) { throw new MakoError($"json_decode(): {ex.Message}"); }
                return true;

            // ── MakoUI ────────────────────────────────────────────────────────
            case "MakoUI.init":
                RequireArity(name, args, 3);
                EnsureUI(name);
                _ui!.Init(Stringify(args[0]), (int)AsNum(name, args[1]), (int)AsNum(name, args[2]));
                result = null; return true;

            case "MakoUI.attach":
                RequireArity(name, args, 0);
                EnsureUI(name);
                _ui!.Attach();
                result = null; return true;

            case "MakoUI.running":
                RequireArity(name, args, 0);
                EnsureUI(name);
                result = _ui!.Running(); return true;

            case "MakoUI.begin":
                RequireArity(name, args, 0);
                EnsureUI(name);
                _ui!.Begin(); result = null; return true;

            case "MakoUI.end":
                RequireArity(name, args, 0);
                EnsureUI(name);
                _ui!.End(); result = null; return true;

            case "MakoUI.begin_window":
                if (args.Count < 1 || args.Count > 2)
                    throw new MakoError("MakoUI.begin_window() expects 1 or 2 arguments");
                EnsureUI(name);
                result = args.Count == 2
                    ? _ui!.BeginWindow(Stringify(args[0]), Truthy(args[1]))
                    : _ui!.BeginWindow(Stringify(args[0]));
                return true;

            case "MakoUI.end_window":
                RequireArity(name, args, 0);
                EnsureUI(name);
                _ui!.EndWindow(); result = null; return true;

            case "MakoUI.text":
                RequireArity(name, args, 1);
                EnsureUI(name);
                _ui!.Text(Stringify(args[0])); result = null; return true;

            case "MakoUI.text_colored":
                if (args.Count < 4 || args.Count > 5)
                    throw new MakoError("MakoUI.text_colored() expects (text, r, g, b [, a])");
                EnsureUI(name);
                _ui!.TextColored(Stringify(args[0]), AsNum(name, args[1]),
                                 AsNum(name, args[2]), AsNum(name, args[3]),
                                 args.Count > 4 ? AsNum(name, args[4]) : 1.0);
                result = null; return true;

            case "MakoUI.button":
                RequireArity(name, args, 1);
                EnsureUI(name);
                result = _ui!.Button(Stringify(args[0])); return true;

            case "MakoUI.small_button":
                RequireArity(name, args, 1);
                EnsureUI(name);
                result = _ui!.SmallButton(Stringify(args[0])); return true;

            case "MakoUI.checkbox":
                RequireArity(name, args, 2);
                EnsureUI(name);
                result = _ui!.Checkbox(Stringify(args[0]), Truthy(args[1])); return true;

            case "MakoUI.slider":
                RequireArity(name, args, 4);
                EnsureUI(name);
                result = _ui!.Slider(Stringify(args[0]),
                    AsNum(name, args[1]), AsNum(name, args[2]), AsNum(name, args[3]));
                return true;

            case "MakoUI.slider_int":
                RequireArity(name, args, 4);
                EnsureUI(name);
                result = _ui!.SliderInt(Stringify(args[0]),
                    AsNum(name, args[1]), AsNum(name, args[2]), AsNum(name, args[3]));
                return true;

            case "MakoUI.input_text":
                RequireArity(name, args, 2);
                EnsureUI(name);
                result = _ui!.InputText(Stringify(args[0]), Stringify(args[1])); return true;

            case "MakoUI.input_number":
                RequireArity(name, args, 2);
                EnsureUI(name);
                result = _ui!.InputNumber(Stringify(args[0]), AsNum(name, args[1])); return true;

            case "MakoUI.separator":
                RequireArity(name, args, 0);
                EnsureUI(name); _ui!.Separator(); result = null; return true;

            case "MakoUI.same_line":
                RequireArity(name, args, 0);
                EnsureUI(name); _ui!.SameLine(); result = null; return true;

            case "MakoUI.spacing":
                RequireArity(name, args, 0);
                EnsureUI(name); _ui!.Spacing(); result = null; return true;

            case "MakoUI.new_line":
                RequireArity(name, args, 0);
                EnsureUI(name); _ui!.NewLine(); result = null; return true;

            case "MakoUI.collapsing":
                RequireArity(name, args, 1);
                EnsureUI(name);
                result = _ui!.CollapsingHeader(Stringify(args[0])); return true;

            case "MakoUI.progress":
                if (args.Count < 1 || args.Count > 2)
                    throw new MakoError("MakoUI.progress() expects 1 or 2 arguments");
                EnsureUI(name);
                _ui!.ProgressBar(AsNum(name, args[0]),
                    args.Count == 2 ? Stringify(args[1]) : null);
                result = null; return true;

            case "MakoUI.set_window_size":
                RequireArity(name, args, 2);
                EnsureUI(name);
                _ui!.SetNextWindowSize(AsNum(name, args[0]), AsNum(name, args[1]));
                result = null; return true;

            case "MakoUI.set_window_pos":
                RequireArity(name, args, 2);
                EnsureUI(name);
                _ui!.SetNextWindowPos(AsNum(name, args[0]), AsNum(name, args[1]));
                result = null; return true;

            case "MakoUI.push_color":
                if (args.Count < 4 || args.Count > 5)
                    throw new MakoError("MakoUI.push_color() expects 4 or 5 arguments (idx, r, g, b [, a])");
                EnsureUI(name);
                _ui!.PushStyleColor((int)AsNum(name, args[0]),
                    AsNum(name, args[1]), AsNum(name, args[2]), AsNum(name, args[3]),
                    args.Count == 5 ? AsNum(name, args[4]) : 1.0);
                result = null; return true;

            case "MakoUI.pop_color":
                if (args.Count > 1) throw new MakoError("MakoUI.pop_color() expects 0 or 1 argument");
                EnsureUI(name);
                _ui!.PopStyleColor(args.Count == 1 ? (int)AsNum(name, args[0]) : 1);
                result = null; return true;

            case "MakoUI.push_var":
                RequireArity(name, args, 2);
                EnsureUI(name);
                _ui!.PushStyleVar((int)AsNum(name, args[0]), AsNum(name, args[1]));
                result = null; return true;

            case "MakoUI.pop_var":
                if (args.Count > 1) throw new MakoError("MakoUI.pop_var() expects 0 or 1 argument");
                EnsureUI(name);
                _ui!.PopStyleVar(args.Count == 1 ? (int)AsNum(name, args[0]) : 1);
                result = null; return true;

            // ── Themes ────────────────────────────────────────────────────────
            case "MakoUI.theme_dark":
                EnsureUI(name); _ui!.ThemeDark(); result = null; return true;
            case "MakoUI.theme_light":
                EnsureUI(name); _ui!.ThemeLight(); result = null; return true;
            case "MakoUI.theme_mako":
                EnsureUI(name); _ui!.ThemeMako(); result = null; return true;

            // ── Menus ─────────────────────────────────────────────────────────
            case "MakoUI.begin_menu_bar":
                EnsureUI(name); result = (object?)_ui!.BeginMenuBar(); return true;
            case "MakoUI.end_menu_bar":
                EnsureUI(name); _ui!.EndMenuBar(); result = null; return true;
            case "MakoUI.begin_main_menu_bar":
                EnsureUI(name); result = (object?)_ui!.BeginMainMenuBar(); return true;
            case "MakoUI.end_main_menu_bar":
                EnsureUI(name); _ui!.EndMainMenuBar(); result = null; return true;
            case "MakoUI.begin_menu":
                if (args.Count != 1) throw new MakoError("MakoUI.begin_menu() expects 1 argument");
                EnsureUI(name); result = (object?)_ui!.BeginMenu(AsStr(name, args[0])); return true;
            case "MakoUI.end_menu":
                EnsureUI(name); _ui!.EndMenu(); result = null; return true;
            case "MakoUI.menu_item":
                if (args.Count < 1 || args.Count > 2) throw new MakoError("MakoUI.menu_item() expects 1 or 2 arguments");
                EnsureUI(name);
                result = (object?)(args.Count == 2
                    ? _ui!.MenuItem(AsStr(name, args[0]), AsStr(name, args[1]))
                    : _ui!.MenuItem(AsStr(name, args[0])));
                return true;

            // ── Popups ────────────────────────────────────────────────────────
            case "MakoUI.open_popup":
                if (args.Count != 1) throw new MakoError("MakoUI.open_popup() expects 1 argument");
                EnsureUI(name); _ui!.OpenPopup(AsStr(name, args[0])); result = null; return true;
            case "MakoUI.begin_popup":
                if (args.Count != 1) throw new MakoError("MakoUI.begin_popup() expects 1 argument");
                EnsureUI(name); result = (object?)_ui!.BeginPopup(AsStr(name, args[0])); return true;
            case "MakoUI.begin_modal":
                if (args.Count != 1) throw new MakoError("MakoUI.begin_modal() expects 1 argument");
                EnsureUI(name); result = (object?)_ui!.BeginModal(AsStr(name, args[0])); return true;
            case "MakoUI.close_popup":
                EnsureUI(name); _ui!.ClosePopup(); result = null; return true;
            case "MakoUI.end_popup":
                EnsureUI(name); _ui!.EndPopup(); result = null; return true;

            // ── Tables ────────────────────────────────────────────────────────
            case "MakoUI.begin_table":
                if (args.Count < 2 || args.Count > 4) throw new MakoError("MakoUI.begin_table() expects 2-4 arguments");
                EnsureUI(name);
                result = (object?)(args.Count == 2
                    ? _ui!.BeginTable(AsStr(name, args[0]), (int)AsNum(name, args[1]))
                    : _ui!.BeginTable(AsStr(name, args[0]), (int)AsNum(name, args[1]),
                                      AsBool(args[2]), args.Count > 3 && AsBool(args[3])));
                return true;
            case "MakoUI.table_column":
                if (args.Count != 1) throw new MakoError("MakoUI.table_column() expects 1 argument");
                EnsureUI(name); _ui!.TableColumn(AsStr(name, args[0])); result = null; return true;
            case "MakoUI.table_header_row":
                EnsureUI(name); _ui!.TableHeaderRow(); result = null; return true;
            case "MakoUI.table_next_row":
                EnsureUI(name); _ui!.TableNextRow(); result = null; return true;
            case "MakoUI.table_next_col":
                EnsureUI(name); _ui!.TableNextCol(); result = null; return true;
            case "MakoUI.end_table":
                EnsureUI(name); _ui!.EndTable(); result = null; return true;

            // ── Drag ──────────────────────────────────────────────────────────
            case "MakoUI.drag":
                if (args.Count < 2 || args.Count > 3) throw new MakoError("MakoUI.drag() expects 2 or 3 arguments");
                EnsureUI(name);
                result = _ui!.Drag(AsStr(name, args[0]), AsNum(name, args[1]),
                                   args.Count > 2 ? AsNum(name, args[2]) : 1.0);
                return true;
            case "MakoUI.drag_int":
                if (args.Count < 2 || args.Count > 3) throw new MakoError("MakoUI.drag_int() expects 2 or 3 arguments");
                EnsureUI(name);
                result = _ui!.DragInt(AsStr(name, args[0]), AsNum(name, args[1]),
                                      args.Count > 2 ? AsNum(name, args[2]) : 1.0);
                return true;
            case "MakoUI.drag_range":
                if (args.Count < 3 || args.Count > 4) throw new MakoError("MakoUI.drag_range() expects (label, lo, hi, speed?)");
                EnsureUI(name);
                result = _ui!.DragRange(AsStr(name, args[0]), AsNum(name, args[1]), AsNum(name, args[2]),
                                        args.Count > 3 ? AsNum(name, args[3]) : 1.0);
                return true;

            // ── Tooltips ─────────────────────────────────────────────────────
            case "MakoUI.tooltip":
                if (args.Count != 1) throw new MakoError("MakoUI.tooltip() expects 1 argument");
                EnsureUI(name); _ui!.Tooltip(AsStr(name, args[0])); result = null; return true;
            case "MakoUI.set_tooltip":
                if (args.Count != 1) throw new MakoError("MakoUI.set_tooltip() expects 1 argument");
                EnsureUI(name); _ui!.SetTooltip(AsStr(name, args[0])); result = null; return true;

            // ── Combo ─────────────────────────────────────────────────────────
            case "MakoUI.combo":
                if (args.Count != 3) throw new MakoError("MakoUI.combo() expects 3 arguments (label, index, list)");
                EnsureUI(name);
                result = (double)_ui!.Combo(AsStr(name, args[0]), (int)AsNum(name, args[1]),
                                             AsList(name, args[2]));
                return true;

            // ── Text variants ─────────────────────────────────────────────────
            case "MakoUI.input_text_multi":
                if (args.Count < 2 || args.Count > 3) throw new MakoError("MakoUI.input_text_multi() expects 2 or 3 arguments");
                EnsureUI(name);
                result = _ui!.InputTextMulti(AsStr(name, args[0]), AsStr(name, args[1]),
                                              args.Count > 2 ? (int)AsNum(name, args[2]) : 6);
                return true;

            // ── Window variants ───────────────────────────────────────────────
            case "MakoUI.begin_window_menu":
                if (args.Count != 1) throw new MakoError("MakoUI.begin_window_menu() expects 1 argument");
                EnsureUI(name); result = (object?)_ui!.BeginWindowMenuBar(AsStr(name, args[0])); return true;

            case "MakoUI.begin_toolbar":
                if (args.Count < 1 || args.Count > 2)
                    throw new MakoError("MakoUI.begin_toolbar() expects (id, height=44)");
                EnsureUI(name);
                result = (object?)_ui!.BeginToolbar(AsStr(name, args[0]),
                    args.Count > 1 ? AsNum(name, args[1]) : 44);
                return true;

            // ── Query ─────────────────────────────────────────────────────────
            case "MakoUI.is_hovered":
                EnsureUI(name); result = (object?)_ui!.IsItemHovered(); return true;
            case "MakoUI.is_clicked":
                EnsureUI(name); result = (object?)_ui!.IsItemClicked(); return true;
            case "MakoUI.is_key_pressed":
                if (args.Count != 1) throw new MakoError("MakoUI.is_key_pressed() expects 1 argument");
                EnsureUI(name); result = (object?)_ui!.IsKeyPressed((int)AsNum(name, args[0])); return true;
            case "MakoUI.get_time":
                EnsureUI(name); result = _ui!.GetTime(); return true;
            case "MakoUI.framerate":
                EnsureUI(name); result = _ui!.GetFramerate(); return true;
            case "MakoUI.fps_counter":
                RequireArity(name, args, 0);
                EnsureUI(name); _ui!.FpsCounter(); result = null; return true;
            case "MakoUI.preview":
                if (args.Count < 1 || args.Count > 3) throw new MakoError("MakoUI.preview() expects (handle, width=220, height=220)");
                EnsureUI(name);
                _ui!.Preview((int)AsNum(name, args[0]),
                    args.Count > 1 ? (float)AsNum(name, args[1]) : 220,
                    args.Count > 2 ? (float)AsNum(name, args[2]) : 220);
                result = null; return true;
            case "MakoUI.wants_mouse":
                RequireArity(name, args, 0);
                EnsureUI(name); result = (object?)_ui!.WantsMouse(); return true;
            case "MakoUI.wants_keyboard":
                RequireArity(name, args, 0);
                EnsureUI(name); result = (object?)_ui!.WantsKeyboard(); return true;

            case "MakoUI.push_font":
                RequireArity(name, args, 2);
                EnsureUI(name); _ui!.PushFont(Stringify(args[0]), (int)AsNum(name, args[1])); result = null; return true;

            case "MakoUI.pop_font":
                RequireArity(name, args, 0);
                EnsureUI(name); _ui!.PopFont(); result = null; return true;

            case "MakoUI.set_default_font_size":
                RequireArity(name, args, 1);
                EnsureUI(name); _ui!.SetDefaultFontSize((float)AsNum(name, args[0])); result = null; return true;

            case "MakoUI.color_picker":
                if (args.Count != 4) throw new MakoError("MakoUI.color_picker() expects (label, r, g, b)");
                EnsureUI(name);
                result = _ui!.ColorPicker(AsStr(name, args[0]), AsNum(name, args[1]), AsNum(name, args[2]), AsNum(name, args[3]));
                return true;

            case "MakoUI.begin_tab_bar":
                RequireArity(name, args, 1);
                EnsureUI(name); result = _ui!.BeginTabBar(Stringify(args[0])); return true;
            case "MakoUI.end_tab_bar":
                RequireArity(name, args, 0);
                EnsureUI(name); _ui!.EndTabBar(); result = null; return true;
            case "MakoUI.begin_tab_item":
                RequireArity(name, args, 1);
                EnsureUI(name); result = _ui!.BeginTabItem(Stringify(args[0])); return true;
            case "MakoUI.end_tab_item":
                RequireArity(name, args, 0);
                EnsureUI(name); _ui!.EndTabItem(); result = null; return true;

            // ── MakoRay / Mako2D / Mako3D ────────────────────────────────────
            default:
                if (name.StartsWith("MakoRay.", StringComparison.OrdinalIgnoreCase))
                {
                    EnsureRay(name);
                    var fn2 = name["MakoRay.".Length..];
                    if (MakoRay.Funcs.TryGetValue(fn2, out var rayFn))
                        { result = rayFn(args); return true; }
                    throw new MakoError($"MakoRay.{fn2}() wasn't found");
                }
                if (name.StartsWith("Mako2D.", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_ray2DActive) throw new MakoError($"{name}() requires 'using Mako2D;'");
                    var fn2 = name["Mako2D.".Length..];
                    if (MakoRay2D.Funcs.TryGetValue(fn2, out var fn2d))
                        { result = fn2d(args); return true; }
                    throw new MakoError($"Mako2D.{fn2}() wasn't found");
                }
                if (name.StartsWith("Mako3D.", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_ray3DActive) throw new MakoError($"{name}() requires 'using Mako3D;'");
                    var fn3 = name["Mako3D.".Length..];
                    if (MakoRay3D.Funcs.TryGetValue(fn3, out var fn3d))
                        { result = fn3d(args); return true; }
                    throw new MakoError($"Mako3D.{fn3}() wasn't found");
                }
                if (name.StartsWith("Models.", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_modelsActive) throw new MakoError($"{name}() requires 'using Models;'");
                    var fn = name["Models.".Length..];
                    if (MakoModels.Funcs.TryGetValue(fn, out var modelFn))
                        { result = modelFn(args); return true; }
                    throw new MakoError($"Models.{fn}() wasn't found");
                }
                if (name.StartsWith("Players.", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_playersActive) throw new MakoError($"{name}() requires 'using Players;'");
                    var fn = name["Players.".Length..];
                    if (MakoPlayers.Funcs.TryGetValue(fn, out var playerFn))
                        { result = playerFn(args); return true; }
                    throw new MakoError($"Players.{fn}() wasn't found");
                }
                if (name.StartsWith("Controllers.", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_controllersActive) throw new MakoError($"{name}() requires 'using Controllers;'");
                    var fn = name["Controllers.".Length..];
                    if (MakoControllers.Funcs.TryGetValue(fn, out var controllerFn))
                        { result = controllerFn(args); return true; }
                    throw new MakoError($"Controllers.{fn}() wasn't found");
                }
                if (name.StartsWith("Save.", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_saveActive) throw new MakoError($"{name}() requires 'using Save;'");
                    var fn = name["Save.".Length..];
                    if (MakoSave.Funcs.TryGetValue(fn, out var saveFn))
                        { result = saveFn(args); return true; }
                    throw new MakoError($"Save.{fn}() wasn't found");
                }
                if (name.StartsWith("ANIX.", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_anixActive) throw new MakoError($"{name}() requires 'using ANIX;'");
                    var fn = name["ANIX.".Length..];
                    if (MakoAnix.Funcs.TryGetValue(fn, out var anixFn))
                        { result = anixFn(args); return true; }
                    throw new MakoError($"ANIX.{fn}() wasn't found");
                }
                if (name.StartsWith("Physics2D.", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_physics2DActive) throw new MakoError($"{name}() requires 'using Physics2D;'");
                    var fnP = name["Physics2D.".Length..];
                    if (MakoPhysics2D.Funcs.TryGetValue(fnP, out var fnPhysics))
                        { result = fnPhysics(args); return true; }
                    throw new MakoError($"Physics2D.{fnP}() wasn't found");
                }
                if (name.StartsWith("Physics3D.", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_physics3DActive) throw new MakoError($"{name}() requires 'using Physics3D;'");
                    var fnP3 = name["Physics3D.".Length..];
                    if (MakoPhysics3D.Funcs.TryGetValue(fnP3, out var fnPhysics3))
                        { result = fnPhysics3(args); return true; }
                    throw new MakoError($"Physics3D.{fnP3}() wasn't found");
                }
                if (name.StartsWith("Inputs.", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_inputsActive) throw new MakoError($"{name}() requires 'using Inputs;'");
                    var fnI = name["Inputs.".Length..];
                    if (MakoInputs.Funcs.TryGetValue(fnI, out var fnIn))
                        { result = fnIn(args); return true; }
                    throw new MakoError($"Inputs.{fnI}() wasn't found");
                }
                if (name.StartsWith("Audio.", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_audioActive) throw new MakoError($"{name}() requires 'using Audio;'");
                    var fnA = name["Audio.".Length..];
                    if (MakoAudio.Funcs.TryGetValue(fnA, out var fnAu))
                        { result = fnAu(args); return true; }
                    throw new MakoError($"Audio.{fnA}() wasn't found");
                }
                if (name.StartsWith("Net.", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_netActive) throw new MakoError($"{name}() requires 'using Net;'");
                    var fnN = name["Net.".Length..];
                    if (MakoNet.Funcs.TryGetValue(fnN, out var fnNe))
                        { result = fnNe(args); return true; }
                    throw new MakoError($"Net.{fnN}() wasn't found");
                }
                if (name.StartsWith("Room.", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_roomActive) throw new MakoError($"{name}() requires 'using Room;'");
                    var fnR = name["Room.".Length..];
                    if (MakoRoom.Funcs.TryGetValue(fnR, out var fnRo))
                        { result = fnRo(args); return true; }
                    throw new MakoError($"Room.{fnR}() wasn't found");
                }
                if (name.Equals("Font.load", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_fontActive) throw new MakoError($"{name}() requires 'using Font;'");
                    if (args.Count < 2 || args.Count > 3) throw new MakoError("Font.load() expects (name, path [, default_size])");
                    EnsureUI(name);
                    _ui!.LoadFont(Stringify(args[0]), Stringify(args[1]), args.Count > 2 ? (int)AsNum(name, args[2]) : 16);
                    result = null; return true;
                }
                if (name.StartsWith("System.", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_systemActive) throw new MakoError($"{name}() requires 'using System;'");
                    var fnS = name["System.".Length..];
                    if (MakoSystem.Funcs.TryGetValue(fnS, out var fnSy))
                        { result = fnSy(args); return true; }
                    throw new MakoError($"System.{fnS}() wasn't found");
                }
                // ── Host-registered functions (embedding) ─────────────────────────
                // Any "Namespace.func" call that didn't match one of MAKO's own
                // built-in packages above falls through to the embedding host's
                // context, if one was supplied — see MakoHostContext. Gated by
                // RegisterPackage the same way built-ins are gated by
                // 'using X;', so a host namespace behaves identically to a
                // built-in one from a script's point of view.
                if (_host != null)
                {
                    int dot = name.IndexOf('.');
                    if (dot > 0)
                    {
                        var ns = name[..dot];
                        if (_hostPackagesActive.Contains(ns))
                        {
                            if (_host.TryGetFunction(name, out var hostFn))
                                { result = hostFn(args); return true; }
                            throw new MakoError($"{name}() wasn't found");
                        }
                    }
                }
                return false;
        }
    }

    private void EnsureRay(string fn)
    {
        if (!_rayActive)
            throw new MakoError(
                $"{fn}() requires 'using MakoRay;' at the top of the script");
    }

    private void EnsureUI(string fn)
    {
        if (_ui is null)
            throw new MakoError(
                $"{fn}() requires 'using MakoUI;' at the top of the script");
    }

    private static void RequireArity(string name, List<object?> args, int expected)
    {
        if (args.Count != expected)
            throw new MakoError($"{name}() expects {expected} argument(s), got {args.Count}");
    }

    private static string       AsStr(string fn, object? v) =>
        v is string s ? s : throw new MakoError($"{fn}() expects a string, got {TypeName(v)} '{Short(v)}'");
    private static bool AsBool(object? v) => Truthy(v);
    private static Dictionary<string, object?> AsDict(string fn, object? v) =>
        v is Dictionary<string, object?> d ? d
            : throw new MakoError($"{fn}() expects a dict, got {TypeName(v)} '{Short(v)}'");
    private static List<object?> AsList(string fn, object? v) =>
        v is List<object?> l ? l : throw new MakoError($"{fn}() expects a list, got {TypeName(v)} '{Short(v)}'");
    private static double AsNum(string fn, object? v)
    {
        try { return ToNumber(v); }
        catch (MakoError)
        {
            throw new MakoError(v is null
                ? $"{fn}() expects a number, got none"
                : $"{fn}() expects a number, got {TypeName(v)} '{Short(v)}'");
        }
    }

    // ── Scope helpers ─────────────────────────────────────────────────────────

    private void PushScope() => _scopes.Add(new Scope());
    private void PopScope()  => _scopes.RemoveAt(_scopes.Count - 1);
    private void PushCallScope()
    {
        PushScope();
        _callScopeBases.Push(_scopes.Count - 1);
    }
    private void PopCallScope()
    {
        _callScopeBases.Pop();
        PopScope();
    }

    private IEnumerable<int> VisibleScopeIndices()
    {
        if (_callScopeBases.Count == 0)
        {
            for (int i = _scopes.Count - 1; i >= 0; i--) yield return i;
            yield break;
        }

        int callBase = _callScopeBases.Peek();
        for (int i = _scopes.Count - 1; i >= callBase; i--) yield return i;
        if (callBase > 0) yield return 0;
    }

    private bool TryGetVar(string name, out object? value)
    {
        foreach (int i in VisibleScopeIndices())
            if (_scopes[i].Vars.TryGetValue(name, out value)) return true;
        value = null;
        return false;
    }

    private object? GetVar(string name)
    {
        foreach (int i in VisibleScopeIndices())
            if (_scopes[i].Vars.TryGetValue(name, out var v)) return v;

        // Not found as a flattened "Base.Field" name (how 'using'd namespace
        // constants like MakoRay.RED are stored). If "Base" is a variable
        // holding a dict/struct instance, treat this as field access instead
        // — the parser can't tell struct-field access from a namespace
        // constant apart at parse time (both are just "Ident.Ident"), so
        // this fallback is where that ambiguity actually gets resolved.
        int dot = name.IndexOf('.');
        if (dot > 0 && TryGetVar(name[..dot], out var baseVal) &&
            baseVal is Dictionary<string, object?> dict)
        {
            var field = name[(dot + 1)..];
            if (dict.TryGetValue(field, out var fieldVal)) return fieldVal;
            throw new MakoError($"'{TypeName(dict)}' has no field '{field}'");
        }

        var candidates  = _scopes.SelectMany(s => s.Vars.Keys)
                                 .Concat(_funcs.Keys)
                                 .Concat(BuiltinNames);
        var suggestion = Suggest.Closest(name, candidates);
        throw new MakoError($"unknown variable '{name}'")
        {
            Hint = suggestion != null ? $"did you mean '{suggestion}'?" : null
        };
    }

    private void SetVar(string name, object? value)
    {
        foreach (int i in VisibleScopeIndices())
        {
            if (_scopes[i].Vars.ContainsKey(name))
            {
                if (_scopes[i].Consts.Contains(name))
                    throw new MakoError($"cannot reassign const '{name}'");
                _scopes[i].Vars[name] = value;
                return;
            }
        }
        _scopes[^1].Vars[name] = value;
    }

    // ── Value helpers ─────────────────────────────────────────────────────────

    private static string ReadInput(string prompt)
    {
        Console.Write(prompt);
        return Console.ReadLine() ?? "";
    }

    private static void RunShellCommand(string cmd)
    {
        var proc = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/sh", Arguments = $"-c \"{cmd.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
            }
        };
        proc.Start();
        proc.WaitForExit();
    }

    private static bool Truthy(object? val) => val switch
    {
        bool b          => b,
        double d        => d != 0,
        string s        => s.Length > 0,
        List<object?> l => l.Count > 0,
        null            => false,
        _               => true,
    };

    // ── Grid pathfinding helpers ──────────────────────────────────────────────

    private static bool GridBlocked(List<object?> grid, int x, int y)
    {
        if (y < 0 || y >= grid.Count) return true;
        if (grid[y] is not List<object?> row) return true;
        if (x < 0 || x >= row.Count) return true;
        return Truthy(row[x]);
    }

    /// A* over a 4-connected grid. Returns steps after the start, goal included.
    private static List<object?> FindPath(List<object?> grid, int sx, int sy, int ex, int ey)
    {
        var empty = new List<object?>();
        if (GridBlocked(grid, sx, sy) || GridBlocked(grid, ex, ey)) return empty;
        if (sx == ex && sy == ey) return empty;

        int rows = grid.Count;
        int cols = 0;
        foreach (var r in grid)
            if (r is List<object?> rl) cols = Math.Max(cols, rl.Count);

        var open    = new PriorityQueue<(int x, int y), int>();
        var gScore  = new Dictionary<(int, int), int>();
        var parent  = new Dictionary<(int, int), (int, int)>();

        gScore[(sx, sy)] = 0;
        open.Enqueue((sx, sy), Math.Abs(ex - sx) + Math.Abs(ey - sy));

        Span<(int dx, int dy)> dirs = stackalloc[] { (1, 0), (-1, 0), (0, 1), (0, -1) };

        while (open.Count > 0)
        {
            var cur = open.Dequeue();
            if (cur.x == ex && cur.y == ey)
            {
                // Reconstruct: goal back to (but not including) start
                var path = new List<object?>();
                var node = (cur.x, cur.y);
                while (node != (sx, sy))
                {
                    path.Add(new List<object?> { (object?)(double)node.Item1, (double)node.Item2 });
                    node = parent[node];
                }
                path.Reverse();
                return path;
            }

            int g = gScore[(cur.x, cur.y)];
            foreach (var (dx, dy) in dirs)
            {
                int nx = cur.x + dx, ny = cur.y + dy;
                if (GridBlocked(grid, nx, ny)) continue;
                int ng = g + 1;
                if (gScore.TryGetValue((nx, ny), out int prev) && prev <= ng) continue;
                gScore[(nx, ny)] = ng;
                parent[(nx, ny)] = (cur.x, cur.y);
                open.Enqueue((nx, ny), ng + Math.Abs(ex - nx) + Math.Abs(ey - ny));
            }
        }
        return empty;   // unreachable
    }

    /// Bresenham line between cell centres; false if any wall cell is crossed.
    private static bool LineOfSight(List<object?> grid, int x1, int y1, int x2, int y2)
    {
        int dx = Math.Abs(x2 - x1), dy = Math.Abs(y2 - y1);
        int stepX = x1 < x2 ? 1 : -1, stepY = y1 < y2 ? 1 : -1;
        int err = dx - dy;
        int x = x1, y = y1;

        while (true)
        {
            if (GridBlocked(grid, x, y)) return false;
            if (x == x2 && y == y2) return true;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += stepX; }
            if (e2 <  dx) { err += dx; y += stepY; }
        }
    }

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a is double da && b is double db) return Math.Abs(da - db) < 1e-12;
        return a.Equals(b);
    }

    private static double ToNumber(object? val)
    {
        if (val is double d) return d;
        if (val is string s && double.TryParse(s,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var n))
            return n;
        throw new MakoError(val is null
            ? "expected a number, got none"
            : $"expected a number, got {TypeName(val)} '{Short(val)}'");
    }

    private static int NormalizeIndex(int i, int len, string what = "list")
    {
        int adj = i < 0 ? len + i : i;
        if (adj < 0 || adj >= len)
            throw new MakoError(len == 0
                ? $"index {i} is out of range — the {what} is empty"
                : $"index {i} is out of range (valid: 0 to {len - 1}, or -1 to -{len})");
        return adj;
    }

    /// Value preview for error messages, truncated so huge lists/strings
    /// don't flood the output.
    private static string Short(object? v)
    {
        var s = Stringify(v);
        return s.Length > 24 ? s[..21] + "..." : s;
    }

    /// Key structs are tagged with internally, so a struct instance can
    /// carry its type name (for type() and method dispatch) while still
    /// being a plain dict everywhere else — indexing, merge(), json_encode,
    /// etc. all keep working on it unchanged.
    private const string StructTypeKey = "__type";

    private static string TypeName(object? val) => val switch
    {
        null                          => "none",
        bool                          => "bool",
        double                        => "number",
        string                        => "string",
        List<object?>                 => "list",
        Dictionary<string, object?> d => d.TryGetValue(StructTypeKey, out var t) && t is string ts ? ts : "dict",
        MakoFn                        => "fn",
        _                             => "unknown",
    };

    public static string Stringify(object? val) => val switch
    {
        null          => "none",
        bool b        => b ? "true" : "false",
        double d      => d == Math.Floor(d) && !double.IsInfinity(d)
                         ? ((long)d).ToString()
                         : d.ToString(System.Globalization.CultureInfo.InvariantCulture),
        string s      => s,
        List<object?> l => "[" + string.Join(", ", l.Select(Stringify)) + "]",
        Dictionary<string, object?> d =>
            "{" + string.Join(", ", d.Select(kv => $"\"{kv.Key}\": {StringifyValue(kv.Value)}")) + "}",
        MakoFn f      => f.ToString(),
        _             => val.ToString() ?? "none",
    };

    // Like Stringify but quotes strings so dict/list printouts are unambiguous.
    private static string StringifyValue(object? val) => val switch
    {
        string s => $"\"{s}\"",
        _        => Stringify(val),
    };
}
