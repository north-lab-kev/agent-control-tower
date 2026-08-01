using Act.Core.Model;
using AwesomeAssertions;

namespace Act.Core.Tests;

// The one-time lift of what used to be typed per card. Its first version skipped any agent that
// already had a settings row, which meant a row written by an earlier build — or by the user
// touching one field — permanently blocked the rescue of the others. Filling gaps is what makes
// the lift safe to run against a half-populated document.
public class AgentDefaultsTests
{
    [Fact]
    public void An_empty_entry_takes_everything_it_is_offered()
    {
        var filled = new AgentDefaults { Agent = AgentType.Codex }.FillGapsFrom(Lifted());

        filled.Binary.Should().Be(@"C:\tools\codex.exe");
        filled.ExtraFlags.Should().Equal("--verbose");
        filled.Env.Should().ContainKey("HTTPS_PROXY");
    }

    // The half-populated case the marker bug left behind: a row exists, but the field that mattered
    // is still empty.
    [Fact]
    public void A_missing_executable_is_filled_even_though_the_entry_exists()
    {
        var existing = new AgentDefaults
        {
            Agent = AgentType.Codex,
            Env = new Dictionary<string, string> { ["MINE"] = "1" },
        };

        var filled = existing.FillGapsFrom(Lifted());

        filled.Binary.Should().Be(@"C:\tools\codex.exe");
        filled.Env.Should().ContainKey("MINE").And.NotContainKey("HTTPS_PROXY");
    }

    [Fact]
    public void What_the_user_already_set_is_never_overwritten()
    {
        var existing = new AgentDefaults
        {
            Agent = AgentType.Codex,
            Binary = @"D:\mine\codex.exe",
            ExtraFlags = ["--quiet"],
        };

        var filled = existing.FillGapsFrom(Lifted());

        filled.Binary.Should().Be(@"D:\mine\codex.exe");
        filled.ExtraFlags.Should().Equal("--quiet");
    }

    // Whitespace is how an emptied box arrives, and it must count as a gap rather than as a value —
    // otherwise clearing the field would make the entry permanently unfillable.
    [Fact]
    public void A_blank_executable_counts_as_a_gap()
        => new AgentDefaults { Binary = "   " }.FillGapsFrom(Lifted())
            .Binary.Should().Be(@"C:\tools\codex.exe");

    [Fact]
    public void Filling_from_nothing_leaves_the_entry_empty()
    {
        var filled = new AgentDefaults { Agent = AgentType.ClaudeCode }
            .FillGapsFrom(new AgentDefaults { Agent = AgentType.ClaudeCode });

        filled.Binary.Should().BeNull();
        filled.ExtraFlags.Should().BeEmpty();
        filled.Env.Should().BeEmpty();
    }

    private static AgentDefaults Lifted() => new()
    {
        Agent = AgentType.Codex,
        Binary = @"C:\tools\codex.exe",
        ExtraFlags = ["--verbose"],
        Env = new Dictionary<string, string> { ["HTTPS_PROXY"] = "http://proxy:8080" },
    };
}
