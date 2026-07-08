namespace Mako;

class MakoError : Exception
{
    public int    Line       { get; }
    public int    Col        { get; }   // 1-based column; 0 = unknown (underline whole line)
    public int    Length     { get; }   // how many '^' to draw under the offending spot
    public string RawMessage { get; }

    /// Set when the error happened in an imported module rather than the main file,
    /// so the CLI can show a snippet from the right source.
    public string? SourcePath { get; init; }

    /// Optional "did you mean X?" or contextual hint shown below the error line.
    public string? Hint { get; init; }

    public MakoError(string message, int line = 0, int col = 0, int length = 1)
        : base(line > 0 ? $"[line {line}] {message}" : message)
    {
        Line       = line;
        Col        = col;
        Length     = length;
        RawMessage = message;
    }
}
