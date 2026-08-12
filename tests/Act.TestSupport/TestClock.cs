using Act.Core.Abstractions;

namespace Act.TestSupport;

// Advances a fixed step on every read, so a scripted session produces ordered, repeatable
// timestamps without any real time passing.
public sealed class TestClock(DateTimeOffset start, TimeSpan? step = null) : IClock
{
    private readonly TimeSpan step = step ?? TimeSpan.FromSeconds(1);

    private DateTimeOffset now = start;

    public TestClock()
        : this(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero))
    {
    }

    public DateTimeOffset Now
    {
        get
        {
            var reading = now;
            now = now.Add(step);

            return reading;
        }
    }

    public void Advance(TimeSpan amount) => now = now.Add(amount);
}
