namespace Act.Core.Model;

public sealed class Transition
{
    public DateTimeOffset At { get; set; }

    public BoardColumn? Column { get; set; }

    public Badge? Badge { get; set; }

    public TransitionReason? Reason { get; set; }

    // The verbatim half: an exit code, a CLI error message, the adjustments a launch made. Data
    // rather than wording, so it is stored as-is and never translated — anything user-facing that
    // *is* wording belongs to `Reason`.
    public string? Note { get; set; }
}
