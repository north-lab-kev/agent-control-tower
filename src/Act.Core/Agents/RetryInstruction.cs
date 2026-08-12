using Act.Core.Resources;

namespace Act.Core.Agents;

// What a retried session is told when it comes back. It rides the resume command line as the
// opening prompt, exactly as the initial prompt does at launch — ACT still types nothing into a
// live terminal.
//
// **Not the initial prompt.** Re-sending that to a session forty turns deep tells it to start the
// work over: by then the opening instruction describes a beginning that no longer exists, and the
// transcript the resume just reopened is the real context. So this says only that the session was
// interrupted, and leaves *what to do about it* to the agent reading its own history.
//
// Localised like everything ACT writes to an agent — see `AttachmentInstruction`.
public static class RetryInstruction
{
    public static string Message => CoreStrings.Retry_Continue;
}
