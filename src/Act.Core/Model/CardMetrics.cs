namespace Act.Core.Model;

public sealed class CardMetrics
{
    public long TokensIn { get; set; }

    public long TokensOut { get; set; }

    public int Compactions { get; set; }

    public int ContextUsed { get; set; }

    public int ContextLimit { get; set; }

    public int TurnCount { get; set; }

    public int ToolCalls { get; set; }

    public DateTimeOffset? LastActivityAt { get; set; }

    public TimeSpan? ActiveTime { get; set; }

    public long TokensTotal => TokensIn + TokensOut;

    public int? ContextPercent
        => ContextLimit > 0 ? (int)(100L * ContextUsed / ContextLimit) : null;
}
