namespace Mako;

// Control-flow signals
file sealed class ReturnSignal(object? value) : Exception { public object? Value { get; } = value; }
file sealed class BreakSignal()    : Exception;
file sealed class ContinueSignal() : Exception;

/// Tree-walk interpreter.
/// Runtime value types:  string | double | bool | List<object?> | null
class Interpreter
{
    private readonly Dictionary<string, FnDecl> _funcs = new();

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
            throw new MakoError(e.RawMessage, stmt.Line, stmt.Col, len);
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
                var listTarget = GetVar(ia.Name);
                if (listTarget is not List<object?> lst)
                    throw new MakoError(
                        $"cannot assign by index into {TypeName(listTarget)} — '{ia.Name}' must be a list");
                lst[NormalizeIndex((int)ToNumber(Eval(ia.Index)), lst.Count)] = Eval(ia.Value);
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
                if (iterable is not List<object?> items)
                    throw new MakoError($"'for' needs a list to loop over, got {TypeName(iterable)}"
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
            throw new MakoError(e.RawMessage, expr.Line, expr.Col, len);
        }
    }

    private object? EvalCore(Expr expr) => expr switch
    {
        StringLit s          => s.Value,
        NumberLit n          => n.Value,
        BoolLit b            => b.Value,
        NullLit              => null,
        ListLit l            => l.Items.ConvertAll(Eval),
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

    private object? EvalIndex(IndexExpr ix)
    {
        var target = Eval(ix.Target);
        var index  = Eval(ix.Index);
        if (index is not double dIdx)
            throw new MakoError($"list index must be a number, got {TypeName(index)} '{Short(index)}'");
        var raw = (int)dIdx;
        if (target is List<object?> list) return list[NormalizeIndex(raw, list.Count, "list")];
        if (target is string s)           return s[NormalizeIndex(raw, s.Length, "string")].ToString();
        throw new MakoError($"cannot index into {TypeName(target)} — only lists and strings can be indexed");
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
            var hint = Suggest.Closest(name, _funcs.Keys.Concat(BuiltinNames));
            throw new MakoError(hint != null
                ? $"function '{name}' wasn't found (did you mean '{hint}'?)"
                : $"function '{name}' wasn't found (got null reference)");
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
                    string s        => (double)s.Length,
                    List<object?> l => (double)l.Count,
                    _ => throw new MakoError($"len() expects a string or list, got '{TypeName(args[0])}'"),
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
                result = AsList(name, args[0]).Any(v => ValuesEqual(v, args[1]));
                return true;

            default:
                return false;
        }
    }

    private static void RequireArity(string name, List<object?> args, int expected)
    {
        if (args.Count != expected)
            throw new MakoError($"{name}() expects {expected} argument(s), got {args.Count}");
    }

    private static string       AsStr(string fn, object? v) =>
        v is string s ? s : throw new MakoError($"{fn}() expects a string, got {TypeName(v)} '{Short(v)}'");
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

    private object? GetVar(string name)
    {
        for (int i = _scopes.Count - 1; i >= 0; i--)
            if (_scopes[i].Vars.TryGetValue(name, out var v)) return v;

        var candidates = _scopes.SelectMany(s => s.Vars.Keys)
                                .Concat(_funcs.Keys)
                                .Concat(BuiltinNames);
        var hint = Suggest.Closest(name, candidates);
        throw new MakoError(hint != null
            ? $"type, function, or name '{name}' wasn't found (did you mean '{hint}'?)"
            : $"type, function, or name '{name}' wasn't found (got null reference)");
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
        null          => "none",
        bool          => "bool",
        double        => "number",
        string        => "string",
        List<object?> => "list",
        _             => "unknown",
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
        _             => val.ToString() ?? "none",
    };
}
