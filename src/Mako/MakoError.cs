namespace Mako;

class MakoError : Exception
{
    public int Line { get; }

    public MakoError(string message, int line = 0)
        : base(line > 0 ? $"[line {line}] {message}" : message)
    {
        Line = line;
    }
}
