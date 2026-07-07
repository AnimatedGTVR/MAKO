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
                throw new MakoError($"Cannot find module '{importPath}'");

            var src    = File.ReadAllText(fullPath);
            var module = new Parser(new Lexer(src).Tokenize()).Parse();
            var modNs  = module.Namespace
                ?? throw new MakoError($"Module '{importPath}' must declare a namespace");

            foreach (var fn in module.Functions)
                _funcs[$"{modNs}.{fn.Name}"] = fn;
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

    private void RunStatement(Statement stmt)
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
                    throw new MakoError($"Cannot index into '{TypeName(listTarget)}'");
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
                    throw new MakoError($"'for' requires a list, got '{TypeName(iterable)}'");
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

    private object? Eval(Expr expr) => expr switch
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
        var raw    = (int)ToNumber(Eval(ix.Index));
        if (target is List<object?> list) return list[NormalizeIndex(raw, list.Count)];
        if (target is string s)           return s[NormalizeIndex(raw, s.Length)].ToString();
        throw new MakoError($"Cannot index into '{TypeName(target)}'");
    }

    private object? EvalUnary(UnaryExpr u)
    {
        var val = Eval(u.Operand);
        return u.Op switch
        {
            "!" => !Truthy(val),
            "-" => val is double d ? -d : throw new MakoError($"Cannot negate '{Stringify(val)}'"),
            _   => throw new MakoError($"Unknown unary operator '{u.Op}'"),
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
            double l = ToNumber(left), r = ToNumber(right);
            return op switch
            {
                "+" => l + r,
                "-" => l - r,
                "*" => l * r,
                "/" => r == 0 ? throw new MakoError("Division by zero") : l / r,
                "%" => r == 0 ? throw new MakoError("Modulo by zero")   : l % r,
                _   => throw new MakoError($"Unknown operator '{op}'"),
            };
        }

        if (op == "==") return  ValuesEqual(left, right);
        if (op == "!=") return !ValuesEqual(left, right);

        double lc = ToNumber(left), rc = ToNumber(right);
        return op switch
        {
            "<"  => lc < rc, ">"  => lc > rc,
            "<=" => lc <= rc, ">=" => lc >= rc,
            _    => throw new MakoError($"Unknown operator '{op}'"),
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
            throw new MakoError($"Undefined function '{name}'");

        if (args.Count != fn.Params.Count)
            throw new MakoError($"'{name}' expects {fn.Params.Count} argument(s), got {args.Count}");

        PushScope();
        for (int i = 0; i < fn.Params.Count; i++)
            _scopes[^1].Vars[fn.Params[i]] = args[i];

        object? ret = null;
        try   { foreach (var s in fn.Body) RunStatement(s); }
        catch (ReturnSignal sig) { ret = sig.Value; }
        finally { PopScope(); }
        return ret;
    }

    private bool TryBuiltin(string name, List<object?> args, out object? result)
    {
        result = null;
        switch (name)
        {
            // ── Type / conversion ─────────────────────────────────────────────
            case "type":    RequireArity(name, args, 1); result = TypeName(args[0]); return true;
            case "to_num":  RequireArity(name, args, 1); result = ToNumber(args[0]); return true;
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
            case "abs":   RequireArity(name, args, 1); result = Math.Abs(ToNumber(args[0]));     return true;
            case "floor": RequireArity(name, args, 1); result = Math.Floor(ToNumber(args[0]));   return true;
            case "ceil":  RequireArity(name, args, 1); result = Math.Ceiling(ToNumber(args[0])); return true;
            case "sqrt":  RequireArity(name, args, 1); result = Math.Sqrt(ToNumber(args[0]));    return true;
            case "round": RequireArity(name, args, 1);
                result = Math.Round(ToNumber(args[0]), MidpointRounding.AwayFromZero); return true;
            case "pow":   RequireArity(name, args, 2); result = Math.Pow(ToNumber(args[0]), ToNumber(args[1])); return true;
            case "max":   RequireArity(name, args, 2); result = Math.Max(ToNumber(args[0]), ToNumber(args[1])); return true;
            case "min":   RequireArity(name, args, 2); result = Math.Min(ToNumber(args[0]), ToNumber(args[1])); return true;

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
        v is string s ? s : throw new MakoError($"{fn}() expects a string, got '{TypeName(v)}'");
    private static List<object?> AsList(string fn, object? v) =>
        v is List<object?> l ? l : throw new MakoError($"{fn}() expects a list, got '{TypeName(v)}'");

    // ── Scope helpers ─────────────────────────────────────────────────────────

    private void PushScope() => _scopes.Add(new Scope());
    private void PopScope()  => _scopes.RemoveAt(_scopes.Count - 1);

    private object? GetVar(string name)
    {
        for (int i = _scopes.Count - 1; i >= 0; i--)
            if (_scopes[i].Vars.TryGetValue(name, out var v)) return v;
        throw new MakoError($"Variable '{name}' is not defined");
    }

    private void SetVar(string name, object? value)
    {
        for (int i = _scopes.Count - 1; i >= 0; i--)
        {
            if (_scopes[i].Vars.ContainsKey(name))
            {
                if (_scopes[i].Consts.Contains(name))
                    throw new MakoError($"Cannot reassign const '{name}'");
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
        throw new MakoError($"Expected a number, got '{Stringify(val)}'");
    }

    private static int NormalizeIndex(int i, int len)
    {
        if (i < 0) i = len + i;
        if (i < 0 || i >= len) throw new MakoError($"Index {i} out of range (length {len})");
        return i;
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
