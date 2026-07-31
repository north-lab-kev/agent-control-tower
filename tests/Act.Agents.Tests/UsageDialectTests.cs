using System.Globalization;
using Act.Agents.ClaudeCode;
using Act.Agents.Codex;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.Agents.Tests;

public class ClaudeCodeUsageDialectTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 4, 0, 0, TimeSpan.Zero);

    private const string Measured = """
        {"five_hour":{"utilization":16.0,"resets_at":"2026-07-31T07:59:59.088971+00:00","limit_dollars":null},
         "seven_day":{"utilization":29.0,"resets_at":"2026-08-01T17:00:00.088993+00:00","limit_dollars":null},
         "seven_day_opus":null,
         "limits":[
           {"kind":"session","group":"session","percent":16,"severity":"normal","resets_at":"2026-07-31T07:59:59.088971+00:00","scope":null,"is_active":false},
           {"kind":"weekly_all","group":"weekly","percent":29,"severity":"normal","resets_at":"2026-08-01T17:00:00.088993+00:00","scope":null,"is_active":true},
           {"kind":"weekly_scoped","group":"weekly","percent":0,"severity":"normal","resets_at":null,"scope":{"model":{"id":null,"display_name":"Fable"}},"is_active":false}],
         "member_dashboard_available":false}
        """;

    private const string WithoutLimits = """
        {"five_hour":{"utilization":16.0,"resets_at":"2026-07-31T07:59:59.088971+00:00"},
         "seven_day":{"utilization":29.0,"resets_at":"2026-08-01T17:00:00.088993+00:00"}}
        """;

    [Fact]
    public void The_two_account_windows_come_off_the_limits_array()
    {
        var usage = new ClaudeCodeUsageDialect().Parse(Measured, Now);

        usage!.Windows.Select(window => (window.Kind, window.Percent))
            .Should().Equal((UsageWindowKind.Session, 16), (UsageWindowKind.Weekly, 29));
    }

    // `weekly_scoped` is a per-model window that reports no reset at all, so there is nothing to
    // count down to and nothing to render.
    [Fact]
    public void A_scoped_window_without_a_reset_is_skipped()
    {
        var usage = new ClaudeCodeUsageDialect().Parse(Measured, Now);

        usage!.Windows.Should().HaveCount(2);
    }

    [Fact]
    public void Reset_times_are_read_as_absolute_moments()
    {
        var usage = new ClaudeCodeUsageDialect().Parse(Measured, Now);

        usage!.Of(UsageWindowKind.Session)!.ResetsAt
            .Should().Be(DateTimeOffset.Parse("2026-07-31T07:59:59.088971+00:00"));
    }

    // The named pair is the fallback for a response that predates `limits`, and there `utilization`
    // carries the percentage rather than a fraction.
    [Fact]
    public void The_named_windows_answer_when_the_limits_array_is_absent()
    {
        var usage = new ClaudeCodeUsageDialect().Parse(WithoutLimits, Now);

        usage!.Windows.Select(window => (window.Kind, window.Percent))
            .Should().Equal((UsageWindowKind.Session, 16), (UsageWindowKind.Weekly, 29));
    }

    [Fact]
    public void A_body_that_is_not_a_usage_response_reads_as_nothing()
    {
        new ClaudeCodeUsageDialect().Parse("""{"error":"unauthorized"}""", Now).Should().BeNull();
    }

    [Fact]
    public void The_access_token_comes_off_the_oauth_block()
    {
        var credentials = Credentials(Now.AddHours(1).ToUnixTimeMilliseconds());

        new ClaudeCodeUsageDialect().Token(credentials, Now).Should().Be("sk-ant-oat01-EXAMPLE");
    }

    // ACT never refreshes the token — Claude Code owns that file — so an expired one is reported as
    // no token rather than spent on a request that can only 401.
    [Fact]
    public void An_expired_token_is_not_offered()
    {
        var credentials = Credentials(Now.AddMinutes(-1).ToUnixTimeMilliseconds());

        new ClaudeCodeUsageDialect().Token(credentials, Now).Should().BeNull();
    }

    [Fact]
    public void An_unreadable_credentials_file_yields_no_token()
    {
        new ClaudeCodeUsageDialect().Token("not json", Now).Should().BeNull();
    }

    [Fact]
    public void The_credentials_path_is_resolved_under_the_running_users_profile()
    {
        new ClaudeCodeUsageDialect().DefaultCredentialsPath()
            .Should().EndWith(Path.Combine(".claude", ".credentials.json"));
    }

    private static string Credentials(long expiresAt)
        => """
            {"claudeAiOauth":{"accessToken":"sk-ant-oat01-EXAMPLE","refreshToken":"sk-ant-ort01-EXAMPLE",
             "expiresAt":EXPIRES,"subscriptionType":"team","rateLimitTier":"default_claude_max_5x"}}
            """.Replace("EXPIRES", expiresAt.ToString(CultureInfo.InvariantCulture));
}

public class CodexUsageDialectTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 4, 0, 0, TimeSpan.Zero);

    private const string Free = """
        {"plan_type":"free",
         "rate_limit":{"allowed":true,"limit_reached":false,
           "primary_window":{"used_percent":5,"limit_window_seconds":2592000,"reset_after_seconds":2470886,"reset_at":1787944859},
           "secondary_window":null},
         "rate_limit_reached_type":null}
        """;

    // ⚠️ PENDING — the paid response body has not been measured; this account is on the free plan and
    // reports one 30-day window. The two window lengths are taken from a real `team` rollout
    // (`window_minutes` 300 and 10080) and the field names from the free response. Re-capture against a
    // paid login and replace this fixture with the measured body.
    private const string Paid = """
        {"plan_type":"team",
         "rate_limit":{"allowed":true,"limit_reached":false,
           "primary_window":{"used_percent":1,"limit_window_seconds":18000,"reset_after_seconds":9000,"reset_at":1780938558},
           "secondary_window":{"used_percent":37,"limit_window_seconds":604800,"reset_after_seconds":400000,"reset_at":1781287181}},
         "rate_limit_reached_type":null}
        """;

    [Fact]
    public void A_paid_plan_reports_the_five_hour_and_weekly_windows()
    {
        var usage = new CodexUsageDialect().Parse(Paid, Now);

        usage!.Windows.Select(window => (window.Kind, window.Percent))
            .Should().Equal((UsageWindowKind.Session, 1), (UsageWindowKind.Weekly, 37));
    }

    // The free plan has no 5-hour window at all, which is why the indicator labels a window by the
    // length the server declares rather than by a fixed pair of captions.
    [Fact]
    public void A_free_plan_reports_a_single_monthly_window()
    {
        var usage = new CodexUsageDialect().Parse(Free, Now);

        usage!.Windows.Select(window => window.Kind).Should().Equal(UsageWindowKind.Monthly);
        usage.Plan.Should().Be("free");
    }

    // `reset_after_seconds` is a countdown the server computed, so it holds even when this machine's
    // clock disagrees with OpenAI's about what time it is.
    [Fact]
    public void The_countdown_is_preferred_over_the_absolute_reset()
    {
        var usage = new CodexUsageDialect().Parse(Free, Now);

        usage!.Windows[0].ResetsAt.Should().Be(Now.AddSeconds(2470886));
    }

    [Fact]
    public void The_absolute_reset_answers_when_no_countdown_is_given()
    {
        const string body = """
            {"rate_limit":{"primary_window":{"used_percent":5,"limit_window_seconds":18000,"reset_at":1787944859}}}
            """;

        new CodexUsageDialect().Parse(body, Now)!.Windows[0].ResetsAt
            .Should().Be(DateTimeOffset.FromUnixTimeSeconds(1787944859));
    }

    [Fact]
    public void A_reached_limit_is_carried_through()
    {
        const string body = """
            {"rate_limit":{"limit_reached":true,
              "primary_window":{"used_percent":100,"limit_window_seconds":18000,"reset_after_seconds":600}}}
            """;

        new CodexUsageDialect().Parse(body, Now)!.LimitReached.Should().BeTrue();
    }

    [Fact]
    public void A_body_without_a_rate_limit_block_reads_as_nothing()
    {
        new CodexUsageDialect().Parse("""{"detail":"unauthorized"}""", Now).Should().BeNull();
    }

    [Fact]
    public void The_access_token_comes_off_the_tokens_block()
    {
        const string credentials = """
            {"auth_mode":"chatgpt","OPENAI_API_KEY":null,
             "tokens":{"id_token":"eyJEXAMPLE","access_token":"eyJACCESS","refresh_token":"rt.1.EXAMPLE",
                       "account_id":"00000000-0000-0000-0000-000000000000"}}
            """;

        new CodexUsageDialect().Token(credentials, Now).Should().Be("eyJACCESS");
    }

    [Fact]
    public void An_unreadable_credentials_file_yields_no_token()
    {
        new CodexUsageDialect().Token("not json", Now).Should().BeNull();
    }

    [Fact]
    public void The_credentials_path_is_resolved_from_the_codex_home()
    {
        new CodexUsageDialect().DefaultCredentialsPath().Should().EndWith("auth.json");
    }
}
