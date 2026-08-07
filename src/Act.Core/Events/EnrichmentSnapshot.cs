namespace Act.Core.Events;

// Every field is optional because enrichment arrives piecemeal — a transcript line may
// carry only a token count, a `/model` switch only the observed model. Null means "no news",
// not "zero".
public sealed record EnrichmentSnapshot
{
    public string? ObservedModel { get; init; }

    public long? TokensIn { get; init; }

    public long? TokensOut { get; init; }

    public int? ContextUsed { get; init; }

    public int? ContextLimit { get; init; }

    public int? TurnCount { get; init; }

    public int? ToolCalls { get; init; }
}
