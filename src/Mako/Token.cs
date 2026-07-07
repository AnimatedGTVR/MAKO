namespace Mako;

enum TokenType
{
    // Literals
    String, Number, True, False,

    // Identifier (variable/function names)
    Identifier,

    // Keywords
    Script,
    Main,
    Print,
    Input,
    If,
    Else,
    Run,
    Using,
    Use,

    // Operators
    Assign,   // =
    Plus,     // +
    Minus,    // -
    Star,     // *
    Slash,    // /
    EqEq,     // ==
    NotEq,    // !=
    Lt,       // <
    Gt,       // >
    LtEq,     // <=
    GtEq,     // >=
    Bang,     // !

    // Punctuation
    LParen,    // (
    RParen,    // )
    LBrace,    // {
    RBrace,    // }
    Semicolon, // ;

    Eof,
}

record Token(TokenType Type, string Value, int Line)
{
    public override string ToString() => $"[{Type} '{Value}' L{Line}]";
}
