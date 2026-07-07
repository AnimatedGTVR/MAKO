namespace Mako;

/// Tree-walk interpreter.
/// Values are boxed as object?:  string, double, bool, or null.
class Interpreter
{
    private readonly Dictionary<string, object?> _vars = new();

    // ── Entry point ───────────────────────────────────────────────────────────

    public void Execute(ProgramNode program)
    {
        foreach (var stmt in program.Body)
            RunStatement(stmt);
    }

    // ── Statements ────────────────────────────────────────────────────────────

    private void RunStatement(Statement stmt)
    {
        switch (stmt)
        {
            case PrintStmt p:
                Console.WriteLine(Stringify(Eval(p.Value)));
                break;

            case AssignStmt a:
                _vars[a.Name] = Eval(a.Value);
                break;

            case IfStmt i:
                if (Truthy(Eval(i.Condition)))
                    foreach (var s in i.Then) RunStatement(s);
                else
                    foreach (var s in i.Else) RunStatement(s);
                break;

            case RunStmt r:
                RunShellCommand(Stringify(Eval(r.Command)));
                break;

            default:
                throw new MakoError($"Unknown statement type: {stmt.GetType().Name}");
        }
    }

    // ── Expressions ───────────────────────────────────────────────────────────

    private object? Eval(Expr expr) => expr switch
    {
        StringLit s   => s.Value,
        NumberLit n   => n.Value,
        BoolLit b     => b.Value,
        IdentExpr id  => LookupVar(id.Name),
        InputExpr inp => ReadInput(Stringify(Eval(inp.Prompt))),
        UnaryExpr u   => EvalUnary(u),
        BinaryExpr bin => EvalBinary(bin),
        _             => throw new MakoError($"Unknown expression type: {expr.GetType().Name}"),
    };

    private object? EvalUnary(UnaryExpr u) => u.Op switch
    {
        "!" => !Truthy(Eval(u.Operand)),
        _   => throw new MakoError($"Unknown unary operator '{u.Op}'"),
    };

    private object? EvalBinary(BinaryExpr bin)
    {
        var left  = Eval(bin.Left);
        var right = Eval(bin.Right);
        string op = bin.Op;

        // String concatenation — if either side is a string, join with +
        if (op == "+" && (left is string || right is string))
            return Stringify(left) + Stringify(right);

        // Numeric arithmetic
        if (op is "+" or "-" or "*" or "/")
        {
            double l = ToNumber(left, bin.Left);
            double r = ToNumber(right, bin.Right);
            return op switch
            {
                "+" => l + r,
                "-" => l - r,
                "*" => l * r,
                "/" => r == 0
                    ? throw new MakoError("Division by zero")
                    : l / r,
                _ => throw new MakoError($"Unknown operator '{op}'"),
            };
        }

        // Equality (works for any type)
        if (op == "==") return ValuesEqual(left, right);
        if (op == "!=") return !ValuesEqual(left, right);

        // Numeric comparison
        {
            double l = ToNumber(left, bin.Left);
            double r = ToNumber(right, bin.Right);
            return op switch
            {
                "<"  => l < r,
                ">"  => l > r,
                "<=" => l <= r,
                ">=" => l >= r,
                _    => throw new MakoError($"Unknown operator '{op}'"),
            };
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private object? LookupVar(string name)
    {
        if (_vars.TryGetValue(name, out var val)) return val;
        throw new MakoError($"Variable '{name}' is not defined");
    }

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
                FileName = "/bin/sh",
                Arguments = $"-c \"{cmd.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
            }
        };
        proc.Start();
        proc.WaitForExit();
    }

    // "truthy" follows common-sense rules:
    //   false, null, 0, "" → false
    //   everything else    → true
    private static bool Truthy(object? val) => val switch
    {
        bool b   => b,
        double d => d != 0,
        string s => s.Length > 0,
        null     => false,
        _        => true,
    };

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;

        // Compare numbers to numbers (avoid double.Equals quirks via subtraction)
        if (a is double da && b is double db) return Math.Abs(da - db) < 1e-12;

        return a.Equals(b);
    }

    private static double ToNumber(object? val, Expr source)
    {
        if (val is double d) return d;
        if (val is string s && double.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var n))
            return n;
        throw new MakoError($"Expected a number but got '{Stringify(val)}'");
    }

    public static string Stringify(object? val) => val switch
    {
        null     => "none",
        bool b   => b ? "true" : "false",
        double d => d == Math.Floor(d) && !double.IsInfinity(d)
                    ? ((long)d).ToString()  // show 42 not 42.0
                    : d.ToString(System.Globalization.CultureInfo.InvariantCulture),
        string s => s,
        _        => val.ToString() ?? "none",
    };
}
