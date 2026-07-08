namespace Mako;

// ── Root ─────────────────────────────────────────────────────────────────────

record FnDecl(string Name, List<string> Params, List<Statement> Body)
{
    /// File the function was loaded from via 'use', or null for the main script.
    public string? Source { get; set; }
}

/// A resolved package reference from a 'using' declaration.
/// Source is null for native packages or bare names (registry-resolved).
record PackageRef(string Name, string? Source);   // Source = "github:User/Repo" or null

record ProgramNode(
    string? ScriptName,
    string? Namespace,
    List<PackageRef> Packages,  // "using Name" / "using Name from "github:...""
    List<string> Imports,       // "use file.mko" — local relative file imports
    List<(string Name, Expr Value)> Constants, // top-level const declarations
    List<FnDecl> Functions,
    List<Statement> Body
);

// ── Statements ────────────────────────────────────────────────────────────────

abstract record Statement
{
    // Source position of the statement's first token (0 = unknown).
    public int Line { get; set; }
    public int Col  { get; set; }
}

/// print expr;
record PrintStmt(Expr Value) : Statement;

/// printnl expr;   (no trailing newline)
record PrintnlStmt(Expr Value) : Statement;

/// name = expr;  /  name += expr;  etc.
record AssignStmt(string Name, Expr Value) : Statement;

/// name[idx] = expr;
record IndexAssignStmt(string Name, Expr Index, Expr Value) : Statement;

/// if condition { ... } else { ... }
record IfStmt(Expr Condition, List<Statement> Then, List<Statement> Else) : Statement;

/// while condition { ... }
record WhileStmt(Expr Condition, List<Statement> Body) : Statement;

/// for var in iterable { ... }
record ForStmt(string Var, Expr Iterable, List<Statement> Body) : Statement;

/// break;
record BreakStmt() : Statement;

/// continue;
record ContinueStmt() : Statement;

/// return expr?;
record ReturnStmt(Expr? Value) : Statement;

/// run "shell command";
record RunStmt(Expr Command) : Statement;

/// const name = expr;   (immutable binding)
record ConstStmt(string Name, Expr Value) : Statement;

/// A bare expression used as a statement (e.g. a function call).
record ExprStmt(Expr Value) : Statement;

// ── Expressions ───────────────────────────────────────────────────────────────

abstract record Expr
{
    // Source position of the expression's anchor token (0 = unknown):
    // the name for identifiers/calls, the operator for binary/unary expressions.
    public int Line { get; set; }
    public int Col  { get; set; }
}

/// "hello world"
record StringLit(string Value) : Expr;

/// 42  /  3.14
record NumberLit(double Value) : Expr;

/// true  /  false
record BoolLit(bool Value) : Expr;

/// none
record NullLit() : Expr;

/// [1, 2, 3]
record ListLit(List<Expr> Items) : Expr;

/// a variable name
record IdentExpr(string Name) : Expr;

/// target[index]
record IndexExpr(Expr Target, Expr Index) : Expr;

/// left op right  — arithmetic / comparison
record BinaryExpr(Expr Left, string Op, Expr Right) : Expr;

/// left and/or right  — short-circuit logical
record LogicalExpr(Expr Left, string Op, Expr Right) : Expr;

/// !expr  /  -expr  /  not expr
record UnaryExpr(string Op, Expr Operand) : Expr;

/// input "prompt"
record InputExpr(Expr Prompt) : Expr;

/// name(arg, ...)
record CallExpr(string Name, List<Expr> Args) : Expr;

/// Namespace.func(arg, ...)
record NamespacedCallExpr(string Ns, string Func, List<Expr> Args) : Expr;
