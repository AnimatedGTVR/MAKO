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

    // Struct names declared anywhere in this file, collected before body
    // parsing so 'Ident { ... }' can be told apart from a block/dict at
    // the point we're parsing an expression (the parser has no type
    // inference — this set is the only way it can know 'Point' names a
    // struct rather than being e.g. an unrelated variable holding a dict).
    private readonly HashSet<string> _structNames = new(StringComparer.Ordinal);

    public Parser(List<Token> tokens) => _tokens = tokens;

    /// Used by the REPL: a fresh Parser only sees the current line's tokens,
    /// so it can't discover struct names declared on earlier lines via its
    /// own CollectStructNames() scan. The REPL passes those in explicitly so
    /// "Point { x: 1 }" parses as a struct literal even when "struct Point"
    /// was typed on a previous line, in a previous Parser instance.
    public Parser(List<Token> tokens, IEnumerable<string> knownStructNames) : this(tokens)
    {
        foreach (var name in knownStructNames) _structNames.Add(name);
    }

    public ProgramNode Parse()
    {
        CollectStructNames();

        string? scriptName = null;
        string? ns         = null;
        var packages  = new List<PackageRef>();
        var imports   = new List<string>();
        var constants = new List<(string Name, Expr Value)>();
        var fns       = new List<FnDecl>();
        var structs   = new List<StructDecl>();
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
            else if (Check(TokenType.Struct))
                structs.Add(ParseStructDecl());
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
                    : Suggest.Closest(tok.Value, ["fn", "struct", "main", "use", "using", "const", "script", "namespace"]);
                if (s != null) hint = $" (did you mean '{s}'?)";
            }
            throw new MakoError(
                $"unexpected {DescribeToken(tok)} at top level — only 'fn'/'struct' declarations and 'main()' are allowed{hint}",
                tok.Line, tok.Col, Math.Max(1, tok.Value.Length));
        }

        return new ProgramNode(scriptName, ns, packages, imports, constants, fns, structs, body,
                               mainTok?.Line ?? 0);
    }

    // ── Declarations ──────────────────────────────────────────────────────────

    /// A single forward scan for 'struct Name { ... }' declarations, run
    /// before real parsing starts — see _structNames for why this is needed.
    private void CollectStructNames()
    {
        for (int i = 0; i < _tokens.Count - 1; i++)
            if (_tokens[i].Type == TokenType.Struct && _tokens[i + 1].Type == TokenType.Identifier)
                _structNames.Add(_tokens[i + 1].Value);
    }

    private StructDecl ParseStructDecl()
    {
        var line = Current().Line;
        Advance(); // "struct"
        var name = Expect(TokenType.Identifier, "expected a struct name after 'struct'").Value;
        var open = Expect(TokenType.LBrace, $"missing '{{' after struct name '{name}'");
        var fields = new List<string>();
        while (!Check(TokenType.RBrace) && !Check(TokenType.Eof))
        {
            fields.Add(Expect(TokenType.Identifier, "expected a field name").Value);
            if (Check(TokenType.Comma)) { Advance(); continue; }
            if (!Check(TokenType.RBrace) && !Check(TokenType.Eof))
            {
                var prev = Previous();
                throw new MakoError(
                    $"missing ',' or '}}' between fields (got {DescribeToken(Current())})",
                    prev.EndLine, prev.EndCol);
            }
        }
        ExpectClosing(TokenType.RBrace, "}", open);
        return new StructDecl(name, fields) { Line = line };
    }

    private FnDecl ParseFnDecl()
    {
        var fnLine = Current().Line;
        Advance(); // "fn"
        var name = Expect(TokenType.Identifier, "expected a function name after 'fn'").Value;

        // fn TypeName.method(self, ...) { }  — a struct method, stored as
        // "TypeName.method" so it dispatches through the same _funcs table
        // package functions already use ("Ns.func").
        if (Check(TokenType.Dot))
        {
            Advance(); // .
            var methodName = Expect(TokenType.Identifier, $"expected a method name after '{name}.'").Value;
            name = $"{name}.{methodName}";
        }

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
        return new FnDecl(name, parms, ParseBlock()) { Line = fnLine };
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
            TokenType.Throw      => ParseThrow(),
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

        // Namespaced call or variable: Ns.func(args)  /  Ns.CONSTANT
        if (Check(TokenType.Dot))
        {
            Advance(); // .
            var fnName = Expect(TokenType.Identifier, $"expected a function name after '{name}.'").Value;

            // p.method(args);  — namespaced call syntactically, but may
            // resolve to a struct method call at runtime (see EvalNamespacedCall).
            if (Check(TokenType.LParen))
            {
                var call = ParseNamespacedCallTail(nameTok, fnName);
                // Further chaining after the call, e.g. p.method().field — fall
                // through the general postfix/assignment path below.
                return FinishPostfixStatement(call);
            }

            Expr baseExpr = new IdentExpr(name) { Line = nameTok.Line, Col = nameTok.Col };
            Expr target = new FieldExpr(baseExpr, fnName) { Line = nameTok.Line, Col = nameTok.Col };
            return FinishPostfixStatement(target);
        }

        // Bare function call: name(args);
        if (Check(TokenType.LParen))
        {
            var call = ParseCallTail(nameTok);
            Expect(TokenType.Semicolon, "function call");
            return new ExprStmt(call);
        }

        // Index assignment: name[idx] = expr;   /   name[i][j] = expr;   (any number of [...])
        if (Check(TokenType.LBracket))
        {
            var indices = new List<Expr>();
            while (Check(TokenType.LBracket))
            {
                var openBr = Advance(); // [
                indices.Add(ParseExpr());
                ExpectClosing(TokenType.RBracket, "]", openBr);
            }
            Expect(TokenType.Assign, "missing '=' after index");
            var rhs = ParseExpr();
            Expect(TokenType.Semicolon, "index assignment");
            return new IndexAssignStmt(name, indices, rhs);
        }

        // Optional type hint: name: TypeName = expr;   — purely syntactic,
        // never enforced by the interpreter (see AssignStmt.TypeHint doc
        // comment). Only a plain assignment may carry a hint, not a
        // compound one — "count: number += 1;" reads oddly and isn't
        // needed since the hint would already be on the variable's first
        // (plain) assignment.
        string? typeHint = null;
        if (Check(TokenType.Colon))
        {
            Advance(); // :
            typeHint = Expect(TokenType.Identifier, $"expected a type name after '{name}:'").Value;
        }

        // Compound or plain assignment (bare "name = expr;", no field/index)
        string? compoundOp = Current().Type switch
        {
            TokenType.Assign  => null,
            TokenType.PlusEq  => typeHint == null ? "+" : throw UnknownAfterName(nameTok),
            TokenType.MinusEq => typeHint == null ? "-" : throw UnknownAfterName(nameTok),
            TokenType.StarEq  => typeHint == null ? "*" : throw UnknownAfterName(nameTok),
            TokenType.SlashEq => typeHint == null ? "/" : throw UnknownAfterName(nameTok),
            _ => throw UnknownAfterName(nameTok),
        };
        Advance();
        var val = ParseExpr();
        if (compoundOp != null)
            val = new BinaryExpr(new IdentExpr(name) { Line = nameTok.Line, Col = nameTok.Col },
                                 compoundOp, val)
                  { Line = nameTok.Line, Col = nameTok.Col };
        Expect(TokenType.Semicolon, $"assignment to '{name}'");
        return new AssignStmt(name, val, typeHint);
    }

    /// Continues a postfix chain (".field" / "[index]" / ".method(args)")
    /// that started with a struct field or method-call statement, then
    /// resolves it as either a field assignment ("...= expr;"), a bare call
    /// statement ("p.method(args);"), or (if nothing assignable follows) a
    /// discarded expression statement.
    private Statement FinishPostfixStatement(Expr expr)
    {
        while (true)
        {
            if (Check(TokenType.LBracket))
            {
                var openBr = Advance();
                var idx = ParseExpr();
                ExpectClosing(TokenType.RBracket, "]", openBr);
                expr = new IndexExpr(expr, idx) { Line = openBr.Line, Col = openBr.Col };
                continue;
            }
            if (Check(TokenType.Dot))
            {
                var dotTok = Advance();
                var field = Expect(TokenType.Identifier, "expected a field or method name after '.'").Value;
                if (Check(TokenType.LParen))
                {
                    var open = Advance();
                    var args = ParseArgList(open);
                    expr = new MethodCallExpr(expr, field, args) { Line = dotTok.Line, Col = dotTok.Col };
                }
                else
                {
                    expr = new FieldExpr(expr, field) { Line = dotTok.Line, Col = dotTok.Col };
                }
                continue;
            }
            break;
        }

        if (Check(TokenType.Assign) && expr is FieldExpr fieldTarget)
        {
            Advance(); // =
            var rhs = ParseExpr();
            Expect(TokenType.Semicolon, "field assignment");
            return new FieldAssignStmt(fieldTarget.Target, fieldTarget.Field, rhs);
        }

        Expect(TokenType.Semicolon, "expression");
        return new ExprStmt(expr);
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

    private ThrowStmt ParseThrow()
    {
        Advance(); // "throw"
        var msg = ParseExpr();
        Expect(TokenType.Semicolon, "throw");
        return new ThrowStmt(msg);
    }

    private TryStmt ParseTry()
    {
        Advance(); // try
        var tryBody = ParseBlock();

        string? catchVar = null;
        var catchBody = new List<Statement>();
        bool hasCatch = false;

        if (Check(TokenType.Catch))
        {
            Advance(); // catch
            hasCatch = true;
            if (Check(TokenType.Identifier))
                catchVar = Advance().Value;
            catchBody = ParseBlock();
        }

        return new TryStmt(tryBody, catchVar, catchBody, hasCatch);
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
        while (true)
        {
            if (Check(TokenType.LBracket))
            {
                var openBr = Advance(); // [
                var idx = ParseExpr();
                ExpectClosing(TokenType.RBracket, "]", openBr);
                expr = new IndexExpr(expr, idx) { Line = openBr.Line, Col = openBr.Col };
                continue;
            }
            // Chained field/method access: expr.field  /  expr.method(args)
            // Only reachable here for the *second and later* dots in a chain —
            // ParsePrimary already consumes a leading "Ident.Ident" itself
            // (for namespace calls/constants like Net.get or MakoRay.RED),
            // so this only fires once that's already been handled, or after
            // any non-identifier primary (a call result, an index, etc.).
            if (Check(TokenType.Dot))
            {
                var dotTok = Advance(); // .
                var field = Expect(TokenType.Identifier, "expected a field or method name after '.'").Value;
                if (Check(TokenType.LParen))
                {
                    var open = Advance(); // (
                    var args = ParseArgList(open);
                    expr = new MethodCallExpr(expr, field, args) { Line = dotTok.Line, Col = dotTok.Col };
                }
                else
                {
                    expr = new FieldExpr(expr, field) { Line = dotTok.Line, Col = dotTok.Col };
                }
                continue;
            }
            break;
        }
        return expr;
    }

    private Expr ParsePrimary()
    {
        var tok = Current();

        if (Check(TokenType.String))         { Advance(); return new StringLit(tok.Value); }
        if (Check(TokenType.TemplateString))
        {
            Advance();
            var expanded = ParseTemplateString(tok.Value, tok.Line, tok.Col);
            return new TemplateStringExpr(tok.Value, expanded) { Line = tok.Line, Col = tok.Col };
        }
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
            // TypeName { field: value, ... }  — struct construction. Gated on
            // a name collected by CollectStructNames(), otherwise 'Ident {'
            // would be ambiguous with a block starting right after a bare
            // identifier expression (which isn't valid anyway, but this keeps
            // the grammar unambiguous rather than relying on that).
            if (_structNames.Contains(tok.Value) && Check(TokenType.LBrace))
                return ParseStructLitTail(tok);
            // Namespace.func(args)
            if (Check(TokenType.Dot))
            {
                Advance(); // .
                var fnName = Expect(TokenType.Identifier, $"expected a function name after '{tok.Value}.'").Value;
                // If followed by '(' it's a call; otherwise it's a namespaced variable (e.g. MakoRay.BLACK).
                if (Check(TokenType.LParen))
                    return ParseNamespacedCallTail(tok, fnName);
                return new IdentExpr($"{tok.Value}.{fnName}") { Line = tok.Line, Col = tok.Col };
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

    private StructLitExpr ParseStructLitTail(Token typeTok)
    {
        var open = Advance(); // {
        var fields = new List<(string Field, Expr Value)>();
        while (!Check(TokenType.RBrace) && !Check(TokenType.Eof))
        {
            var field = Expect(TokenType.Identifier, "expected a field name").Value;
            Expect(TokenType.Colon, $"expected ':' after field name '{field}'");
            var val = ParseExpr();
            fields.Add((field, val));
            if (Check(TokenType.Comma)) { Advance(); continue; }
            if (!Check(TokenType.RBrace) && !Check(TokenType.Eof))
            {
                var prev = Previous();
                throw new MakoError(
                    $"missing ',' or '}}' between fields (got {DescribeToken(Current())})",
                    prev.EndLine, prev.EndCol);
            }
        }
        ExpectClosing(TokenType.RBrace, "}", open);
        return new StructLitExpr(typeTok.Value, fields) { Line = typeTok.Line, Col = typeTok.Col };
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

                // Find the matching closing brace (handle nesting). Skip over
                // nested string literals (e.g. dict["key"]) whole so a quoted
                // '{' or '}' inside one doesn't unbalance the depth count.
                int depth = 1, start = i + 1;
                i++;
                while (i < raw.Length && depth > 0)
                {
                    if (raw[i] == '"')
                    {
                        i++;
                        while (i < raw.Length && raw[i] != '"')
                        {
                            if (raw[i] == '\\' && i + 1 < raw.Length) i++;
                            i++;
                        }
                    }
                    else if (raw[i] == '{') depth++;
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
    private Token Current() { SkipComments(); return _tokens[_pos]; }

    private void SkipComments()
    {
        while (_pos < _tokens.Count && _tokens[_pos].Type == TokenType.Comment)
            _pos++;
    }

    private Token Advance()
    {
        SkipComments();
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
