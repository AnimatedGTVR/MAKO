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
        var imports = new List<string>();
        var fns     = new List<FnDecl>();
        var body    = new List<Statement>();

        if (Check(TokenType.Script))
        {
            Advance();
            scriptName = Expect(TokenType.String, "Expected script name string").Value;
            Expect(TokenType.Semicolon, "Expected ';' after script name");
        }

        if (Check(TokenType.Namespace))
        {
            Advance();
            ns = Expect(TokenType.Identifier, "Expected namespace name").Value;
            Expect(TokenType.Semicolon, "Expected ';' after namespace name");
        }

        while (Check(TokenType.Use))
        {
            Advance();
            imports.Add(Expect(TokenType.String, "Expected file path string after 'use'").Value);
            Expect(TokenType.Semicolon, "Expected ';' after use path");
        }

        while (!Check(TokenType.Eof))
        {
            if (Check(TokenType.Fn))
                fns.Add(ParseFnDecl());
            else if (Check(TokenType.Main))
            {
                Advance();
                Expect(TokenType.LParen, "Expected '(' after 'main'");
                Expect(TokenType.RParen, "Expected ')' after '('");
                body = ParseBlock();
            }
            else break;
        }

        Expect(TokenType.Eof, "Expected end of file");
        return new ProgramNode(scriptName, ns, imports, fns, body);
    }

    // ── Declarations ──────────────────────────────────────────────────────────

    private FnDecl ParseFnDecl()
    {
        Advance(); // "fn"
        var name = Expect(TokenType.Identifier, "Expected function name").Value;
        Expect(TokenType.LParen, "Expected '(' after function name");
        var parms = new List<string>();
        while (!Check(TokenType.RParen) && !Check(TokenType.Eof))
        {
            parms.Add(Expect(TokenType.Identifier, "Expected parameter name").Value);
            if (Check(TokenType.Comma)) Advance();
        }
        Expect(TokenType.RParen, "Expected ')' after parameters");
        return new FnDecl(name, parms, ParseBlock());
    }

    // ── Statements ────────────────────────────────────────────────────────────

    private List<Statement> ParseBlock()
    {
        Expect(TokenType.LBrace, "Expected '{'");
        var stmts = new List<Statement>();
        while (!Check(TokenType.RBrace) && !Check(TokenType.Eof))
            stmts.Add(ParseStatement());
        Expect(TokenType.RBrace, "Expected '}'");
        return stmts;
    }

    private Statement ParseStatement() => Current().Type switch
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
        TokenType.Identifier => ParseAssignOrCall(),
        _ => throw new MakoError(
            $"Unexpected token '{Current().Value}' — not a valid statement start",
            Current().Line),
    };

    private PrintStmt ParsePrint()
    {
        Advance();
        var val = ParseExpr();
        Expect(TokenType.Semicolon, "Expected ';' after print");
        return new PrintStmt(val);
    }

    private ConstStmt ParseConst()
    {
        Advance(); // "const"
        var name = Expect(TokenType.Identifier, "Expected name after 'const'").Value;
        Expect(TokenType.Assign, $"Expected '=' after const '{name}'");
        var val = ParseExpr();
        Expect(TokenType.Semicolon, $"Expected ';' after const '{name}'");
        return new ConstStmt(name, val);
    }

    private PrintnlStmt ParsePrintnl()
    {
        Advance();
        var val = ParseExpr();
        Expect(TokenType.Semicolon, "Expected ';' after printnl");
        return new PrintnlStmt(val);
    }

    private Statement ParseAssignOrCall()
    {
        var name = Advance().Value; // identifier

        // Namespaced call: Ns.func(args);
        if (Check(TokenType.Dot))
        {
            Advance(); // .
            var fnName = Expect(TokenType.Identifier, $"Expected function name after '{name}.'").Value;
            var call = ParseNamespacedCallTail(name, fnName);
            Expect(TokenType.Semicolon, "Expected ';' after function call");
            return new ExprStmt(call);
        }

        // Bare function call: name(args);
        if (Check(TokenType.LParen))
        {
            var call = ParseCallTail(name);
            Expect(TokenType.Semicolon, "Expected ';' after function call");
            return new ExprStmt(call);
        }

        // Index assignment: name[idx] = expr;
        if (Check(TokenType.LBracket))
        {
            Advance(); // [
            var idx = ParseExpr();
            Expect(TokenType.RBracket, "Expected ']'");
            Expect(TokenType.Assign, "Expected '=' after index");
            var rhs = ParseExpr();
            Expect(TokenType.Semicolon, "Expected ';' after index assignment");
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
            _ => throw new MakoError(
                $"Expected '=' or assignment operator after '{name}' (got '{Current().Value}')",
                Current().Line),
        };
        Advance();
        var val = ParseExpr();
        if (compoundOp != null)
            val = new BinaryExpr(new IdentExpr(name), compoundOp, val);
        Expect(TokenType.Semicolon, $"Expected ';' after assignment to '{name}'");
        return new AssignStmt(name, val);
    }

    private IfStmt ParseIf()
    {
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
        return new IfStmt(cond, then, els);
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
        var varName = Expect(TokenType.Identifier, "Expected variable name after 'for'").Value;
        Expect(TokenType.In, "Expected 'in' after variable name");
        var iterable = ParseExpr();
        return new ForStmt(varName, iterable, ParseBlock());
    }

    private BreakStmt ParseBreak()
    {
        int line = Current().Line;
        Advance();
        Expect(TokenType.Semicolon, "Expected ';' after break");
        return new BreakStmt();
    }

    private ContinueStmt ParseContinue()
    {
        Advance();
        Expect(TokenType.Semicolon, "Expected ';' after continue");
        return new ContinueStmt();
    }

    private ReturnStmt ParseReturn()
    {
        Advance(); // "return"
        Expr? val = null;
        if (!Check(TokenType.Semicolon))
            val = ParseExpr();
        Expect(TokenType.Semicolon, "Expected ';' after return");
        return new ReturnStmt(val);
    }

    private RunStmt ParseRun()
    {
        Advance();
        var cmd = ParseExpr();
        Expect(TokenType.Semicolon, "Expected ';' after run");
        return new RunStmt(cmd);
    }

    // ── Expressions ───────────────────────────────────────────────────────────

    private Expr ParseExpr() => ParseLogical();

    private Expr ParseLogical()
    {
        var left = ParseComparison();
        while (Current().Type is TokenType.And or TokenType.Or)
        {
            var op = Advance().Value;
            left = new LogicalExpr(left, op, ParseComparison());
        }
        return left;
    }

    private Expr ParseComparison()
    {
        var left = ParseAddition();
        while (Current().Type is TokenType.EqEq or TokenType.NotEq
               or TokenType.Lt or TokenType.Gt or TokenType.LtEq or TokenType.GtEq)
        {
            var op = Advance().Value;
            left = new BinaryExpr(left, op, ParseAddition());
        }
        return left;
    }

    private Expr ParseAddition()
    {
        var left = ParseMultiply();
        while (Current().Type is TokenType.Plus or TokenType.Minus)
        {
            var op = Advance().Value;
            left = new BinaryExpr(left, op, ParseMultiply());
        }
        return left;
    }

    private Expr ParseMultiply()
    {
        var left = ParseUnary();
        while (Current().Type is TokenType.Star or TokenType.Slash or TokenType.Percent)
        {
            var op = Advance().Value;
            left = new BinaryExpr(left, op, ParseUnary());
        }
        return left;
    }

    private Expr ParseUnary()
    {
        if (Check(TokenType.Bang) || Check(TokenType.Not))
        {
            Advance();
            return new UnaryExpr("!", ParseUnary());
        }
        if (Check(TokenType.Minus))
        {
            Advance();
            return new UnaryExpr("-", ParseUnary());
        }
        return ParsePostfix();
    }

    // Handles chained indexing: expr[i][j]
    private Expr ParsePostfix()
    {
        var expr = ParsePrimary();
        while (Check(TokenType.LBracket))
        {
            Advance(); // [
            var idx = ParseExpr();
            Expect(TokenType.RBracket, "Expected ']'");
            expr = new IndexExpr(expr, idx);
        }
        return expr;
    }

    private Expr ParsePrimary()
    {
        var tok = Current();

        if (Check(TokenType.String))         { Advance(); return new StringLit(tok.Value); }
        if (Check(TokenType.TemplateString)) { Advance(); return ParseTemplateString(tok.Value, tok.Line); }
        if (Check(TokenType.Number))   { Advance(); return new NumberLit(double.Parse(tok.Value, System.Globalization.CultureInfo.InvariantCulture)); }
        if (Check(TokenType.True))     { Advance(); return new BoolLit(true); }
        if (Check(TokenType.False))    { Advance(); return new BoolLit(false); }
        if (Check(TokenType.None))     { Advance(); return new NullLit(); }

        // List literal: [expr, expr, ...]
        if (Check(TokenType.LBracket))
        {
            Advance(); // [
            var items = new List<Expr>();
            while (!Check(TokenType.RBracket) && !Check(TokenType.Eof))
            {
                items.Add(ParseExpr());
                if (Check(TokenType.Comma)) Advance();
            }
            Expect(TokenType.RBracket, "Expected ']'");
            return new ListLit(items);
        }

        if (Check(TokenType.Input))
        {
            Advance();
            return new InputExpr(ParsePrimary());
        }

        if (Check(TokenType.Identifier))
        {
            Advance();
            // Namespace.func(args)
            if (Check(TokenType.Dot))
            {
                Advance(); // .
                var fnName = Expect(TokenType.Identifier, $"Expected function name after '{tok.Value}.'").Value;
                return ParseNamespacedCallTail(tok.Value, fnName);
            }
            if (Check(TokenType.LParen))
                return ParseCallTail(tok.Value);
            return new IdentExpr(tok.Value);
        }

        if (Check(TokenType.LParen))
        {
            Advance();
            var inner = ParseExpr();
            Expect(TokenType.RParen, "Expected ')'");
            return inner;
        }

        throw new MakoError($"Unexpected token '{tok.Value}' in expression", tok.Line);
    }

    private CallExpr ParseCallTail(string name)
    {
        Advance(); // (
        var args = ParseArgList();
        return new CallExpr(name, args);
    }

    private NamespacedCallExpr ParseNamespacedCallTail(string ns, string fn)
    {
        Expect(TokenType.LParen, $"Expected '(' after '{ns}.{fn}'");
        var args = ParseArgList();
        return new NamespacedCallExpr(ns, fn, args);
    }

    private List<Expr> ParseArgList()
    {
        var args = new List<Expr>();
        while (!Check(TokenType.RParen) && !Check(TokenType.Eof))
        {
            args.Add(ParseExpr());
            if (Check(TokenType.Comma)) Advance();
        }
        Expect(TokenType.RParen, "Expected ')' after arguments");
        return args;
    }

    // ── Template string interpolation ─────────────────────────────────────────

    // Public entry point used by sub-parsers for {expr} content.
    public Expr ParseExprPublic()
    {
        var e = ParseExpr();
        Expect(TokenType.Eof, "Expected end of interpolated expression");
        return e;
    }

    // Converts "Hello, {name}! You are {age} years old." into a chain of + expressions.
    private Expr ParseTemplateString(string raw, int line)
    {
        var parts = new List<Expr>();
        int i = 0;
        var sb = new System.Text.StringBuilder();

        while (i < raw.Length)
        {
            char c = raw[i];

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
                if (depth != 0) throw new MakoError("Unterminated '{' in string interpolation", line);

                var exprSrc = raw[start..i];
                var subTokens = new Lexer(exprSrc).Tokenize();
                var subExpr   = new Parser(subTokens).ParseExprPublic();
                // Wrap in to_str() so any type prints cleanly
                parts.Add(new CallExpr("to_str", [subExpr]));
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
            result = new BinaryExpr(result, "+", parts[j]);
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

    private Token Expect(TokenType type, string message)
    {
        if (!Check(type))
            throw new MakoError($"{message} (got '{Current().Value}')", Current().Line);
        return Advance();
    }
}
