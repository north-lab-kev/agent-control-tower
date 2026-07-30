namespace Act.Core.Model;

public sealed class Transition
{
    public DateTimeOffset At { get; set; }

    public BoardColumn? Column { get; set; }

    public Badge? Badge { get; set; }

    public string? Note { get; set; }
}
