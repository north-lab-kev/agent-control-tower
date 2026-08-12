namespace Act.Core.Model;

public sealed class WindowBounds
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public bool Maximized { get; set; }

    public WindowBounds Copy() => new()
    {
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        Maximized = Maximized,
    };
}
