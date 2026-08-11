namespace Act.Core.Scheduling;

// The percentage at which a quota window counts as spent, and so the point where auto-execution
// stops rather than launching into a refusal.
//
// It is below 100 by default and settable because 100 is the wrong place to stop. The percentage ACT
// holds is up to three minutes old (`Usage:PollSeconds`), a 5-hour window moves about a third of a
// percent a minute, and the number is rounded to a whole percent before ACT ever sees it — so a
// reading of 99 can be a window that is already full. A task launched into the last percent does not
// fail cleanly either: it starts, burns the remainder, and stops mid-run, which costs more than
// waiting for the reset would have.
//
// The bounds only keep the number a percentage; how much quota to leave for interactive work is the
// user's call, not this rule's. `1` rather than `0` because a ceiling of zero would hold every card
// forever, which is what the master switch already says plainly.
public static class UsageCeiling
{
    public const int Default = 95;

    public const int Minimum = 1;

    public const int Maximum = 100;

    public static int Clamp(int percent) => Math.Clamp(percent, Minimum, Maximum);
}
