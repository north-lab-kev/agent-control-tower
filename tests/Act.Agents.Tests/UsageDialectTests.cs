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

    // A 5-hour window nothing has run in yet. Reconstructed rather than measured: the `session` entry is
    // the measured one with the reset nulled, which is the shape `weekly_scoped` carries in the body
    // above — a real idle body was not captured because reaching the endpoint starts the window.
    private const string SessionNotStarted = """
        {"five_hour":{"utilization":0,"resets_at":null,"limit_dollars":null},
         "seven_day":{"utilization":24.0,"resets_at":"2026-08-08T16:59:59.284078+00:00","limit_dollars":null},
         "limits":[
           {"kind":"session","group":"session","percent":0,"severity":"normal","resets_at":null,"scope":null,"is_active":false},
           {"kind":"weekly_all","group":"weekly","percent":24,"severity":"normal","resets_at":"2026-08-08T16:59:59.284078+00:00","scope":null,"is_active":true}]}
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

    // `weekly_scoped` is a per-model window, and ACT names windows by the account-level pair. It is
    // skipped on its `kind`, not on its null reset — a null reset is a state the account windows reach
    // too, and skipping on it is what once hid the session meter entirely.
    [Fact]
    public void A_per_model_scoped_window_is_skipped()
    {
        var usage = new ClaudeCodeUsageDialect().Parse(Measured, Now);

        usage!.Windows.Should().HaveCount(2);
    }

    // The 5-hour clock starts on the first request, so an idle account reports the window at 0% with no
    // reset. It is still a window, and reading it as absent left the top bar with a weekly meter and no
    // session one — which reads as ACT being unable to see the session quota at all.
    [Fact]
    public void A_window_that_has_not_started_is_read_as_zero_with_no_reset()
    {
        var usage = new ClaudeCodeUsageDialect().Parse(SessionNotStarted, Now);

        usage!.Windows.Select(window => (window.Kind, window.Percent))
            .Should().Equal((UsageWindowKind.Session, 0), (UsageWindowKind.Weekly, 24));

        usage.Of(UsageWindowKind.Session)!.ResetsAt.Should().BeNull();
    }

    [Fact]
    public void A_named_window_without_a_reset_is_kept_as_well()
    {
        const string body = """{"five_hour":{"utilization":0,"resets_at":null}}""";

        var usage = new ClaudeCodeUsageDialect().Parse(body, Now);

        usage!.Of(UsageWindowKind.Session)!.ResetsAt.Should().BeNull();
        usage.Of(UsageWindowKind.Weekly)!.Percent.Should().Be(0);
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

    // A read that succeeded answers with both account windows whatever the body left out: which entries
    // are present is a property of the account's activity, not of what ACT managed to see.
    [Fact]
    public void A_body_that_names_one_window_still_reports_the_pair()
    {
        const string body = """
            {"limits":[{"kind":"weekly_all","percent":24,"resets_at":"2026-08-08T16:59:59+00:00"}]}
            """;

        var usage = new ClaudeCodeUsageDialect().Parse(body, Now);

        usage!.Windows.Select(window => (window.Kind, window.Percent))
            .Should().Equal((UsageWindowKind.Session, 0), (UsageWindowKind.Weekly, 24));

        usage.Of(UsageWindowKind.Session)!.ResetsAt.Should().BeNull();
    }

    // The named pair fills what `limits` omits rather than being discarded whole, so a partial array
    // never costs a window the body actually reported.
    [Fact]
    public void The_named_pair_fills_a_window_the_limits_array_left_out()
    {
        const string body = """
            {"five_hour":{"utilization":16.0,"resets_at":"2026-07-31T07:59:59+00:00"},
             "limits":[{"kind":"weekly_all","percent":24,"resets_at":"2026-08-08T16:59:59+00:00"}]}
            """;

        new ClaudeCodeUsageDialect().Parse(body, Now)!.Windows
            .Select(window => (window.Kind, window.Percent))
            .Should().Equal((UsageWindowKind.Session, 16), (UsageWindowKind.Weekly, 24));
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

        new ClaudeCodeUsageDialect().Token(credentials, Now)
            .Should().Be(UsageToken.Present("sk-ant-oat01-EXAMPLE"));
    }

    // ACT never performs the refresh itself — Claude Code owns that file — so an expired one is never
    // spent on a request that can only 401. It is reported as *expired* rather than as missing, because
    // the top bar has to be able to say which of the two it is.
    [Fact]
    public void An_expired_token_is_not_offered_and_says_it_expired()
    {
        var credentials = Credentials(Now.AddMinutes(-1).ToUnixTimeMilliseconds());

        new ClaudeCodeUsageDialect().Token(credentials, Now).Should().Be(UsageToken.Expired);
    }

    // The second clock in the file, and the one that decides whether waiting helps: the access token
    // lasts 8 hours and the refresh token about 21 days, so both expired means no amount of running
    // the CLI will fix it and only an interactive sign-in will.
    [Fact]
    public void An_expired_token_beside_a_lapsed_refresh_token_reads_as_lapsed()
    {
        var credentials = Credentials(
            Now.AddMinutes(-1).ToUnixTimeMilliseconds(),
            Now.AddDays(-1).ToUnixTimeMilliseconds());

        new ClaudeCodeUsageDialect().Token(credentials, Now).Should().Be(UsageToken.Lapsed);
    }

    // The ordering that makes the guard correct rather than merely present. The request ACT is about to
    // make uses the *access* token, so a refresh token that has lapsed while the access token is still
    // good is not a problem yet — reading the refresh clock first would blank a working meter for the
    // last hours of a login that happens to be near its end.
    [Fact]
    public void A_lapsed_refresh_token_does_not_withhold_an_access_token_that_still_works()
    {
        var credentials = Credentials(
            Now.AddHours(1).ToUnixTimeMilliseconds(),
            Now.AddDays(-1).ToUnixTimeMilliseconds());

        new ClaudeCodeUsageDialect().Token(credentials, Now)
            .Should().Be(UsageToken.Present("sk-ant-oat01-EXAMPLE"));
    }

    // A CLI old enough not to write the field says nothing about whether the refresh token is good, and
    // "no reason to think it has lapsed" is the reading that keeps the free nudge in play.
    [Fact]
    public void A_file_that_does_not_state_the_refresh_expiry_stays_merely_expired()
    {
        var credentials = """
            {"claudeAiOauth":{"accessToken":"sk-ant-oat01-EXAMPLE","refreshToken":"sk-ant-ort01-EXAMPLE",
             "expiresAt":EXPIRES,"subscriptionType":"team"}}
            """.Replace(
            "EXPIRES",
            Now.AddMinutes(-1).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));

        new ClaudeCodeUsageDialect().Token(credentials, Now).Should().Be(UsageToken.Expired);
    }

    [Fact]
    public void An_unreadable_credentials_file_yields_no_token()
    {
        new ClaudeCodeUsageDialect().Token("not json", Now).Should().Be(UsageToken.Missing);
    }

    [Fact]
    public void The_credentials_path_is_resolved_under_the_running_users_profile()
    {
        new ClaudeCodeUsageDialect().DefaultCredentialsPath()
            .Should().EndWith(Path.Combine(".claude", ".credentials.json"));
    }

    // Shaped after the measured file (claude-code 2.1.220), which carries both clocks.
    // The refresh expiry defaults to alive, so a test that says nothing about it is asking about the
    // access token alone.
    private static string Credentials(long expiresAt, long? refreshTokenExpiresAt = null)
        => """
            {"claudeAiOauth":{"accessToken":"sk-ant-oat01-EXAMPLE","refreshToken":"sk-ant-ort01-EXAMPLE",
             "expiresAt":EXPIRES,"refreshTokenExpiresAt":REFRESH,"subscriptionType":"team",
             "rateLimitTier":"default_claude_max_5x"}}
            """
            .Replace("EXPIRES", expiresAt.ToString(CultureInfo.InvariantCulture))
            .Replace(
                "REFRESH",
                (refreshTokenExpiresAt ?? Now.AddDays(21).ToUnixTimeMilliseconds())
                    .ToString(CultureInfo.InvariantCulture));
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

        new CodexUsageDialect().Token(credentials, Now).Should().Be(UsageToken.Present("eyJACCESS"));
    }

    [Fact]
    public void An_unreadable_credentials_file_yields_no_token()
    {
        new CodexUsageDialect().Token("not json", Now).Should().Be(UsageToken.Missing);
    }

    [Fact]
    public void The_credentials_path_is_resolved_from_the_codex_home()
    {
        new CodexUsageDialect().DefaultCredentialsPath().Should().EndWith("auth.json");
    }
}
