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
    Using, Use,

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

    Eof,
}

record Token(TokenType Type, string Value, int Line)
{
    public override string ToString() => $"[{Type} '{Value}' L{Line}]";
}
