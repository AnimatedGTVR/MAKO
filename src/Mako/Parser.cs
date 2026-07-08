namespace Mako;

/// Recursive-descent parser.
/// Grammar (v0.02):
///
///   program       = script_decl? (fn_decl | main_decl)* EOF
///   script_decl   = "script" STRING ";"
///   fn_decl       = "fn" IDENT "(" param_list ")" block
///   main_decl     = "main" "(" ")" block
///   param_list    = (IDENT ("," IDENT)*)?
///   statement     = print_stmt | printnl_stmt | assign_stmt | index_assign_stmt
///                 | if_stmt | while_stmt | for_stmt
///                 | break_stmt | continue_stmt | return_stmt
///                 | run_stmt | expr_stmt
///   block         = "{" statement* "}"
///   expr          = logical
///   logical       = comparison (("and" | "or") comparison)*
///   comparison    = addition (("==" | "!=" | "<" | ">" | "<=" | ">=") addition)*
///   addition      = multiply (("+" | "-") multiply)*
///   multiply      = unary (("*" | "/" | "%") unary)*
///   unary         = ("!" | "-" | "not") unary | postfix
///   postfix       = primary ("[" expr "]")*
///   primary       = STRING | NUMBER | "true" | "false" | "none"
///                 | "[" arg_list "]"
///                 | "input" primary | IDENT call_tail? | "(" expr ")"

class Parser
{
    private readonly List<Token> _tokens;
    private int _pos;

    public Parser(List<Token> tokens) => _tokens = tokens;

    public ProgramNode Parse()
    {
        string? scriptName = null;
        string? ns         = null;
        var packages  = new List<PackageRef>();
        var imports   = new List<string>();
        var constants = new List<(string Name, Expr Value)>();
        var fns       = new List<FnDecl>();
        var body      = new List<Statement>();
        Token? mainTok = null;

        if (Check(TokenType.Script))
        {
            Advance();
            scriptName = Expect(TokenType.String, "expected a quoted script name after 'script'").Value;
            Expect(TokenType.Semicolon, "script name");
        }

        if (Check(TokenType.Namespace))
        {
            Advance();
            ns = Expect(TokenType.Identifier, "expected a namespace name after 'namespace'").Value;
            Expect(TokenType.Semicolon, "namespace name");
        }

        // using Name;
        // using Name from "github:User/Repo";
        while (Check(TokenType.Using))
        {
            Advance();
            var pkgName = Expect(TokenType.Identifier, "expected a package name after 'using'").Value;
            string? source = null;
            if (Check(TokenType.From))
            {
                Advance();
                source = Expect(TokenType.String, "expected a quoted source URL after 'from'").Value;
            }
            packages.Add(new PackageRef(pkgName, source));
            Expect(TokenType.Semicolon, "using declaration");
        }

        while (Check(TokenType.Use))
        {
            Advance();
            imports.Add(Expect(TokenType.String, "expected a quoted file path after 'use'").Value);
            Expect(TokenType.Semicolon, "use path");
        }

        // Top-level const declarations (before fn/main)
        while (Check(TokenType.Const))
        {
            Advance();
            var cname = Expect(TokenType.Identifier, "expected a name after 'const'").Value;
            Expect(TokenType.Assign, $"expected '=' after const name '{cname}'");
            var cval = ParseExpr();
            Expect(TokenType.Semicolon, $"const '{cname}'");
            constants.Add((cname, cval));
        }

        while (!Check(TokenType.Eof))
        {
            if (Check(TokenType.Fn))
                fns.Add(ParseFnDecl());
            else if (Check(TokenType.Main))
            {
                if (mainTok is { } first)
                    throw new MakoError(
                        $"duplicate 'main' block (the first one is on line {first.Line})",
                        Current().Line, Current().Col, 4);
                mainTok = Current();
                Advance();
                var open = Expect(TokenType.LParen, "missing '(' after 'main'");
                ExpectClosing(TokenType.RParen, ")", open);
                body = ParseBlock();
            }
            else break;
        }

        if (!Check(TokenType.Eof))
        {
            var tok = Current();
            string hint = "";
            if (tok.Type == TokenType.Identifier)
            {
                var s = tok.Value is "func" or "function" or "def"
                    ? "fn"
                    : Suggest.Closest(tok.Value, ["fn", "main", "use", "using", "const", "script", "namespace"]);
                if (s != null) hint = $" (did you mean '{s}'?)";
            }
            throw new MakoError(
                $"unexpected {DescribeToken(tok)} at top level — only 'fn' declarations and 'main()' are allowed{hint}",
                tok.Line, tok.Col, Math.Max(1, tok.Value.Length));
        }

        return new ProgramNode(scriptName, ns, packages, imports, constants, fns, body);
    }

    // ── Declarations ──────────────────────────────────────────────────────────

    private FnDecl ParseFnDecl()
    {
        Advance(); // "fn"
        var name = Expect(TokenType.Identifier, "expected a function name after 'fn'").Value;
        var open = Expect(TokenType.LParen, $"missing '(' after function name '{name}'");
        var parms = new List<string>();
        while (!Check(TokenType.RParen) && !Check(TokenType.Eof))
        {
            parms.Add(Expect(TokenType.Identifier, "expected a parameter name").Value);
            if (Check(TokenType.Comma)) { Advance(); continue; }
            if (!Check(TokenType.RParen) && !Check(TokenType.Eof))
            {
                var prev = Previous();
                throw new MakoError(
                    $"missing ',' or ')' between parameters (got {DescribeToken(Current())})",
                    prev.EndLine, prev.EndCol);
            }
        }
        ExpectClosing(TokenType.RParen, ")", open);
        return new FnDecl(name, parms, ParseBlock());
    }

    // ── Statements ────────────────────────────────────────────────────────────

    private List<Statement> ParseBlock()
    {
        var open = Expect(TokenType.LBrace, "block");
        var stmts = new List<Statement>();
        while (!Check(TokenType.RBrace) && !Check(TokenType.Eof))
            stmts.Add(ParseStatement());
        if (!Check(TokenType.RBrace))
        {
            var prev = Previous();
            throw new MakoError(
                $"missing '}}' to close the block opened on line {open.Line} (got {DescribeToken(Current())})",
                prev.EndLine, prev.EndCol);
        }
        Advance(); // }
        return stmts;
    }

    private Statement ParseStatement()
    {
        var tok = Current();
        Statement s = tok.Type switch
        {
            TokenType.Print      => ParsePrint(),
            TokenType.Printnl    => ParsePrintnl(),
            TokenType.Const      => ParseConst(),
            TokenType.If         => ParseIf(),
            TokenType.While      => ParseWhile(),
            TokenType.For        => ParseFor(),
            TokenType.Break      => ParseBreak(),
            TokenType.Continue   => ParseContinue(),
            TokenType.Return     => ParseReturn(),
            TokenType.Run        => ParseRun(),
            TokenType.Try        => ParseTry(),
            TokenType.Identifier => ParseAssignOrCall(),
            TokenType.Else => throw new MakoError(
                "found 'else' without a matching 'if'", tok.Line, tok.Col, 4),
            TokenType.Fn => throw new MakoError(
                "functions must be declared at top level, outside 'main' and other functions — use 'fn(x) => ...' for inline lambdas",
                tok.Line, tok.Col, 2),
            _ => throw new MakoError(
                $"unexpected {DescribeToken(tok)} — not a valid statement start",
                tok.Line, tok.Col, Math.Max(1, tok.Value.Length)),
        };
        if (s.Line == 0) { s.Line = tok.Line; s.Col = tok.Col; }
        return s;
    }

    private PrintStmt ParsePrint()
    {
        Advance();
        var val = ParseExpr();
        Expect(TokenType.Semicolon, "print");
        return new PrintStmt(val);
    }

    private ConstStmt ParseConst()
    {
        Advance(); // "const"
        var name = Expect(TokenType.Identifier, "expected a name after 'const'").Value;
        Expect(TokenType.Assign, $"missing '=' after const '{name}'");
        var val = ParseExpr();
        Expect(TokenType.Semicolon, $"const '{name}'");
        return new ConstStmt(name, val);
    }

    private PrintnlStmt ParsePrintnl()
    {
        Advance();
        var val = ParseExpr();
        Expect(TokenType.Semicolon, "printnl");
        return new PrintnlStmt(val);
    }

    private Statement ParseAssignOrCall()
    {
        var nameTok = Advance(); // identifier
        var name    = nameTok.Value;

        // Namespaced call: Ns.func(args);
        if (Check(TokenType.Dot))
        {
            Advance(); // .
            var fnName = Expect(TokenType.Identifier, $"expected a function name after '{name}.'").Value;
            var call = ParseNamespacedCallTail(nameTok, fnName);
            Expect(TokenType.Semicolon, "function call");
            return new ExprStmt(call);
        }

        // Bare function call: name(args);
        if (Check(TokenType.LParen))
        {
            var call = ParseCallTail(nameTok);
            Expect(TokenType.Semicolon, "function call");
            return new ExprStmt(call);
        }

        // Index assignment: name[idx] = expr;
        if (Check(TokenType.LBracket))
        {
            var openBr = Advance(); // [
            var idx = ParseExpr();
            ExpectClosing(TokenType.RBracket, "]", openBr);
            Expect(TokenType.Assign, "missing '=' after index");
            var rhs = ParseExpr();
            Expect(TokenType.Semicolon, "index assignment");
            return new IndexAssignStmt(name, idx, rhs);
        }

        // Compound or plain assignment
        string? compoundOp = Current().Type switch
        {
            TokenType.Assign  => null,
            TokenType.PlusEq  => "+",
            TokenType.MinusEq => "-",
            TokenType.StarEq  => "*",
            TokenType.SlashEq => "/",
            _ => throw UnknownAfterName(nameTok),
        };
        Advance();
        var val = ParseExpr();
        if (compoundOp != null)
            val = new BinaryExpr(new IdentExpr(name) { Line = nameTok.Line, Col = nameTok.Col },
                                 compoundOp, val)
                  { Line = nameTok.Line, Col = nameTok.Col };
        Expect(TokenType.Semicolon, $"assignment to '{name}'");
        return new AssignStmt(name, val);
    }

    /// Statement began with an identifier but what follows makes no sense.
    /// Most often a typo'd keyword ("whle", "prnt") — try to say so.
    private MakoError UnknownAfterName(Token nameTok)
    {
        var name = nameTok.Value;

        if (name is "let" or "var")
        {
            var varName = Check(TokenType.Identifier) ? Current().Value : "x";
            return new MakoError(
                $"MAKO has no '{name}' keyword — assign directly, e.g. '{varName} = ...;'",
                nameTok.Line, nameTok.Col, name.Length);
        }

        string? hint = name switch
        {
            "func" or "function" or "def" => "fn",
            "elif" or "elsif"             => "else if",
            _ => Suggest.Closest(name, Lexer.Keywords.Keys),
        };
        if (hint != null)
            return new MakoError($"unknown statement '{name}' — did you mean '{hint}'?",
                nameTok.Line, nameTok.Col, name.Length);

        return new MakoError(
            $"expected '=', '(', '[' or an assignment operator after '{name}' (got {DescribeToken(Current())})",
            Current().Line, Current().Col, Math.Max(1, Current().Value.Length));
    }

    private IfStmt ParseIf()
    {
        var ifTok = Current();
        Advance(); // "if"
        var cond = ParseExpr();
        var then = ParseBlock();
        var els  = new List<Statement>();
        if (Check(TokenType.Else))
        {
            Advance();
            els = Check(TokenType.If)
                ? new List<Statement> { ParseIf() }
                : ParseBlock();
        }
        return new IfStmt(cond, then, els) { Line = ifTok.Line, Col = ifTok.Col };
    }

    private WhileStmt ParseWhile()
    {
        Advance(); // "while"
        var cond = ParseExpr();
        return new WhileStmt(cond, ParseBlock());
    }

    private ForStmt ParseFor()
    {
        Advance(); // "for"
        var varName = Expect(TokenType.Identifier, "expected a loop variable name after 'for'").Value;
        Expect(TokenType.In, $"expected 'in' after 'for {varName}'");
        var iterable = ParseExpr();
        return new ForStmt(varName, iterable, ParseBlock());
    }

    private BreakStmt ParseBreak()
    {
        Advance();
        Expect(TokenType.Semicolon, "break");
        return new BreakStmt();
    }

    private ContinueStmt ParseContinue()
    {
        Advance();
        Expect(TokenType.Semicolon, "continue");
        return new ContinueStmt();
    }

    private ReturnStmt ParseReturn()
    {
        Advance(); // "return"
        Expr? val = null;
        if (!Check(TokenType.Semicolon))
            val = ParseExpr();
        Expect(TokenType.Semicolon, "return");
        return new ReturnStmt(val);
    }

    private RunStmt ParseRun()
    {
        Advance();
        var cmd = ParseExpr();
        Expect(TokenType.Semicolon, "run");
        return new RunStmt(cmd);
    }

    private TryStmt ParseTry()
    {
        Advance(); // try
        var tryBody = ParseBlock();

        string? catchVar = null;
        var catchBody = new List<Statement>();

        if (Check(TokenType.Catch))
        {
            Advance(); // catch
            if (Check(TokenType.Identifier))
                catchVar = Advance().Value;
            catchBody = ParseBlock();
        }

        return new TryStmt(tryBody, catchVar, catchBody);
    }

    // ── Expressions ───────────────────────────────────────────────────────────

    private Expr ParseExpr() => ParseLogical();

    private Expr ParseLogical()
    {
        var left = ParseComparison();
        while (Current().Type is TokenType.And or TokenType.Or)
        {
            var op = Advance();
            left = new LogicalExpr(left, op.Value, ParseComparison())
                   { Line = op.Line, Col = op.Col };
        }
        return left;
    }

    private Expr ParseComparison()
    {
        var left = ParseAddition();
        while (Current().Type is TokenType.EqEq or TokenType.NotEq
               or TokenType.Lt or TokenType.Gt or TokenType.LtEq or TokenType.GtEq)
        {
            var op = Advance();
            left = new BinaryExpr(left, op.Value, ParseAddition())
                   { Line = op.Line, Col = op.Col };
        }
        return left;
    }

    private Expr ParseAddition()
    {
        var left = ParseMultiply();
        while (Current().Type is TokenType.Plus or TokenType.Minus)
        {
            var op = Advance();
            left = new BinaryExpr(left, op.Value, ParseMultiply())
                   { Line = op.Line, Col = op.Col };
        }
        return left;
    }

    private Expr ParseMultiply()
    {
        var left = ParseUnary();
        while (Current().Type is TokenType.Star or TokenType.Slash or TokenType.Percent)
        {
            var op = Advance();
            left = new BinaryExpr(left, op.Value, ParseUnary())
                   { Line = op.Line, Col = op.Col };
        }
        return left;
    }

    private Expr ParseUnary()
    {
        if (Check(TokenType.Bang) || Check(TokenType.Not))
        {
            var op = Advance();
            return new UnaryExpr("!", ParseUnary()) { Line = op.Line, Col = op.Col };
        }
        if (Check(TokenType.Minus))
        {
            var op = Advance();
            return new UnaryExpr("-", ParseUnary()) { Line = op.Line, Col = op.Col };
        }
        return ParsePostfix();
    }

    // Handles chained indexing: expr[i][j]
    private Expr ParsePostfix()
    {
        var expr = ParsePrimary();
        while (Check(TokenType.LBracket))
        {
            var openBr = Advance(); // [
            var idx = ParseExpr();
            ExpectClosing(TokenType.RBracket, "]", openBr);
            expr = new IndexExpr(expr, idx) { Line = openBr.Line, Col = openBr.Col };
        }
        return expr;
    }

    private Expr ParsePrimary()
    {
        var tok = Current();

        if (Check(TokenType.String))         { Advance(); return new StringLit(tok.Value); }
        if (Check(TokenType.TemplateString)) { Advance(); return ParseTemplateString(tok.Value, tok.Line, tok.Col); }
        if (Check(TokenType.Number))   { Advance(); return new NumberLit(double.Parse(tok.Value, System.Globalization.CultureInfo.InvariantCulture)); }
        if (Check(TokenType.True))     { Advance(); return new BoolLit(true); }
        if (Check(TokenType.False))    { Advance(); return new BoolLit(false); }
        if (Check(TokenType.None))     { Advance(); return new NullLit(); }

        // List literal: [expr, expr, ...]
        if (Check(TokenType.LBracket))
        {
            var openBr = Advance(); // [
            var items = new List<Expr>();
            while (!Check(TokenType.RBracket) && !Check(TokenType.Eof))
            {
                items.Add(ParseExpr());
                if (Check(TokenType.Comma)) { Advance(); continue; }
                if (!Check(TokenType.RBracket) && !Check(TokenType.Eof))
                {
                    var prev = Previous();
                    throw new MakoError(
                        $"missing ',' or ']' between list items (got {DescribeToken(Current())})",
                        prev.EndLine, prev.EndCol);
                }
            }
            ExpectClosing(TokenType.RBracket, "]", openBr);
            return new ListLit(items);
        }

        // Dict literal: {"key": value, ...}
        if (Check(TokenType.LBrace))
        {
            var openBr = Advance(); // {
            var entries = new List<(Expr Key, Expr Value)>();
            while (!Check(TokenType.RBrace) && !Check(TokenType.Eof))
            {
                var key = ParseExpr();
                Expect(TokenType.Colon, "expected ':' after dict key");
                var val = ParseExpr();
                entries.Add((key, val));
                if (Check(TokenType.Comma)) { Advance(); continue; }
                if (!Check(TokenType.RBrace) && !Check(TokenType.Eof))
                {
                    var prev = Previous();
                    throw new MakoError(
                        $"missing ',' or '}}' between dict entries (got {DescribeToken(Current())})",
                        prev.EndLine, prev.EndCol);
                }
            }
            ExpectClosing(TokenType.RBrace, "}", openBr);
            return new DictLit(entries) { Line = openBr.Line, Col = openBr.Col };
        }

        // Lambda: fn(x) => expr   OR   fn(x, y) { ... }
        if (Check(TokenType.Fn))
        {
            var fnTok = Advance(); // fn
            var open  = Expect(TokenType.LParen, "expected '(' after 'fn' in lambda");
            var parms = new List<string>();
            while (!Check(TokenType.RParen) && !Check(TokenType.Eof))
            {
                parms.Add(Expect(TokenType.Identifier, "expected a parameter name").Value);
                if (Check(TokenType.Comma)) Advance();
            }
            ExpectClosing(TokenType.RParen, ")", open);

            List<Statement> body;
            if (Check(TokenType.Arrow))
            {
                Advance(); // =>
                var expr = ParseExpr();
                body = [new ReturnStmt(expr) { Line = expr.Line, Col = expr.Col }];
            }
            else
            {
                body = ParseBlock();
            }
            return new LambdaExpr(parms, body) { Line = fnTok.Line, Col = fnTok.Col };
        }

        if (Check(TokenType.Input))
        {
            Advance();
            if (Check(TokenType.Semicolon))
                throw new MakoError("'input' needs a prompt, e.g. input \"name: \"",
                    tok.Line, tok.Col, 5);
            return new InputExpr(ParsePrimary()) { Line = tok.Line, Col = tok.Col };
        }

        if (Check(TokenType.Identifier))
        {
            Advance();
            // Namespace.func(args)
            if (Check(TokenType.Dot))
            {
                Advance(); // .
                var fnName = Expect(TokenType.Identifier, $"expected a function name after '{tok.Value}.'").Value;
                return ParseNamespacedCallTail(tok, fnName);
            }
            if (Check(TokenType.LParen))
                return ParseCallTail(tok);
            return new IdentExpr(tok.Value) { Line = tok.Line, Col = tok.Col };
        }

        if (Check(TokenType.LParen))
        {
            var open = Advance();
            var inner = ParseExpr();
            ExpectClosing(TokenType.RParen, ")", open);
            return inner;
        }

        var eqHint = tok.Type == TokenType.Assign ? " — did you mean '=='?" : "";
        throw new MakoError($"unexpected {DescribeToken(tok)} in expression{eqHint}",
            tok.Line, tok.Col, Math.Max(1, tok.Value.Length));
    }

    private CallExpr ParseCallTail(Token nameTok)
    {
        var open = Advance(); // (
        var args = ParseArgList(open);
        return new CallExpr(nameTok.Value, args) { Line = nameTok.Line, Col = nameTok.Col };
    }

    private NamespacedCallExpr ParseNamespacedCallTail(Token nsTok, string fn)
    {
        var open = Expect(TokenType.LParen, $"missing '(' after '{nsTok.Value}.{fn}'");
        var args = ParseArgList(open);
        return new NamespacedCallExpr(nsTok.Value, fn, args) { Line = nsTok.Line, Col = nsTok.Col };
    }

    private List<Expr> ParseArgList(Token open)
    {
        var args = new List<Expr>();
        while (!Check(TokenType.RParen) && !Check(TokenType.Eof))
        {
            args.Add(ParseExpr());
            if (Check(TokenType.Comma)) { Advance(); continue; }
            if (!Check(TokenType.RParen) && !Check(TokenType.Eof))
            {
                var prev = Previous();
                throw new MakoError(
                    $"missing ',' or ')' between arguments (got {DescribeToken(Current())})",
                    prev.EndLine, prev.EndCol);
            }
        }
        ExpectClosing(TokenType.RParen, ")", open);
        return args;
    }

    // ── Template string interpolation ─────────────────────────────────────────

    // Public entry point used by sub-parsers for {expr} content.
    public Expr ParseExprPublic()
    {
        var e = ParseExpr();
        Expect(TokenType.Eof, "unexpected content after expression in string interpolation");
        return e;
    }

    // Converts "Hello, {name}! You are {age} years old." into a chain of + expressions.
    // `raw` is the string's source text verbatim (between the quotes), so escape
    // sequences and {{ }} are handled here and positions map exactly to the file.
    private Expr ParseTemplateString(string raw, int line, int col)
    {
        var parts = new List<Expr>();
        int i = 0;
        var sb = new System.Text.StringBuilder();

        while (i < raw.Length)
        {
            char c = raw[i];

            // {{ / }} → literal brace (escape)
            if (c is '{' or '}' && i + 1 < raw.Length && raw[i + 1] == c)
            {
                sb.Append(c); i += 2; continue;
            }
            // \n, \t, ...
            if (c == '\\' && i + 1 < raw.Length)
            {
                sb.Append(Lexer.Unescape(raw[i + 1])); i += 2; continue;
            }

            if (c == '{')
            {
                // Flush accumulated plain text
                if (sb.Length > 0) { parts.Add(new StringLit(sb.ToString())); sb.Clear(); }

                // Find the matching closing brace (handle nesting)
                int depth = 1, start = i + 1;
                i++;
                while (i < raw.Length && depth > 0)
                {
                    if (raw[i] == '{') depth++;
                    else if (raw[i] == '}') depth--;
                    if (depth > 0) i++;
                }

                // Position of the expression inside the original file, so errors
                // in "{expr}" point at the real source, not the substring.
                int nlBefore = 0, lastNl = -1;
                for (int k = 0; k < start; k++)
                    if (raw[k] == '\n') { nlBefore++; lastNl = k; }
                int exprLine = line + nlBefore;
                int exprCol  = nlBefore == 0 ? col + 1 + start : start - lastNl;

                if (depth != 0)
                    throw new MakoError("missing '}' to close interpolation in string",
                        exprLine, Math.Max(1, exprCol - 1));

                var exprSrc = raw[start..i];
                var subTokens = new Lexer(exprSrc, exprLine, exprCol).Tokenize();
                var subExpr   = new Parser(subTokens).ParseExprPublic();
                // Wrap in to_str() so any type prints cleanly
                parts.Add(new CallExpr("to_str", [subExpr]) { Line = exprLine, Col = exprCol });
                i++; // skip closing }
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        if (sb.Length > 0) parts.Add(new StringLit(sb.ToString()));
        if (parts.Count == 0) return new StringLit("");
        if (parts.Count == 1) return parts[0];

        Expr result = parts[0];
        for (int j = 1; j < parts.Count; j++)
            result = new BinaryExpr(result, "+", parts[j]) { Line = line, Col = col };
        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool Check(TokenType type) => Current().Type == type;
    private Token Current() => _tokens[_pos];

    private Token Advance()
    {
        var t = _tokens[_pos];
        if (t.Type != TokenType.Eof) _pos++;
        return t;
    }

    /// For ';' and '{' the caret goes where the symbol belongs — just past the
    /// previous token — and `message` names the construct being parsed.
    /// For everything else the caret goes under the surprising token and
    /// `message` is the full "expected ..." text.
    private Token Expect(TokenType type, string message)
    {
        if (Check(type)) return Advance();

        var got    = DescribeToken(Current());
        var eqHint = Current().Type == TokenType.Assign ? " — did you mean '=='?" : "";
        var prev   = Previous();

        throw type switch
        {
            TokenType.Semicolon => new MakoError(
                $"missing ';' (got {got}){eqHint}", prev.EndLine, prev.EndCol),
            TokenType.LBrace => new MakoError(
                $"missing '{{' (got {got}){eqHint}", prev.EndLine, prev.EndCol),
            _ => new MakoError(
                $"{message} (got {got})",
                Current().Line, Current().Col, Math.Max(1, Current().Value.Length)),
        };
    }

    /// Closing delimiter with a pointer back to the opener when they're on
    /// different lines.
    private Token ExpectClosing(TokenType type, string symbol, Token open)
    {
        if (Check(type)) return Advance();
        var prev = Previous();
        var note = Current().Line != open.Line ? $" to match '{open.Value}' on line {open.Line}" : "";
        throw new MakoError(
            $"missing '{symbol}'{note} (got {DescribeToken(Current())})",
            prev.EndLine, prev.EndCol);
    }

    private static string DescribeToken(Token tok) => tok.Type switch
    {
        TokenType.Print or TokenType.Printnl or TokenType.If   or TokenType.While or
        TokenType.For   or TokenType.Fn      or TokenType.Return or TokenType.Break or
        TokenType.Continue or TokenType.Run  or TokenType.Const or
        TokenType.Try      or TokenType.Identifier
            => $"start of '{tok.Value}' statement",
        TokenType.RBrace => "end of block '}'",
        TokenType.Eof    => "end of file",
        TokenType.String or TokenType.TemplateString
            => $"string \"{(tok.Value.Length > 20 ? tok.Value[..17] + "..." : tok.Value)}\"",
        _                => $"'{tok.Value}'",
    };

    private Token Previous() => _pos > 0 ? _tokens[_pos - 1] : _tokens[0];
}
