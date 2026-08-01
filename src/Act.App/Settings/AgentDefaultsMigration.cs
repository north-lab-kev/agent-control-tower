using Act.App.Cards;
using Act.Core.Model;

namespace Act.App.Settings;

// The binary, the extra flags and the environment used to be typed per task, under *Advanced* on
// the form. They became per-agent settings on 2026-08-01 — they describe the install, not the work
// — and this carries what a real board already had across, once, so a Codex card launched
// yesterday still launches today.
//
// Without it the loss is silent and specific: a Store-packaged Codex is on no `PATH`, so every
// existing Codex card would fail at spawn with "not found", for a path the user had already
// supplied and could no longer see anywhere.
//
// Newest card wins, because the most recent time the user typed a path is the one most likely to
// still be right. It fills **gaps only** — anything already in settings is the user's and outranks
// a card — and it runs at most once, tracked by an explicit `AgentDefaultsLifted` marker rather
// than inferred from whether a settings row exists. That inference was the first version's bug: a
// row written by an earlier build made the lift look done, and the path it existed to rescue
// stayed lost.
public sealed class AgentDefaultsMigration(
    BoardState board,
    UserSettingsService settings,
    ILogger<AgentDefaultsMigration> log)
{
    public void Run()
    {
        if (settings.AgentDefaultsLifted)
            return;

        foreach (var agent in Enum.GetValues<AgentType>())
        {
            var filled = settings.Defaults(agent).FillGapsFrom(Lift(agent));

            settings.SetDefaults(filled);

            log.LogInformation(
                "Agent defaults for {Agent} lifted from {Cards} cards: executable {Binary}.",
                agent,
                board.All.Count,
                filled.Binary ?? "(none found)");
        }

        settings.MarkAgentDefaultsLifted();
    }

    private AgentDefaults Lift(AgentType agent)
    {
        var defaults = new AgentDefaults { Agent = agent };

        var source = board.All
            .Where(card => card.AgentType == agent)
            .OrderByDescending(card => card.LaunchedAt ?? card.CreatedAt)
            .FirstOrDefault(card =>
                !string.IsNullOrWhiteSpace(card.LaunchConfig.AgentBinary)
                || card.LaunchConfig.ExtraFlags.Count > 0
                || card.LaunchConfig.Env.Count > 0);

        if (source is null)
            return defaults;

        defaults.Binary = string.IsNullOrWhiteSpace(source.LaunchConfig.AgentBinary)
            ? null
            : source.LaunchConfig.AgentBinary.Trim();
        defaults.ExtraFlags = [.. source.LaunchConfig.ExtraFlags];
        defaults.Env = new Dictionary<string, string>(source.LaunchConfig.Env);

        return defaults;
    }
}
