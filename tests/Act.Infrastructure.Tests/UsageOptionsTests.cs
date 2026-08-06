using Act.Core.Model;
using Act.Infrastructure.Usage;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;

namespace Act.Infrastructure.Tests;

// `appsettings` holds facts about the machine and the vendor, so every value here arrives from a
// file ACT does not control. The clamps are what stops a typo becoming a poll loop that hammers a
// vendor endpoint or one that never fires.
public class UsageOptionsTests
{
    [Fact]
    public void The_defaults_watch_both_agents_every_three_minutes()
    {
        var options = new UsageOptions();

        options.Enabled.Should().BeTrue();
        options.PollInterval.Should().Be(TimeSpan.FromMinutes(3));
        options.RefreshOnExpiry.Should().BeTrue();
        options.RefreshCooldown.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(-1, 60)]
    [InlineData(30, 60)]
    [InlineData(60, 60)]
    [InlineData(600, 600)]
    [InlineData(3600, 3600)]
    [InlineData(86400, 3600)]
    public void The_poll_interval_is_clamped_to_a_minute_and_an_hour(int configured, int expected)
        => new UsageOptions { PollSeconds = configured }
            .PollInterval.Should().Be(TimeSpan.FromSeconds(expected));

    [Theory]
    [InlineData(0, 60)]
    [InlineData(30, 60)]
    [InlineData(900, 900)]
    [InlineData(86400, 86400)]
    [InlineData(int.MaxValue, 86400)]
    public void The_refresh_cooldown_is_clamped_to_a_minute_and_a_day(int configured, int expected)
        => new UsageOptions { RefreshCooldownSeconds = configured }
            .RefreshCooldown.Should().Be(TimeSpan.FromSeconds(expected));

    [Fact]
    public void An_agent_with_no_section_gets_the_empty_overrides()
    {
        var agent = new UsageOptions().For(AgentType.ClaudeCode);

        agent.CredentialsPath.Should().BeNull();
        agent.Endpoint.Should().BeNull();
    }

    [Fact]
    public void An_agent_section_overrides_the_discovered_path_and_endpoint()
    {
        var options = new UsageOptions
        {
            Agents = new Dictionary<string, UsageAgentOptions>
            {
                ["ClaudeCode"] = new()
                {
                    CredentialsPath = "/opt/claude/.credentials.json",
                    Endpoint = "https://example.invalid/usage",
                },
            },
        };

        var agent = options.For(AgentType.ClaudeCode);

        agent.CredentialsPath.Should().Be("/opt/claude/.credentials.json");
        agent.Endpoint.Should().Be("https://example.invalid/usage");
    }

    // The keys are hand-typed into a json file, so matching them case-sensitively would turn a
    // lower-cased section name into a silently ignored override.
    [Theory]
    [InlineData("claudecode")]
    [InlineData("CLAUDECODE")]
    [InlineData("ClaudeCode")]
    public void An_agent_section_is_matched_however_it_was_spelled(string key)
    {
        var options = new UsageOptions
        {
            Agents = new Dictionary<string, UsageAgentOptions>
            {
                [key] = new() { Endpoint = "https://example.invalid/usage" },
            },
        };

        options.For(AgentType.ClaudeCode).Endpoint.Should().Be("https://example.invalid/usage");
    }

    [Fact]
    public void One_agent_s_section_is_not_the_other_s()
    {
        var options = new UsageOptions
        {
            Agents = new Dictionary<string, UsageAgentOptions>
            {
                ["Codex"] = new() { Endpoint = "https://example.invalid/codex" },
            },
        };

        options.For(AgentType.Codex).Endpoint.Should().Be("https://example.invalid/codex");
        options.For(AgentType.ClaudeCode).Endpoint.Should().BeNull();
    }

    // The section ships absent rather than blank, so the binding that matters is the one where
    // nothing is there to bind.
    [Fact]
    public void An_absent_section_binds_to_the_defaults()
    {
        var configuration = new ConfigurationBuilder().Build();

        var options = configuration.GetSection(UsageOptions.SectionName).Get<UsageOptions>()
            ?? new UsageOptions();

        options.Enabled.Should().BeTrue();
        options.Agents.Should().BeEmpty();
    }

    [Fact]
    public void A_configured_section_binds_through_to_the_per_agent_overrides()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Usage:Enabled"] = "false",
                ["Usage:PollSeconds"] = "30",
                ["Usage:Agents:Codex:Endpoint"] = "https://example.invalid/codex",
            })
            .Build();

        var options = configuration.GetSection(UsageOptions.SectionName).Get<UsageOptions>();

        options.Should().NotBeNull();
        options.Enabled.Should().BeFalse();
        options.PollInterval.Should().Be(TimeSpan.FromSeconds(60), "30 is below the floor");
        options.For(AgentType.Codex).Endpoint.Should().Be("https://example.invalid/codex");
    }
}
