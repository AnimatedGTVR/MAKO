namespace Mako;

/// Recursive-descent parser.
/// Grammar (v0.1):
///
///   program     = script_decl? main_decl? EOF
///   script_decl = "script" STRING ";"
///   main_decl   = "main" "(" ")" "{" statement* "}"
///   statement   = print_stmt | assign_stmt | if_stmt | run_stmt
///   print_stmt  = "print" expr ";"
///   assign_stmt = IDENT "=" expr ";"
///   if_stmt     = "if" expr block ( "else" block )?
///   run_stmt    = "run" expr ";"
///   block       = "{" statement* "}"
///   expr        = comparison
///   comparison  = addition ( ("==" | "!=" | "<" | ">" | "<=" | ">=") addition )*
///   addition    = multiply ( ("+" | "-") multiply )*
///   multiply    = unary  ( ("*" | "/") unary )*
///   unary       = "!" unary | primary
///   primary     = STRING | NUMBER | "true" | "false" | "input" expr | IDENT | "(" expr ")"

class Parser
{
    private readonly List<Token> _tokens;
    private int _pos;

    public Parser(List<Token> tokens) => _tokens = tokens;

    public ProgramNode Parse()
    {
        string? scriptName = null;
        var body = new List<Statement>();

        if (Check(TokenType.Script))
        {
            Advance(); // consume "script"
            scriptName = Expect(TokenType.String, "Expected script name string").Value;
            Expect(TokenType.Semicolon, "Expected ';' after script name");
        }

        if (Check(TokenType.Main))
        {
            Advance(); // consume "main"
            Expect(TokenType.LParen, "Expected '(' after 'main'");
            Expect(TokenType.RParen, "Expected ')' after '('");
            body = ParseBlock();
        }

        Expect(TokenType.Eof, "Expected end of file");
        return new ProgramNode(scriptName, body);
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

    private Statement ParseStatement()
    {
        return Current().Type switch
        {
            TokenType.Print      => ParsePrint(),
            TokenType.If         => ParseIf(),
            TokenType.Run        => ParseRun(),
            TokenType.Identifier => ParseAssign(),
            _ => throw new MakoError(
                $"Unexpected token '{Current().Value}' — not a valid statement start",
                Current().Line),
        };
    }

    private PrintStmt ParsePrint()
    {
        Advance(); // "print"
        var val = ParseExpr();
        Expect(TokenType.Semicolon, "Expected ';' after print value");
        return new PrintStmt(val);
    }

    private AssignStmt ParseAssign()
    {
        var name = Advance().Value; // identifier
        Expect(TokenType.Assign, $"Expected '=' after '{name}'");
        var val = ParseExpr();
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
            Advance(); // "else"
            // Allow "else if" as shorthand for "else { if ... }"
            if (Check(TokenType.If))
                els = new List<Statement> { ParseIf() };
            else
                els = ParseBlock();
        }
        return new IfStmt(cond, then, els);
    }

    private RunStmt ParseRun()
    {
        Advance(); // "run"
        var cmd = ParseExpr();
        Expect(TokenType.Semicolon, "Expected ';' after run command");
        return new RunStmt(cmd);
    }

    // ── Expressions ───────────────────────────────────────────────────────────

    private Expr ParseExpr() => ParseComparison();

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
        while (Current().Type is TokenType.Star or TokenType.Slash)
        {
            var op = Advance().Value;
            left = new BinaryExpr(left, op, ParseUnary());
        }
        return left;
    }

    private Expr ParseUnary()
    {
        if (Check(TokenType.Bang))
        {
            var op = Advance().Value;
            return new UnaryExpr(op, ParseUnary());
        }
        return ParsePrimary();
    }

    private Expr ParsePrimary()
    {
        var tok = Current();

        if (Check(TokenType.String))  { Advance(); return new StringLit(tok.Value); }
        if (Check(TokenType.Number))  { Advance(); return new NumberLit(double.Parse(tok.Value, System.Globalization.CultureInfo.InvariantCulture)); }
        if (Check(TokenType.True))    { Advance(); return new BoolLit(true); }
        if (Check(TokenType.False))   { Advance(); return new BoolLit(false); }

        if (Check(TokenType.Input))
        {
            Advance(); // "input"
            var prompt = ParsePrimary(); // accepts a single primary as the prompt
            return new InputExpr(prompt);
        }

        if (Check(TokenType.Identifier))
        {
            Advance();
            return new IdentExpr(tok.Value);
        }

        if (Check(TokenType.LParen))
        {
            Advance(); // (
            var inner = ParseExpr();
            Expect(TokenType.RParen, "Expected ')'");
            return inner;
        }

        throw new MakoError($"Unexpected token '{tok.Value}' in expression", tok.Line);
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
