namespace Act.Core.Spawning;

// One `create_followup` call, exactly as it arrived. Every knob is a nullable string rather than a
// parsed enum because parsing is where the error messages come from: an agent that asks for a
// permission mode that does not exist has to be told what does, and a binder that threw before the
// resolver ran would answer with a schema violation instead.
public sealed record FollowUpRequest(
    string Title,
    string Prompt,
    string? Agent = null,
    string? Model = null,
    string? Effort = null,
    string? Permission = null,
    string? Schedule = null,
    string? WorkingDir = null,
    IReadOnlyList<string>? DependsOn = null,
    string? ClientKey = null)
{
    // What every inheritable knob defaults to, and what it means: the parent card's value. Not the
    // user's global defaults and not a template — a follow-up is the same work in the same tree.
    public const string Inherit = "same";

    public static bool Inherits(string? value)
        => string.IsNullOrWhiteSpace(value) || value.Trim().Equals(Inherit, StringComparison.OrdinalIgnoreCase);

    // Null or the trimmed key: the one normalization both the idempotency lookup and the stored
    // `SpawnKey` read, so a change to it can never make a retry stop matching the key it stored.
    public string? NormalizedClientKey
        => string.IsNullOrWhiteSpace(ClientKey) ? null : ClientKey.Trim();
}
