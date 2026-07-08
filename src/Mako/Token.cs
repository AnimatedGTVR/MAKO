namespace Mako;

enum TokenType
{
    // Literals
    String, TemplateString, Number, True, False, None,

    // Identifier (variable/function names)
    Identifier,

    // Keywords
    Script,
    Namespace,
    Main,
    Print, Printnl,
    Input,
    If, Else,
    While, For, In,
    Break, Continue,
    Fn, Return,
    Run,
    And, Or, Not,
    Const,
    Using, Use, From,
    Try, Catch,
    Arrow,   // =>

    // Operators
    Assign,   // =
    Plus,     // +
    Minus,    // -
    Star,     // *
    Slash,    // /
    Percent,  // %
    EqEq,     // ==
    NotEq,    // !=
    Lt,       // <
    Gt,       // >
    LtEq,     // <=
    GtEq,     // >=
    Bang,     // !
    PlusEq,   // +=
    MinusEq,  // -=
    StarEq,   // *=
    SlashEq,  // /=

    // Punctuation
    Dot,       // .
    LParen,    // (
    RParen,    // )
    LBrace,    // {
    RBrace,    // }
    LBracket,  // [
    RBracket,  // ]
    Semicolon, // ;
    Comma,     // ,
    Colon,     // :

    Eof,
}

/// Col/EndCol are 1-based. EndLine/EndCol point just past the token's last
/// character — exactly where a missing ';' caret belongs.
record Token(TokenType Type, string Value, int Line, int Col = 0, int EndLine = 0, int EndCol = 0)
{
    public override string ToString() => $"[{Type} '{Value}' L{Line}:C{Col}]";
}
