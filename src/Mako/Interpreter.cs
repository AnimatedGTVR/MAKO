namespace Mako;

// Control-flow signals
file sealed class ReturnSignal(object? value) : Exception { public object? Value { get; } = value; }
file sealed class BreakSignal()    : Exception;
file sealed class ContinueSignal() : Exception;

/// A first-class lambda / closure value.
sealed class MakoFn(List<string> parms, List<Statement> body, Dictionary<string, object?> captured)
{
    public List<string>             Params   { get; } = parms;
    public List<Statement>          Body     { get; } = body;
    public Dictionary<string,object?> Captured { get; } = captured;
    public override string ToString() => $"fn({string.Join(", ", Params)})"  ;
}

/// Tree-walk interpreter.
/// Runtime value types:  string | double | bool | List<object?> | null
class Interpreter
{
    private readonly Dictionary<string, FnDecl> _funcs = new();
    private MakoUI? _ui;

    // Each scope holds variable values and a set of const names.
    private sealed class Scope
    {
        public Dictionary<string, object?> Vars   { get; } = new();
        public HashSet<string>             Consts { get; } = new();
    }
    private readonly List<Scope> _scopes = [new()];

    // ── Entry point ───────────────────────────────────────────────────────────

    public void Execute(ProgramNode program, string baseDir = "")
    {
        try { ExecuteCore(program, baseDir); }
        finally { _ui?.Dispose(); _ui = null; }
    }

    /// REPL entry — runs in the persistent interpreter state, prints expression results.
    public void ExecuteRepl(ProgramNode program)
    {
        // Register any new functions declared in this line.
        foreach (var fn in program.Functions)
            _funcs[fn.Name] = fn;

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
            }
        }
    }

    private void ExecuteCore(ProgramNode program, string baseDir)
    {
        // ── using PackageName [from "source"] — named packages ───────────────
        foreach (var pkg in program.Packages)
        {
            if (pkg.Source != null)
                PackageManager.RegisterSource(pkg.Name, pkg.Source);

            PackageManager.Ensure(pkg.Name);

            if (PackageManager.NativePackages.Contains(pkg.Name))
            {
                _ui ??= new MakoUI();
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

        // Top-level const declarations — evaluated once, marked immutable globally.
        foreach (var (cname, cexpr) in program.Constants)
        {
            var cval = Eval(cexpr);
            SetVar(cname, cval);
            _scopes[0].Consts.Add(cname);
        }

        try   { foreach (var stmt in program.Body) RunStatement(stmt); }
        catch (ReturnSignal) { }
    }

    // ── Statements ────────────────────────────────────────────────────────────

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

            case ConstStmt c:
                var constVal = Eval(c.Value);
                if (_scopes[^1].Consts.Contains(c.Name))
                    throw new MakoError($"'{c.Name}' is already declared as const");
                _scopes[^1].Consts.Add(c.Name);
                _scopes[^1].Vars[c.Name] = constVal;
                break;

            case IndexAssignStmt ia:
                var indexTarget = GetVar(ia.Name);
                if (indexTarget is List<object?> lst)
                    lst[NormalizeIndex((int)ToNumber(Eval(ia.Index)), lst.Count)] = Eval(ia.Value);
                else if (indexTarget is Dictionary<string, object?> dictTarget)
                    dictTarget[Stringify(Eval(ia.Index))] = Eval(ia.Value);
                else
                    throw new MakoError(
                        $"cannot assign by index into {TypeName(indexTarget)} — '{ia.Name}' must be a list or dict");
                break;

            case IfStmt i:
                if (Truthy(Eval(i.Condition)))
                    foreach (var s in i.Then) RunStatement(s);
                else
                    foreach (var s in i.Else) RunStatement(s);
                break;

            case WhileStmt w:
                try
                {
                    while (Truthy(Eval(w.Condition)))
                        try   { foreach (var s in w.Body) RunStatement(s); }
                        catch (ContinueSignal) { }
                }
                catch (BreakSignal) { }
                break;

            case ForStmt f:
                var iterable = Eval(f.Iterable);
                List<object?> items;
                if (iterable is List<object?> lst2)
                    items = lst2;
                else if (iterable is Dictionary<string, object?> iterDict)
                    items = iterDict.Keys.Select(k => (object?)k).ToList();
                else
                    throw new MakoError($"'for' needs a list or dict to loop over, got {TypeName(iterable)}"
                        + (iterable is string ? " — to loop over characters, use split(text, \"\")" : "")
                        + (iterable is double ? " — to loop over numbers, use range(n)" : ""));
                try
                {
                    foreach (var item in new List<object?>(items))
                    {
                        SetVar(f.Var, item);
                        try   { foreach (var s in f.Body) RunStatement(s); }
                        catch (ContinueSignal) { }
                    }
                }
                catch (BreakSignal) { }
                break;

            case BreakStmt:    throw new BreakSignal();
            case ContinueStmt: throw new ContinueSignal();

            case ReturnStmt r:
                throw new ReturnSignal(r.Value is null ? null : Eval(r.Value));

            case RunStmt r:
                RunShellCommand(Stringify(Eval(r.Command)));
                break;

            case TryStmt ts:
                try
                {
                    PushScope();
                    try   { foreach (var s in ts.Try) RunStatement(s); }
                    finally { PopScope(); }
                }
                catch (MakoError e) when (ts.Catch.Count > 0)
                {
                    PushScope();
                    if (ts.CatchVar != null) SetVar(ts.CatchVar, e.RawMessage);
                    try   { foreach (var s in ts.Catch) RunStatement(s); }
                    finally { PopScope(); }
                }
                catch (Exception e) when (e is not (ReturnSignal or BreakSignal or ContinueSignal)
                                              && ts.Catch.Count > 0)
                {
                    PushScope();
                    if (ts.CatchVar != null) SetVar(ts.CatchVar, e.Message);
                    try   { foreach (var s in ts.Catch) RunStatement(s); }
                    finally { PopScope(); }
                }
                break;

            case ExprStmt e:
                Eval(e.Value);
                break;

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
        StringLit s          => s.Value,
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
        NamespacedCallExpr n => CallFunction($"{n.Ns}.{n.Func}", n.Args),
        _                    => throw new MakoError($"Unknown expression type: {expr.GetType().Name}"),
    };

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

        PushScope();
        // Inject captured variables first, then params (params shadow captures).
        foreach (var (k, v) in fn.Captured)
            _scopes[^1].Vars[k] = v;
        for (int i = 0; i < fn.Params.Count; i++)
            _scopes[^1].Vars[fn.Params[i]] = args[i];

        try
        {
            foreach (var stmt in fn.Body) RunStatement(stmt);
            return null;
        }
        catch (ReturnSignal r) { return r.Value; }
        finally { PopScope(); }
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

        PushScope();
        for (int i = 0; i < fn.Params.Count; i++)
            _scopes[^1].Vars[fn.Params[i]] = args[i];

        object? ret = null;
        try   { foreach (var s in fn.Body) RunStatement(s); }
        catch (ReturnSignal sig) { ret = sig.Value; }
        catch (MakoError e) when (fn.Source != null && e.SourcePath is null)
        {
            // Error inside an imported function: its line numbers refer to the
            // module file, so tag the error with that path for the CLI snippet.
            throw new MakoError(e.RawMessage, e.Line, e.Col, e.Length)
                  { SourcePath = fn.Source };
        }
        finally { PopScope(); }
        return ret;
    }

    private static readonly string[] BuiltinNames =
    [
        "type", "to_num", "to_str", "exit", "assert",
        "abs", "floor", "ceil", "sqrt", "round", "pow", "max", "min", "range",
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
        "MakoUI.init", "MakoUI.running", "MakoUI.begin", "MakoUI.end",
        // MakoUI — windows
        "MakoUI.begin_window", "MakoUI.end_window", "MakoUI.begin_window_menu",
        // MakoUI — widgets
        "MakoUI.text", "MakoUI.text_colored", "MakoUI.button", "MakoUI.small_button",
        "MakoUI.checkbox", "MakoUI.slider", "MakoUI.slider_int",
        "MakoUI.drag", "MakoUI.drag_int",
        "MakoUI.input_text", "MakoUI.input_number", "MakoUI.input_text_multi",
        "MakoUI.combo",
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
        "MakoUI.get_time", "MakoUI.framerate",
        // MakoUI — style & themes
        "MakoUI.push_color", "MakoUI.pop_color", "MakoUI.push_var", "MakoUI.pop_var",
        "MakoUI.theme_dark", "MakoUI.theme_light", "MakoUI.theme_mako",
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
                    Dictionary<string, object?> d => (double)d.Count,
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
                result = AsDict(name, args[0]).Keys.Select(k => (object?)k).ToList();
                return true;

            case "values":
                RequireArity(name, args, 1);
                result = AsDict(name, args[0]).Values.ToList();
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

            // ── MakoUI ────────────────────────────────────────────────────────
            case "MakoUI.init":
                RequireArity(name, args, 3);
                EnsureUI(name);
                _ui!.Init(Stringify(args[0]), (int)AsNum(name, args[1]), (int)AsNum(name, args[2]));
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
                RequireArity(name, args, 5);
                EnsureUI(name);
                _ui!.TextColored(AsNum(name, args[0]), AsNum(name, args[1]),
                                 AsNum(name, args[2]), AsNum(name, args[3]),
                                 Stringify(args[4]));
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

            default:
                return false;
        }
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

    private bool TryGetVar(string name, out object? value)
    {
        for (int i = _scopes.Count - 1; i >= 0; i--)
            if (_scopes[i].Vars.TryGetValue(name, out value)) return true;
        value = null;
        return false;
    }

    private object? GetVar(string name)
    {
        for (int i = _scopes.Count - 1; i >= 0; i--)
            if (_scopes[i].Vars.TryGetValue(name, out var v)) return v;

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
        for (int i = _scopes.Count - 1; i >= 0; i--)
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

    private static string TypeName(object? val) => val switch
    {
        null                          => "none",
        bool                          => "bool",
        double                        => "number",
        string                        => "string",
        List<object?>                 => "list",
        Dictionary<string, object?>   => "dict",
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
