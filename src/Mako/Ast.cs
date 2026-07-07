namespace Mako;

// ── Root ─────────────────────────────────────────────────────────────────────

record FnDecl(string Name, List<string> Params, List<Statement> Body);

record ProgramNode(
    string? ScriptName,
    string? Namespace,
    List<string> Imports,      // "use" file paths
    List<FnDecl> Functions,
    List<Statement> Body
);

// ── Statements ────────────────────────────────────────────────────────────────

abstract record Statement;

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

abstract record Expr;

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
