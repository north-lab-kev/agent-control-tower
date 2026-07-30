namespace Act.Core.Abstractions;

public sealed record TerminalSize(int Cols, int Rows)
{
    public static TerminalSize Default { get; } = new(120, 30);
}
