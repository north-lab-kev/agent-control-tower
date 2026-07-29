using Act.Core.Model;

namespace Act.Core.Agents;

// The instruction block ACT injects at session start. It is agent-agnostic on purpose —
// the `.act/` conventions are ACT's, not any one CLI's, so the wording lives here and each
// adapter only decides how to deliver it. The status convention is advisory (unlike an
// enforced permission request), which is why the fallback is spelled out to the agent too.
public static class AgentPreamble
{
    public static string Compose(Guid taskId, AutoGitOptions? autoGit = null)
    {
        var prefix = ActContract.FilePrefix(taskId);

        return $$"""
            ## ACT session conventions

            You are running under ACT (Agent Control Tower), which tracks this session on a
            board and relies on two file conventions to know where you stand. Both live under
            `{{ActContract.RootDirectoryName}}/` in your working directory. Create the
            directories if they are missing, and write every file atomically — write it under
            a temporary name in the same directory, then rename it into place — so ACT never
            reads a half-written file.

            ### Status — write one before you end every turn

            Path: `{{ActContract.RelativeStatusDirectory}}/{{prefix}}<turn>.json`, where
            `<turn>` is 1 on your first turn and one higher on each turn after it.

            Write exactly one of:

                { "state": "ready_for_review" }
                { "state": "needs_input", "question": "what you need from the user" }

            Use `ready_for_review` when the work is done and nothing is blocking you. Use
            `needs_input` when you cannot continue without the user, and put the question in
            `question` — it is shown on the board. If you end a turn without this file, ACT
            assumes `ready_for_review`.
            {{GitSection(autoGit)}}
            ### Follow-ups — optional, one file per task you want queued

            Path: `{{ActContract.RelativeFollowUpsDirectory}}/{{prefix}}<nnn>.json`, where
            `<nnn>` is a sequence you choose: `001`, `002`, and so on.

                {
                  "title": "short name",
                  "prompt": "the full instruction for that task",
                  "cwd": "optional working directory; defaults to this one",
                  "dependsOn": ["001"]
                }

            `dependsOn` is optional and lists the sequence numbers of sibling follow-ups that
            must finish first. Write these files before you end the turn — ACT reads them then
            and queues one task for each. Only split work out this way when it genuinely
            belongs in its own task.
            """;
    }

    private static string GitSection(AutoGitOptions? autoGit)
        => autoGit is null
            ? string.Empty
            : $"""

                ### Git — do this before your final status file

                This task is configured to {GitInstruction(autoGit)}. Do it as your last piece
                of work, then write the `ready_for_review` status file. If any git step fails,
                write `needs_input` instead and say which step failed.

                """;

    private static string GitInstruction(AutoGitOptions autoGit) => autoGit.Action switch
    {
        GitAction.Commit => "commit your work",
        GitAction.Push => "commit and push your work",
        GitAction.PullRequest => autoGit.Draft
            ? "commit, push, and open a draft pull request"
            : "commit, push, and open a pull request",
        _ => "commit your work",
    };
}
