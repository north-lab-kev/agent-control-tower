using Act.Core.Model;
using Act.Core.Resources;

namespace Act.Core.Agents;

// The one thing ACT appends to a task's prompt, and the exception that proves the rule: ACT
// injects no conventions of its own (there is no preamble), but `autoGit` is the *user's* own
// instruction, chosen on the task form, so ACT is only phrasing what they already asked for.
//
// Localised like every other string ACT writes: the agent is told to do the work in the
// language the user runs ACT in. `AppCulture` sets `DefaultThreadCurrentUICulture` process
// wide, so the lookup follows the UI language from any thread — including a launch that later
// comes from the queue runner rather than a click.
//
// The suffix is composed at launch and never stored: `Card.InitialPrompt` stays verbatim for
// the life of the task, which is what the form promises.
public static class AutoGitInstruction
{
    public static string Append(string prompt, AutoGitOptions? autoGit)
        => autoGit is null
            ? prompt
            : $"{prompt}\n\n{For(autoGit)}";

    public static string For(AutoGitOptions autoGit) => autoGit.Action switch
    {
        GitAction.Commit => CoreStrings.AutoGit_Commit,
        GitAction.Push => CoreStrings.AutoGit_Push,
        GitAction.PullRequest => autoGit.Draft
            ? CoreStrings.AutoGit_DraftPullRequest
            : CoreStrings.AutoGit_PullRequest,
        _ => CoreStrings.AutoGit_Commit,
    };
}
