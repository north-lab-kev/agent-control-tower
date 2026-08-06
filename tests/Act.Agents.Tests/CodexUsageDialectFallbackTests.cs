using Act.Agents.Codex;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.Agents.Tests;

// Two undocumented endpoints and a credential file the CLI owns, so every shape below is one a
// vendor change can produce tomorrow. None of them may throw: an unreadable quota is a named
// unavailability, and the queue launches anyway rather than freezing an overnight run.
[Collection(EnvironmentCollection.Name)]
public class CodexUsageDialectFallbackTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly CodexUsageDialect dialect = new();

    [Fact]
    public void The_dialect_answers_for_codex()
    {
        dialect.Agent.Should().Be(AgentType.Codex);
        dialect.DefaultEndpoint.Should().Be("https://chatgpt.com/backend-api/wham/usage");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("\"a string\"")]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{ "tokens": null }""")]
    [InlineData("""{ "tokens": "a string" }""")]
    [InlineData("""{ "tokens": { } }""")]
    [InlineData("""{ "tokens": { "access_token": "" } }""")]
    [InlineData("""{ "tokens": { "access_token": 7 } }""")]
    public void A_credential_file_with_no_usable_token_reports_missing(string credentials)
        => dialect.Token(credentials, Now).Should().Be(UsageToken.Missing);

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "rate_limit": null }""")]
    [InlineData("""{ "rate_limit": [] }""")]
    [InlineData("""{ "rate_limit": {} }""")]
    [InlineData("""{ "rate_limit": { "primary_window": 7 } }""")]
    [InlineData("""{ "rate_limit": { "primary_window": { "used_percent": 40 } } }""")]
    public void A_response_with_no_readable_window_is_null_rather_than_an_empty_reading(string response)
        => dialect.Parse(response, Now).Should().BeNull("no reading is not the same as a reading of zero");

    // A percentage the endpoint reports outside the range it documents is clamped rather than
    // rendered as a meter past its own end.
    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(42.4, 42)]
    [InlineData(42.6, 43)]
    [InlineData(100, 100)]
    [InlineData(140, 100)]
    public void A_percentage_is_rounded_and_clamped(double reported, int expected)
        => dialect.Parse(Response($"\"used_percent\": {reported}, \"limit_window_seconds\": 18000"), Now)!
            .Windows[0].Percent.Should().Be(expected);

    [Theory]
    [InlineData("""{ "limit_window_seconds": 18000 }""")]
    [InlineData("""{ "limit_window_seconds": 18000, "used_percent": null }""")]
    [InlineData("""{ "limit_window_seconds": 18000, "used_percent": "40" }""")]
    public void A_percentage_ACT_cannot_read_is_zero_rather_than_a_missing_window(string window)
        => dialect.Parse($$"""{ "rate_limit": { "primary_window": {{window}} } }""", Now)!
            .Windows.Should().ContainSingle().Which.Percent.Should().Be(0);

    // Relative first, because it is what the endpoint actually sends; the absolute form is the
    // fallback for a shape that has been seen but is not the norm.
    [Fact]
    public void A_relative_reset_is_measured_from_the_instant_the_reading_was_taken()
        => dialect.Parse(Response("\"limit_window_seconds\": 18000, \"reset_after_seconds\": 3600"), Now)!
            .Windows[0].ResetsAt.Should().Be(Now.AddHours(1));

    [Fact]
    public void An_absolute_reset_is_read_as_unix_seconds()
        => dialect.Parse(Response("\"limit_window_seconds\": 18000, \"reset_at\": 1785715200"), Now)!
            .Windows[0].ResetsAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1785715200));

    [Fact]
    public void A_relative_reset_wins_when_the_endpoint_sends_both()
        => dialect.Parse(
                Response("\"limit_window_seconds\": 18000, \"reset_after_seconds\": 60, \"reset_at\": 1785715200"),
                Now)!
            .Windows[0].ResetsAt.Should().Be(Now.AddMinutes(1));

    // A window with no reset instant is one nothing has run in yet. Dropping it for the missing
    // instant is what once left the top bar showing a weekly meter and no session one.
    [Fact]
    public void A_window_with_no_reset_is_still_a_window()
    {
        var window = dialect.Parse(Response("\"limit_window_seconds\": 18000, \"used_percent\": 12"), Now)!
            .Windows.Should().ContainSingle().Which;

        window.ResetsAt.Should().BeNull();
        window.Percent.Should().Be(12);
    }

    // Named by the length the server declares, never by a fixed caption pair: a free plan reports a
    // single 30-day window, and hardcoding "5-hour plus weekly" would mislabel a real account.
    [Theory]
    [InlineData(18_000, UsageWindowKind.Session)]
    [InlineData(604_800, UsageWindowKind.Weekly)]
    [InlineData(2_592_000, UsageWindowKind.Monthly)]
    public void A_window_is_named_by_the_length_the_server_declares(int seconds, UsageWindowKind kind)
        => dialect.Parse(Response($"\"limit_window_seconds\": {seconds}"), Now)!
            .Windows[0].Kind.Should().Be(kind);

    [Fact]
    public void The_limit_flag_is_only_true_when_the_endpoint_really_says_so()
    {
        Reading("""{ "rate_limit": { "primary_window": { "limit_window_seconds": 18000 } } }""")
            .LimitReached.Should().BeFalse();

        Reading("""
            { "rate_limit": { "limit_reached": "yes", "primary_window": { "limit_window_seconds": 18000 } } }
            """)
            .LimitReached.Should().BeFalse("a string is not the flag");

        Reading("""
            { "rate_limit": { "limit_reached": true, "primary_window": { "limit_window_seconds": 18000 } } }
            """)
            .LimitReached.Should().BeTrue();
    }

    [Fact]
    public void The_plan_is_carried_when_it_is_named_and_null_when_it_is_not()
    {
        Reading("""
            { "plan_type": "plus", "rate_limit": { "primary_window": { "limit_window_seconds": 18000 } } }
            """)
            .Plan.Should().Be("plus");

        Reading("""{ "rate_limit": { "primary_window": { "limit_window_seconds": 18000 } } }""")
            .Plan.Should().BeNull();
    }

    [Fact]
    public void The_credentials_path_follows_the_codex_home_ACT_was_pointed_at()
    {
        var original = Environment.GetEnvironmentVariable("CODEX_HOME");

        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", @"C:\custom\codex");

            dialect.DefaultCredentialsPath().Should().Be(Path.Combine(@"C:\custom\codex", "auth.json"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", original);
        }
    }

    private AgentUsage Reading(string response) => dialect.Parse(response, Now)!;

    private static string Response(string window)
        => $$"""{ "rate_limit": { "primary_window": { {{window}} } } }""";
}
