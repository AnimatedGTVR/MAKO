namespace Mako;

class Lexer
{
    private readonly string _src;
    private int _pos;
    private int _line = 1;

    private static readonly Dictionary<string, TokenType> Keywords = new()
    {
        ["script"]    = TokenType.Script,
        ["namespace"] = TokenType.Namespace,
        ["main"]      = TokenType.Main,
        ["const"]     = TokenType.Const,
        ["print"]     = TokenType.Print,
        ["printnl"]   = TokenType.Printnl,
        ["input"]     = TokenType.Input,
        ["if"]        = TokenType.If,
        ["else"]      = TokenType.Else,
        ["while"]     = TokenType.While,
        ["for"]       = TokenType.For,
        ["in"]        = TokenType.In,
        ["break"]     = TokenType.Break,
        ["continue"]  = TokenType.Continue,
        ["fn"]        = TokenType.Fn,
        ["return"]    = TokenType.Return,
        ["run"]       = TokenType.Run,
        ["and"]       = TokenType.And,
        ["or"]        = TokenType.Or,
        ["not"]       = TokenType.Not,
        ["using"]     = TokenType.Using,
        ["use"]       = TokenType.Use,
        ["true"]      = TokenType.True,
        ["false"]     = TokenType.False,
        ["none"]      = TokenType.None,
    };

    public Lexer(string source) => _src = source;

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        while (true)
        {
            SkipWhitespaceAndComments();
            if (_pos >= _src.Length)
            {
                tokens.Add(Tok(TokenType.Eof, ""));
                break;
            }
            tokens.Add(ReadToken());
        }
        return tokens;
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void SkipWhitespaceAndComments()
    {
        while (_pos < _src.Length)
        {
            char c = _src[_pos];
            if (c == '\n') { _line++; _pos++; continue; }
            if (char.IsWhiteSpace(c)) { _pos++; continue; }

            if (c == '/' && Peek(1) == '/')
            {
                while (_pos < _src.Length && _src[_pos] != '\n') _pos++;
                continue;
            }
            if (c == '/' && Peek(1) == '*')
            {
                _pos += 2;
                while (_pos < _src.Length)
                {
                    if (_src[_pos] == '\n') _line++;
                    if (_src[_pos] == '*' && Peek(1) == '/') { _pos += 2; break; }
                    _pos++;
                }
                continue;
            }
            break;
        }
    }

    private Token ReadToken()
    {
        int line = _line;
        char c = _src[_pos];

        if (c == '"') return ReadString(line);
        if (char.IsDigit(c)) return ReadNumber(line);
        if (char.IsLetter(c) || c == '_') return ReadIdentifier(line);

        char next = Peek(1);
        if (c == '=' && next == '=') { _pos += 2; return Tok(TokenType.EqEq,    "==", line); }
        if (c == '!' && next == '=') { _pos += 2; return Tok(TokenType.NotEq,   "!=", line); }
        if (c == '<' && next == '=') { _pos += 2; return Tok(TokenType.LtEq,    "<=", line); }
        if (c == '>' && next == '=') { _pos += 2; return Tok(TokenType.GtEq,    ">=", line); }
        if (c == '+' && next == '=') { _pos += 2; return Tok(TokenType.PlusEq,  "+=", line); }
        if (c == '-' && next == '=') { _pos += 2; return Tok(TokenType.MinusEq, "-=", line); }
        if (c == '*' && next == '=') { _pos += 2; return Tok(TokenType.StarEq,  "*=", line); }
        if (c == '/' && next == '=') { _pos += 2; return Tok(TokenType.SlashEq, "/=", line); }

        _pos++;
        return c switch
        {
            '=' => Tok(TokenType.Assign,    "=",  line),
            '+' => Tok(TokenType.Plus,      "+",  line),
            '-' => Tok(TokenType.Minus,     "-",  line),
            '*' => Tok(TokenType.Star,      "*",  line),
            '/' => Tok(TokenType.Slash,     "/",  line),
            '%' => Tok(TokenType.Percent,   "%",  line),
            '<' => Tok(TokenType.Lt,        "<",  line),
            '>' => Tok(TokenType.Gt,        ">",  line),
            '!' => Tok(TokenType.Bang,      "!",  line),
            '.' => Tok(TokenType.Dot,       ".",  line),
            '(' => Tok(TokenType.LParen,    "(",  line),
            ')' => Tok(TokenType.RParen,    ")",  line),
            '{' => Tok(TokenType.LBrace,    "{",  line),
            '}' => Tok(TokenType.RBrace,    "}",  line),
            '[' => Tok(TokenType.LBracket,  "[",  line),
            ']' => Tok(TokenType.RBracket,  "]",  line),
            ';' => Tok(TokenType.Semicolon, ";",  line),
            ',' => Tok(TokenType.Comma,     ",",  line),
            _   => throw new MakoError($"Unexpected character '{c}'", line),
        };
    }

    private Token ReadString(int line)
    {
        _pos++; // opening "
        var sb         = new System.Text.StringBuilder();
        bool isTemplate = false;
        int  braceDepth = 0;

        while (_pos < _src.Length && _src[_pos] != '"')
        {
            char c = _src[_pos];

            if (braceDepth == 0)
            {
                // {{ → literal { (escape)
                if (c == '{' && Peek(1) == '{') { sb.Append('{'); _pos += 2; continue; }
                // }} → literal } (escape)
                if (c == '}' && Peek(1) == '}') { sb.Append('}'); _pos += 2; continue; }

                // Start of interpolation
                if (c == '{') { isTemplate = true; braceDepth++; sb.Append('{'); _pos++; continue; }

                // Normal escape processing outside braces
                if (c == '\\' && _pos + 1 < _src.Length)
                {
                    _pos++;
                    sb.Append(_src[_pos] switch
                    {
                        'n' => '\n', 't' => '\t', '"' => '"',
                        '\\' => '\\', 'r' => '\r', var x => x,
                    });
                    _pos++; continue;
                }
                if (c == '\n') { _line++; sb.Append('\n'); _pos++; continue; }
                sb.Append(c); _pos++;
            }
            else
            {
                // Inside {expr} — pass through verbatim so the parser can re-parse it
                if (c == '{')  braceDepth++;
                if (c == '}') { braceDepth--; }
                if (c == '\n') _line++;
                sb.Append(c); _pos++;
            }
        }

        if (_pos >= _src.Length)
            throw new MakoError("Unterminated string", line);
        _pos++; // closing "

        return Tok(isTemplate ? TokenType.TemplateString : TokenType.String, sb.ToString(), line);
    }

    private Token ReadNumber(int line)
    {
        int start = _pos;
        while (_pos < _src.Length && (char.IsDigit(_src[_pos]) || _src[_pos] == '.'))
            _pos++;
        return Tok(TokenType.Number, _src[start.._pos], line);
    }

    private Token ReadIdentifier(int line)
    {
        int start = _pos;
        while (_pos < _src.Length && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '_'))
            _pos++;
        var word = _src[start.._pos];
        var type = Keywords.TryGetValue(word, out var kw) ? kw : TokenType.Identifier;
        return Tok(type, word, line);
    }

    private char Peek(int offset = 0) =>
        _pos + offset < _src.Length ? _src[_pos + offset] : '\0';

    private Token Tok(TokenType type, string value, int line = -1) =>
        new(type, value, line < 0 ? _line : line);
}
