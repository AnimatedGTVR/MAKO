namespace Mako;

// ── Root ─────────────────────────────────────────────────────────────────────

record ProgramNode(
    string? ScriptName,
    List<Statement> Body       // statements inside main() { }
);

// ── Statements ────────────────────────────────────────────────────────────────

abstract record Statement;

/// print expression;
record PrintStmt(Expr Value) : Statement;

/// name = expression;
record AssignStmt(string Name, Expr Value) : Statement;

/// if condition { ... } else { ... }
record IfStmt(Expr Condition, List<Statement> Then, List<Statement> Else) : Statement;

/// run "shell command";
record RunStmt(Expr Command) : Statement;

// ── Expressions ───────────────────────────────────────────────────────────────

abstract record Expr;

/// "hello world"
record StringLit(string Value) : Expr;

/// 42  /  3.14
record NumberLit(double Value) : Expr;

/// true  /  false
record BoolLit(bool Value) : Expr;

/// a variable name
record IdentExpr(string Name) : Expr;

/// left op right   (op = "+", "-", "*", "/", "==", "!=", "<", ">", "<=", ">=")
record BinaryExpr(Expr Left, string Op, Expr Right) : Expr;

/// !expr
record UnaryExpr(string Op, Expr Operand) : Expr;

/// input "prompt"   — reads a line from stdin
record InputExpr(Expr Prompt) : Expr;
