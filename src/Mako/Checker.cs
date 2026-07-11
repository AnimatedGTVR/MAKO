namespace Mako;

/// One issue found by `mko check` — always a warning, never a hard error.
/// MAKO stays dynamically typed; nothing here blocks a script from running.
record CheckIssue(int Line, string Message);

/// `mko check file.mko` — a lint pass over the parsed AST. Three
/// low-false-positive rules, chosen deliberately narrow for a first
/// version: type-hint mismatches, unused local variables, and unreachable
/// code after return/break/continue. Undefined-variable-use was left out
/// on purpose — it needs real scope/closure tracking to avoid false
/// positives, and a lint tool that cries wolf gets ignored.
static class Checker
{
    // Literal-value AST nodes whose runtime type is knowable without
    // evaluating anything — enough to catch an obviously wrong hint like
    // `age: number = "thirty";` without attempting real type inference.
    private static string? LiteralTypeName(Expr expr) => expr switch
    {
        StringLit or TemplateStringExpr => "string",
        NumberLit => "number",
        BoolLit => "bool",
        NullLit => "none",
        ListLit => "list",
        DictLit => "dict",
        _ => null, // anything else (calls, identifiers, arithmetic, ...) is not checked
    };

    // Hint spellings accepted as aliases for the same runtime type, since
    // MAKO doesn't reserve "number"/"string"/etc. as keywords — a script
    // author could write "int" or "str" and mean the same thing. Kept
    // small and explicit rather than guessing at every possible spelling.
    private static readonly Dictionary<string, string> TypeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["int"] = "number", ["float"] = "number", ["double"] = "number", ["number"] = "number",
        ["str"] = "string", ["string"] = "string",
        ["bool"] = "bool", ["boolean"] = "bool",
        ["list"] = "list", ["array"] = "list",
        ["dict"] = "dict", ["map"] = "dict", ["object"] = "dict",
        ["none"] = "none", ["null"] = "none",
    };

    public static List<CheckIssue> Check(ProgramNode program)
    {
        var issues = new List<CheckIssue>();
        CheckFunctionBody(program.Body, issues);
        foreach (var fn in program.Functions)
            CheckFunctionBody(fn.Body, issues);
        return issues;
    }

    private static void CheckFunctionBody(List<Statement> body, List<CheckIssue> issues)
    {
        CheckBlock(body, issues);
        CheckUnusedLocals(body, issues);
    }

    // ── Type-hint mismatches + unreachable code ─────────────────────────────

    private static void CheckBlock(List<Statement> block, List<CheckIssue> issues)
    {
        bool sawTerminator = false;
        foreach (var stmt in block)
        {
            if (sawTerminator)
            {
                issues.Add(new CheckIssue(stmt.Line,
                    $"unreachable code — this can never run, it follows a return/break/continue in the same block"));
                // Only report the first unreachable statement per block —
                // once one is flagged, everything after it is redundantly
                // unreachable too and would just be noise.
                break;
            }

            switch (stmt)
            {
                case AssignStmt { TypeHint: not null } a:
                    CheckHint(a.Name, a.TypeHint, a.Value, a.Line, issues);
                    break;
                case ReturnStmt or BreakStmt or ContinueStmt:
                    sawTerminator = true;
                    break;
                case IfStmt ifs:
                    CheckBlock(ifs.Then, issues);
                    if (ifs.Else.Count > 0) CheckBlock(ifs.Else, issues);
                    break;
                case WhileStmt ws:
                    CheckBlock(ws.Body, issues);
                    break;
                case ForStmt fs:
                    CheckBlock(fs.Body, issues);
                    break;
                case TryStmt ts:
                    CheckBlock(ts.Try, issues);
                    if (ts.HasCatch) CheckBlock(ts.Catch, issues);
                    break;
            }
        }
    }

    private static void CheckHint(string name, string hint, Expr value, int line, List<CheckIssue> issues)
    {
        string? actual = LiteralTypeName(value);
        if (actual == null) return; // not a literal — can't check without real inference

        string expected = TypeAliases.GetValueOrDefault(hint, hint.ToLowerInvariant());
        if (expected != actual)
            issues.Add(new CheckIssue(line,
                $"'{name}: {hint}' but the value assigned is a {actual}"));
    }

    // ── Unused local variables ───────────────────────────────────────────────

    private static void CheckUnusedLocals(List<Statement> body, List<CheckIssue> issues)
    {
        var assigned = new Dictionary<string, int>(); // name -> first assignment line
        var used = new HashSet<string>();
        CollectAssignments(body, assigned);
        CollectUses(body, used);
        foreach (var (name, line) in assigned)
            if (!used.Contains(name) && !name.StartsWith('_')) // leading underscore = "intentionally unused"
                issues.Add(new CheckIssue(line, $"'{name}' is assigned but never used"));
    }

    private static void CollectAssignments(List<Statement> block, Dictionary<string, int> assigned)
    {
        foreach (var stmt in block)
        {
            switch (stmt)
            {
                case AssignStmt a when !assigned.ContainsKey(a.Name):
                    assigned[a.Name] = a.Line;
                    break;
                case ForStmt fs when !assigned.ContainsKey(fs.Var):
                    assigned[fs.Var] = fs.Line;
                    CollectAssignments(fs.Body, assigned);
                    break;
                case ForStmt fs:
                    CollectAssignments(fs.Body, assigned);
                    break;
                case IfStmt ifs:
                    CollectAssignments(ifs.Then, assigned);
                    CollectAssignments(ifs.Else, assigned);
                    break;
                case WhileStmt ws:
                    CollectAssignments(ws.Body, assigned);
                    break;
                case TryStmt ts:
                    CollectAssignments(ts.Try, assigned);
                    if (ts.HasCatch) CollectAssignments(ts.Catch, assigned);
                    break;
            }
        }
    }

    // Walks every expression reachable from the block and records every
    // identifier read — deliberately over-approximates (e.g. also counts
    // the left side of "x = x + 1;" as a use, which is correct: reading x
    // to compute its own new value is a real use, not dead assignment).
    private static void CollectUses(List<Statement> block, HashSet<string> used)
    {
        foreach (var stmt in block)
        {
            switch (stmt)
            {
                case PrintStmt p: UseExpr(p.Value, used); break;
                case PrintnlStmt p: UseExpr(p.Value, used); break;
                case AssignStmt a: UseExpr(a.Value, used); break;
                case IndexAssignStmt ia:
                    used.Add(ia.Name);
                    foreach (var idx in ia.Indices) UseExpr(idx, used);
                    UseExpr(ia.Value, used);
                    break;
                case FieldAssignStmt fa: UseExpr(fa.Target, used); UseExpr(fa.Value, used); break;
                case IfStmt ifs:
                    UseExpr(ifs.Condition, used);
                    CollectUses(ifs.Then, used);
                    CollectUses(ifs.Else, used);
                    break;
                case WhileStmt ws: UseExpr(ws.Condition, used); CollectUses(ws.Body, used); break;
                case ForStmt fs: UseExpr(fs.Iterable, used); CollectUses(fs.Body, used); break;
                case ReturnStmt { Value: not null } r: UseExpr(r.Value, used); break;
                case RunStmt rs: UseExpr(rs.Command, used); break;
                case ConstStmt cs: UseExpr(cs.Value, used); break;
                case TryStmt ts:
                    CollectUses(ts.Try, used);
                    if (ts.HasCatch) CollectUses(ts.Catch, used);
                    break;
                case ThrowStmt th: UseExpr(th.Message, used); break;
                case ExprStmt es: UseExpr(es.Value, used); break;
            }
        }
    }

    private static void UseExpr(Expr expr, HashSet<string> used)
    {
        switch (expr)
        {
            case IdentExpr id: used.Add(id.Name); break;
            case TemplateStringExpr t: UseExpr(t.Expanded, used); break;
            case ListLit l: foreach (var item in l.Items) UseExpr(item, used); break;
            case DictLit d: foreach (var (k, v) in d.Entries) { UseExpr(k, used); UseExpr(v, used); } break;
            case IndexExpr ix: UseExpr(ix.Target, used); UseExpr(ix.Index, used); break;
            case FieldExpr f: UseExpr(f.Target, used); break;
            case MethodCallExpr m: UseExpr(m.Target, used); foreach (var a in m.Args) UseExpr(a, used); break;
            case StructLitExpr sl: foreach (var (_, v) in sl.Fields) UseExpr(v, used); break;
            case BinaryExpr b: UseExpr(b.Left, used); UseExpr(b.Right, used); break;
            case LogicalExpr lo: UseExpr(lo.Left, used); UseExpr(lo.Right, used); break;
            case UnaryExpr u: UseExpr(u.Operand, used); break;
            case InputExpr inp: UseExpr(inp.Prompt, used); break;
            case CallExpr c: foreach (var a in c.Args) UseExpr(a, used); break;
            case NamespacedCallExpr nc: foreach (var a in nc.Args) UseExpr(a, used); break;
            case LambdaExpr lam: CollectUses(lam.Body, used); break;
        }
    }
}
