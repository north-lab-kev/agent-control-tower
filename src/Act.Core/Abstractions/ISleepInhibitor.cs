namespace Act.Core.Abstractions;

// Holds the machine awake. A port because there is no portable way to say it — every OS spells
// it differently — and because the *reason* is ACT's rather than the OS's: an agent working for
// twenty minutes looks exactly like an idle desktop from outside the process.
//
// The hold is for as long as the setting is on, not per session: a queue that starts at 02:00
// needs the machine up before its window opens, not once it is already running.
public interface ISleepInhibitor
{
    bool IsHeld { get; }

    void Hold();

    void Release();
}
