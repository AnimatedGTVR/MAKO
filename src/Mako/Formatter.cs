using System.Text;

namespace Mako;

/// AST-based formatter for MAKO source code.
/// Preserves template strings verbatim and re-inserts # comments by line number.
static class Formatter
{
    private const string Indent = "    "; // 4 spaces

    public static string Format(string source)
    {
        var tokens  = new Lexer(source).Tokenize();
        var program = new Parser(tokens).Parse();

        // Identify which comment lines are standalone (no code on the same line).
        // Inline comments (# after code) are dropped — they can't be placed reliably.
        var codeLines = new HashSet<int>(
            tokens.Where(t => t.Type != TokenType.Comment && t.Type != TokenType.Eof)
                  .Select(t => t.Line));

        var comments = new SortedDictionary<int, string>();
        foreach (var t in tokens.Where(t => t.Type == TokenType.Comment))
            if (!codeLines.Contains(t.Line))
                comments[t.Line] = t.Value;

        // The first code line tells us where the file header starts.
        // Comments before it are file-preamble and should appear above everything.
        int firstCodeLine = codeLines.Count > 0 ? codeLines.Min() : int.MaxValue;

        var p = new PrintContext(comments, firstCodeLine);
        p.PrintProgram(program);
        return p.Result();
    }

    // ── Print context ─────────────────────────────────────────────────────────

    private sealed class PrintContext(SortedDictionary<int, string> comments, int firstCodeLine)
    {
        private readonly StringBuilder _sb = new();
        private int _depth = 0;

        // Track which source lines we've already emitted comments for.
        private int _lastCommentLine = 0;

        public string Result()
        {
            var s = _sb.ToString().TrimEnd();
            return s.Length == 0 ? "" : s + "\n";
        }

        private string Pad => string.Concat(Enumerable.Repeat(Indent, _depth));

        private void Line(string text) => _sb.AppendLine(Pad + text);
        private void Blank() => _sb.AppendLine();

        // Emit any unprinted standalone comments up to (but not including) sourceLine.
        private void FlushCommentsBefore(int sourceLine)
        {
            foreach (var kv in comments.Where(kv => kv.Key < sourceLine && kv.Key > _lastCommentLine))
            {
                Line($"# {kv.Value.TrimStart()}");
                _lastCommentLine = kv.Key;
            }
        }

        // Emit all remaining unprinted comments.
        private void FlushAllComments()
        {
            foreach (var kv in comments.Where(kv => kv.Key > _lastCommentLine))
            {
                Line($"# {kv.Value.TrimStart()}");
                _lastCommentLine = kv.Key;
            }
        }

        // ── Program ───────────────────────────────────────────────────────────

        public void PrintProgram(ProgramNode p)
        {
            // Flush file-preamble comments (those that appear before the first code line).
            FlushCommentsBefore(firstCodeLine);

            if (p.ScriptName != null) Line($"script \"{Escape(p.ScriptName)}\";");
            if (p.Namespace  != null) Line($"namespace {p.Namespace};");
            if (p.ScriptName != null || p.Namespace != null) Blank();

            foreach (var pkg in p.Packages)
                Line(pkg.Source != null
                    ? $"using {pkg.Name} from \"{Escape(pkg.Source)}\";"
                    : $"using {pkg.Name};");

            foreach (var imp in p.Imports)
                Line($"use \"{Escape(imp)}\";");

            if (p.Packages.Count > 0 || p.Imports.Count > 0) Blank();

            foreach (var (cname, cexpr) in p.Constants)
                Line($"const {cname} = {Expr(cexpr)};");
            if (p.Constants.Count > 0) Blank();

            for (int i = 0; i < p.Functions.Count; i++)
            {
                // Emit standalone comments that precede this fn declaration.
                if (p.Functions[i].Line > 0)
                    FlushCommentsBefore(p.Functions[i].Line);
                PrintFn(p.Functions[i]);
                _sb.AppendLine(); // close the fn's `}` with a newline
                Blank();          // blank separator after fn
            }

            if (p.Body.Count > 0)
            {
                if (p.MainLine > 0)
                    FlushCommentsBefore(p.MainLine);
                _sb.Append("main() ");
                PrintBlock(p.Body);
                _sb.AppendLine();
            }

            // Emit any trailing comments at end of file.
            FlushAllComments();
        }

        private void PrintFn(FnDecl fn)
        {
            var paramList = string.Join(", ", fn.Params);
            _sb.Append($"{Pad}fn {fn.Name}({paramList}) ");
            PrintBlock(fn.Body);
        }

        // ── Blocks & statements ───────────────────────────────────────────────

        private void PrintBlock(List<Statement> stmts)
        {
            _sb.AppendLine("{");
            _depth++;
            PrintStmts(stmts);
            _depth--;
            _sb.Append(Pad + "}");
        }

        private void PrintStmts(List<Statement> stmts)
        {
            foreach (var stmt in stmts)
            {
                if (stmt.Line > 0)
                    FlushCommentsBefore(stmt.Line);
                PrintStmt(stmt);
            }
        }

        private void PrintStmt(Statement stmt)
        {
            switch (stmt)
            {
                case PrintStmt p:
                    Line($"print {Expr(p.Value)};");
                    break;

                case PrintnlStmt p:
                    Line($"printnl {Expr(p.Value)};");
                    break;

                case ConstStmt c:
                    Line($"const {c.Name} = {Expr(c.Value)};");
                    break;

                case AssignStmt a:
                    Line($"{a.Name} = {Expr(a.Value)};");
                    break;

                case IndexAssignStmt ia:
                    Line($"{ia.Name}{string.Concat(ia.Indices.Select(idx => $"[{Expr(idx)}]"))} = {Expr(ia.Value)};");
                    break;

                case IfStmt i:
                    _sb.Append($"{Pad}if {Expr(i.Condition)} ");
                    PrintBlock(i.Then);
                    if (i.Else.Count > 0)
                    {
                        if (i.Else.Count == 1 && i.Else[0] is IfStmt elseIf)
                        {
                            _sb.Append(" else ");
                            _sb.Append($"if {Expr(elseIf.Condition)} ");
                            PrintBlock(elseIf.Then);
                            if (elseIf.Else.Count > 0)
                            {
                                _sb.Append(" else ");
                                PrintBlock(elseIf.Else);
                            }
                        }
                        else
                        {
                            _sb.Append(" else ");
                            PrintBlock(i.Else);
                        }
                    }
                    _sb.AppendLine();
                    break;

                case WhileStmt w:
                    _sb.Append($"{Pad}while {Expr(w.Condition)} ");
                    PrintBlock(w.Body);
                    _sb.AppendLine();
                    break;

                case ForStmt f:
                    _sb.Append($"{Pad}for {f.Var} in {Expr(f.Iterable)} ");
                    PrintBlock(f.Body);
                    _sb.AppendLine();
                    break;

                case TryStmt t:
                    _sb.Append($"{Pad}try ");
                    PrintBlock(t.Try);
                    if (t.HasCatch)
                    {
                        var catchVar = t.CatchVar != null ? $" {t.CatchVar}" : "";
                        _sb.Append($" catch{catchVar} ");
                        PrintBlock(t.Catch);
                    }
                    _sb.AppendLine();
                    break;

                case BreakStmt:
                    Line("break;");
                    break;

                case ContinueStmt:
                    Line("continue;");
                    break;

                case ReturnStmt r:
                    Line(r.Value != null ? $"return {Expr(r.Value)};" : "return;");
                    break;

                case RunStmt r:
                    Line($"run {Expr(r.Command)};");
                    break;

                case ExprStmt e:
                    Line($"{Expr(e.Value)};");
                    break;

                default:
                    Line($"/* unknown stmt: {stmt.GetType().Name} */");
                    break;
            }
        }

        // ── Expressions ───────────────────────────────────────────────────────

        private string Expr(Expr e) => e switch
        {
            StringLit s             => $"\"{Escape(s.Value)}\"",
            TemplateStringExpr t    => $"\"{t.Raw}\"",
            NumberLit n             => FormatNumber(n.Value),
            BoolLit b               => b.Value ? "true" : "false",
            NullLit                 => "none",
            IdentExpr i             => i.Name,
            ListLit l               => $"[{string.Join(", ", l.Items.Select(Expr))}]",
            DictLit d               => DictExpr(d),
            LambdaExpr lam          => LambdaExpr(lam),
            IndexExpr ix            => $"{Expr(ix.Target)}[{Expr(ix.Index)}]",
            UnaryExpr u             => UnaryExpr(u),
            BinaryExpr b            => BinaryExpr(b),
            LogicalExpr l           => $"{Expr(l.Left)} {l.Op} {Expr(l.Right)}",
            InputExpr inp           => $"input {Expr(inp.Prompt)}",
            CallExpr c              => $"{c.Name}({string.Join(", ", c.Args.Select(Expr))})",
            NamespacedCallExpr n    => $"{n.Ns}.{n.Func}({string.Join(", ", n.Args.Select(Expr))})",
            _                       => $"/* ? {e.GetType().Name} */",
        };

        private string DictExpr(DictLit d)
        {
            if (d.Entries.Count == 0) return "{}";
            var entries = d.Entries.Select(kv => $"{Expr(kv.Key)}: {Expr(kv.Value)}");
            var inline  = "{" + string.Join(", ", entries) + "}";
            // Keep inline if short enough, otherwise expand.
            if (inline.Length <= 60) return inline;
            var sep = ",\n" + Pad + Indent + Indent;
            return "{\n" + Pad + Indent + Indent +
                   string.Join(sep, entries) +
                   "\n" + Pad + Indent + "}";
        }

        private string LambdaExpr(LambdaExpr lam)
        {
            var parms = string.Join(", ", lam.Params);
            // Arrow form: single return statement with no sub-blocks.
            if (lam.Body is [ReturnStmt { Value: { } retVal }])
                return $"fn({parms}) => {Expr(retVal)}";
            // Block form.
            var sb2 = new StringBuilder();
            sb2.Append($"fn({parms}) {{");
            _depth++;
            foreach (var s in lam.Body)
            {
                sb2.AppendLine();
                sb2.Append(Pad);
                sb2.Append(InlineStmt(s));
            }
            _depth--;
            sb2.AppendLine();
            sb2.Append(Pad + "}");
            return sb2.ToString();
        }

        // Statement rendered inline for lambda block bodies.
        private string InlineStmt(Statement s) => s switch
        {
            ReturnStmt r   => r.Value != null ? $"return {Expr(r.Value)};" : "return;",
            PrintStmt p    => $"print {Expr(p.Value)};",
            AssignStmt a   => $"{a.Name} = {Expr(a.Value)};",
            ExprStmt e     => $"{Expr(e.Value)};",
            BreakStmt      => "break;",
            ContinueStmt   => "continue;",
            _              => "/* ... */",
        };

        private string UnaryExpr(UnaryExpr u)
        {
            var operand = Expr(u.Operand);
            return u.Op == "not" ? $"not {operand}" : $"{u.Op}{operand}";
        }

        private string BinaryExpr(BinaryExpr b)
        {
            // Detect to_str() wrappers from template string expansion — skip wrapping.
            var left  = Expr(b.Left);
            var right = Expr(b.Right);
            return $"{left} {b.Op} {right}";
        }

        private static string FormatNumber(double d)
        {
            if (d == Math.Floor(d) && !double.IsInfinity(d) && Math.Abs(d) < 1e15)
                return ((long)d).ToString();
            return d.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string Escape(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
