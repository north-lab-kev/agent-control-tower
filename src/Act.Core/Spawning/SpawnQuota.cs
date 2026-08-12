using Act.Core.Model;

namespace Act.Core.Spawning;

// The runaway-loop backstop. Counted off `children[]` rather than off anything per-session, so the
// budget is the card's for its whole lifetime and a relaunch or a restore cannot refill it — an
// agent that loops does not get a fresh hundred every time its terminal comes back.
//
// A cap of zero is a legitimate answer and means this board does not accept agent-spawned work.
public static class SpawnQuota
{
    public const int Default = 100;

    public const int Minimum = 0;

    public const int Maximum = 1000;

    public static int Clamp(int cap) => Math.Clamp(cap, Minimum, Maximum);

    public static bool Exhausted(Card parent, int cap) => parent.Children.Count >= Clamp(cap);
}
