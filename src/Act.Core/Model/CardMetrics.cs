namespace Act.Core.Model;

public sealed class CardMetrics
{
    public int ContextUsed { get; init; }

    public int ContextLimit { get; init; }

    public int TurnCount { get; init; }

    public int Compactions { get; init; }

    public decimal Cost { get; init; }

    public DateTimeOffset? LastActivityAt { get; init; }

    public int? ContextPercent
        => ContextLimit > 0 ? (int)(100L * ContextUsed / ContextLimit) : null;
}
