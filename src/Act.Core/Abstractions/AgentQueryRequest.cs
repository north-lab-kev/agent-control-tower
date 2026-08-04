using Act.Core.Model;

namespace Act.Core.Abstractions;

// One question, one answer, no session. Deliberately not a `LaunchConfig`: a query is not a run of
// the user's work, so the model, the effort and the permission mode are none of the caller's
// business — the adapter picks the cheapest thing it has (see `AgentCapabilities.UtilityModel`) and
// the caller cannot make it expensive.
//
// `Machine` is the half that *does* apply, and for the same reason it applies to a launch: where
// this machine keeps the CLI and what environment it needs are facts about the install, not about
// the request. Its `ExtraFlags` are ignored — they are the user's launch preferences, and a
// `--permission-mode` or a `--model` among them would silently redirect a query that has no
// business honouring either.
public sealed record AgentQueryRequest(string Prompt, AgentDefaults? Machine = null)
{
    // Tighter than `CommandHost`'s own ceiling, because someone is usually waiting on this one: a
    // title arrives in five to seven seconds, so a run still going after this is wedged rather than
    // thinking, and holding a Save button for a minute and a half to find that out is not a trade
    // worth making. The deadline is not an error — the caller falls back.
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(45);
}
