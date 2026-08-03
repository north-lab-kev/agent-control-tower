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

    // Clamped, because the two figures come from different places and can disagree: a limit read
    // from an earlier transcript line, or a model whose effective window is smaller than the one
    // ACT knows, both produce a used count past the limit — and a bar that overflows its own track
    // reads as a defect rather than as a full context.
    public int? ContextPercent
        => ContextLimit > 0 ? (int)Math.Clamp(100L * ContextUsed / ContextLimit, 0, 100) : null;
}
