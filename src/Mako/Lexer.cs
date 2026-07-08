namespace Mako;

class Lexer
{
    private readonly string _src;
    private int _pos;
    private int _line;
    private int _lineStart;          // index in _src where the current line begins

    // Interpolation sub-lexers start mid-line: report positions relative to the
    // enclosing file so errors inside "{expr}" point at the right place.
    private readonly int _startLine;
    private readonly int _colBase;   // extra column offset applied on the first line

    internal static readonly Dictionary<string, TokenType> Keywords = new()
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
        ["from"]      = TokenType.From,
        ["true"]      = TokenType.True,
        ["false"]     = TokenType.False,
        ["none"]      = TokenType.None,
    };

    public Lexer(string source, int startLine = 1, int startCol = 1)
    {
        _src       = source;
        _line      = startLine;
        _startLine = startLine;
        _colBase   = startCol - 1;
    }

    private int CurCol => _pos - _lineStart + 1 + (_line == _startLine ? _colBase : 0);

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
            if (c == '\n') { _line++; _pos++; _lineStart = _pos; continue; }
            if (char.IsWhiteSpace(c)) { _pos++; continue; }

            if (c == '/' && Peek(1) == '/' || c == '#')
            {
                while (_pos < _src.Length && _src[_pos] != '\n') _pos++;
                continue;
            }
            if (c == '/' && Peek(1) == '*')
            {
                int openLine = _line, openCol = CurCol;
                _pos += 2;
                bool closed = false;
                while (_pos < _src.Length)
                {
                    if (_src[_pos] == '\n') { _line++; _lineStart = _pos + 1; }
                    if (_src[_pos] == '*' && Peek(1) == '/') { _pos += 2; closed = true; break; }
                    _pos++;
                }
                if (!closed)
                    throw new MakoError("missing '*/' to close comment (got end of file)",
                        openLine, openCol, 2);
                continue;
            }
            break;
        }
    }

    private Token ReadToken()
    {
        int line = _line, col = CurCol;
        char c = _src[_pos];

        if (c == '"') return ReadString(line, col);
        if (char.IsDigit(c)) return ReadNumber(line, col);
        if (char.IsLetter(c) || c == '_') return ReadIdentifier(line, col);

        char next = Peek(1);
        if (c == '=' && next == '=') { _pos += 2; return Tok(TokenType.EqEq,    "==", line, col); }
        if (c == '!' && next == '=') { _pos += 2; return Tok(TokenType.NotEq,   "!=", line, col); }
        if (c == '<' && next == '=') { _pos += 2; return Tok(TokenType.LtEq,    "<=", line, col); }
        if (c == '>' && next == '=') { _pos += 2; return Tok(TokenType.GtEq,    ">=", line, col); }
        if (c == '+' && next == '=') { _pos += 2; return Tok(TokenType.PlusEq,  "+=", line, col); }
        if (c == '-' && next == '=') { _pos += 2; return Tok(TokenType.MinusEq, "-=", line, col); }
        if (c == '*' && next == '=') { _pos += 2; return Tok(TokenType.StarEq,  "*=", line, col); }
        if (c == '/' && next == '=') { _pos += 2; return Tok(TokenType.SlashEq, "/=", line, col); }

        _pos++;
        return c switch
        {
            '=' => Tok(TokenType.Assign,    "=",  line, col),
            '+' => Tok(TokenType.Plus,      "+",  line, col),
            '-' => Tok(TokenType.Minus,     "-",  line, col),
            '*' => Tok(TokenType.Star,      "*",  line, col),
            '/' => Tok(TokenType.Slash,     "/",  line, col),
            '%' => Tok(TokenType.Percent,   "%",  line, col),
            '<' => Tok(TokenType.Lt,        "<",  line, col),
            '>' => Tok(TokenType.Gt,        ">",  line, col),
            '!' => Tok(TokenType.Bang,      "!",  line, col),
            '.' => Tok(TokenType.Dot,       ".",  line, col),
            '(' => Tok(TokenType.LParen,    "(",  line, col),
            ')' => Tok(TokenType.RParen,    ")",  line, col),
            '{' => Tok(TokenType.LBrace,    "{",  line, col),
            '}' => Tok(TokenType.RBrace,    "}",  line, col),
            '[' => Tok(TokenType.LBracket,  "[",  line, col),
            ']' => Tok(TokenType.RBracket,  "]",  line, col),
            ';' => Tok(TokenType.Semicolon, ";",  line, col),
            ',' => Tok(TokenType.Comma,     ",",  line, col),
            _   => throw UnexpectedChar(c, line, col),
        };
    }

    private static MakoError UnexpectedChar(char c, int line, int col)
    {
        string hint = c switch
        {
            '&'  => " — use 'and' for logical and",
            '|'  => " — use 'or' for logical or",
            '\'' => " — strings use double quotes, e.g. \"text\"",
            _    => "",
        };
        return new MakoError($"unexpected character '{c}'{hint}", line, col);
    }

    private Token ReadString(int line, int col)
    {
        int openPos         = _pos;
        int lineStartAtOpen = _lineStart;
        int colBaseAtOpen   = _line == _startLine ? _colBase : 0;

        _pos++; // opening "
        int contentStart = _pos;
        var sb         = new System.Text.StringBuilder(); // unescaped value (plain strings)
        bool isTemplate = false;
        int  braceDepth = 0;

        while (_pos < _src.Length && _src[_pos] != '"')
        {
            char c = _src[_pos];

            if (braceDepth == 0)
            {
                // {{ / }} → literal brace (escape)
                if (c == '{' && Peek(1) == '{') { sb.Append('{'); _pos += 2; continue; }
                if (c == '}' && Peek(1) == '}') { sb.Append('}'); _pos += 2; continue; }

                // Start of interpolation
                if (c == '{') { isTemplate = true; braceDepth++; _pos++; continue; }

                // Normal escape processing outside braces
                if (c == '\\' && _pos + 1 < _src.Length)
                {
                    sb.Append(Unescape(_src[_pos + 1]));
                    _pos += 2; continue;
                }
                // A raw line break means a quote was forgotten — point at the
                // end of the line where the closing one belongs. (Strings
                // can't span lines; the \n escape exists for line breaks.)
                if (c == '\n')
                    throw UnterminatedString(openPos, line, lineStartAtOpen, colBaseAtOpen,
                        "missing closing '\"' (got end of line)", CurCol);
                sb.Append(c); _pos++;
            }
            else
            {
                // Inside {expr} — pass through so the parser can re-parse it
                if (c == '{')  braceDepth++;
                if (c == '}') { braceDepth--; }
                if (c == '\n') { _line++; _lineStart = _pos + 1; }
                _pos++;
            }
        }

        if (_pos >= _src.Length)
        {
            // Point at the end of the line the string opened on — where the
            // closing quote most likely belongs.
            int eol = _src.IndexOf('\n', openPos);
            if (eol < 0) eol = _src.Length;
            int endCol = eol - lineStartAtOpen + 1 + colBaseAtOpen;
            throw UnterminatedString(openPos, line, lineStartAtOpen, colBaseAtOpen,
                "missing closing '\"' (got end of file)", endCol);
        }
        int contentEnd = _pos;
        _pos++; // closing "

        // Template strings keep the source text verbatim: the template parser
        // does its own escape/brace handling, and error positions inside {expr}
        // then map 1:1 onto the file.
        return isTemplate
            ? Tok(TokenType.TemplateString, _src[contentStart..contentEnd], line, col)
            : Tok(TokenType.String, sb.ToString(), line, col);
    }

    internal static char Unescape(char c) => c switch
    {
        'n' => '\n', 't' => '\t', '"' => '"',
        '\\' => '\\', 'r' => '\r', var x => x,
    };

    /// A string ran off the end of its line/file. If the quote that "opened"
    /// it sits right after a word (`Functions"`), it was really meant to close
    /// a string — the *opening* quote is the missing one, so point before the
    /// word instead.
    private MakoError UnterminatedString(int openPos, int line, int lineStartAtOpen,
                                         int colBaseAtOpen, string fallback, int fallbackCol)
    {
        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        if (openPos > 0 && IsWordChar(_src[openPos - 1]))
        {
            int ws = openPos;
            while (ws > 0 && IsWordChar(_src[ws - 1])) ws--;
            var word = _src[ws..openPos];
            int wordCol = ws - lineStartAtOpen + 1 + colBaseAtOpen;
            return new MakoError($"missing opening '\"' before '{word}'", line, wordCol);
        }
        return new MakoError(fallback, line, fallbackCol);
    }

    private Token ReadNumber(int line, int col)
    {
        int start = _pos, dots = 0;
        while (_pos < _src.Length && (char.IsDigit(_src[_pos]) || _src[_pos] == '.'))
        {
            if (_src[_pos] == '.') dots++;
            _pos++;
        }
        var text = _src[start.._pos];

        if (dots > 1)
            throw new MakoError($"invalid number '{text}' (more than one decimal point)",
                line, col, text.Length);

        // "12abc" — a name can't start with a digit
        if (_pos < _src.Length && (char.IsLetter(_src[_pos]) || _src[_pos] == '_'))
        {
            while (_pos < _src.Length && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '_'))
                _pos++;
            var full = _src[start.._pos];
            throw new MakoError($"invalid name '{full}' (names cannot start with a digit)",
                line, col, full.Length);
        }

        return Tok(TokenType.Number, text, line, col);
    }

    private Token ReadIdentifier(int line, int col)
    {
        int start = _pos;
        while (_pos < _src.Length && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '_'))
            _pos++;
        var word = _src[start.._pos];
        var type = Keywords.TryGetValue(word, out var kw) ? kw : TokenType.Identifier;
        return Tok(type, word, line, col);
    }

    private char Peek(int offset = 0) =>
        _pos + offset < _src.Length ? _src[_pos + offset] : '\0';

    private Token Tok(TokenType type, string value, int line = -1, int col = -1) =>
        new(type, value,
            line < 0 ? _line : line,
            col  < 0 ? CurCol : col,
            _line, CurCol);
}
