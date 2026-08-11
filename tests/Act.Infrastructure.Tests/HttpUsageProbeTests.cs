using System.Net;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Infrastructure.Usage;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.Infrastructure.Tests;

public class HttpUsageProbeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_reading_is_available()
    {
        var result = await Probe(new Handler(HttpStatusCode.OK, "{}")).ReadAsync();

        result.Availability.Should().Be(UsageAvailability.Available);
        result.Usage.Should().NotBeNull();
        result.IsAvailable.Should().BeTrue();
        result.IsUnavailable.Should().BeFalse();
    }

    [Fact]
    public async Task A_missing_credentials_file_reports_not_signed_in()
    {
        var result = await Probe(new Handler(HttpStatusCode.OK, "{}"), credentials: null).ReadAsync();

        result.Availability.Should().Be(UsageAvailability.NotSignedIn);
        result.IsUnavailable.Should().BeTrue();
    }

    [Fact]
    public async Task A_credentials_file_with_no_token_reports_not_signed_in()
        => (await Probe(new Handler(HttpStatusCode.OK, "{}"), token: UsageToken.Missing).ReadAsync())
            .Availability.Should().Be(UsageAvailability.NotSignedIn);

    [Fact]
    public async Task An_expired_token_is_reported_without_spending_a_request()
    {
        var handler = new Handler(HttpStatusCode.OK, "{}");

        var result = await Probe(handler, token: UsageToken.Expired).ReadAsync();

        result.Availability.Should().Be(UsageAvailability.Expired);
        handler.Calls.Should().Be(0);
    }

    // Told apart from `Expired` because the remedy differs: one renews itself once the CLI runs, the
    // other needs the user to sign in. Neither spends a request, since both are read off the file.
    [Fact]
    public async Task A_lapsed_refresh_token_asks_for_a_new_sign_in_without_spending_a_request()
    {
        var handler = new Handler(HttpStatusCode.OK, "{}");

        var result = await Probe(handler, token: UsageToken.Lapsed).ReadAsync();

        result.Availability.Should().Be(UsageAvailability.SignInRequired);
        handler.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_refused_token_is_told_apart_from_any_other_failure(HttpStatusCode status)
        => (await Probe(new Handler(status, "nope")).ReadAsync())
            .Availability.Should().Be(UsageAvailability.Unauthorized);

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Any_other_status_is_a_plain_failure(HttpStatusCode status)
        => (await Probe(new Handler(status, "nope")).ReadAsync())
            .Availability.Should().Be(UsageAvailability.Failed);

    [Fact]
    public async Task A_body_the_dialect_cannot_read_is_a_failure()
        => (await Probe(new Handler(HttpStatusCode.OK, "{}"), parses: false).ReadAsync())
            .Availability.Should().Be(UsageAvailability.Failed);

    [Fact]
    public async Task A_network_error_reports_unreachable()
        => (await Probe(new Handler(new HttpRequestException("no route"))).ReadAsync())
            .Availability.Should().Be(UsageAvailability.Unreachable);

    [Fact]
    public async Task A_timeout_reports_unreachable()
        => (await Probe(new Handler(new TaskCanceledException("timed out"))).ReadAsync())
            .Availability.Should().Be(UsageAvailability.Unreachable);

    // The endpoint override is a hand-edited appsettings value, and a scheme-less or malformed one
    // throws out of `SendAsync` in types the probe's catches do not name — which would end the usage
    // pump's polling loop for the life of the process. A config mistake has to read as "usage
    // unavailable", not as a dead pump.
    [Theory]
    [InlineData("api.anthropic.com/usage")]
    [InlineData("not a url at all")]
    [InlineData("file:///c:/somewhere.json")]
    public async Task A_misconfigured_endpoint_is_a_failure_rather_than_a_thrown_request(string endpoint)
    {
        var handler = new Handler(HttpStatusCode.OK, "{}");
        var options = new UsageOptions
        {
            Agents = new Dictionary<string, UsageAgentOptions>
            {
                ["ClaudeCode"] = new() { Endpoint = endpoint },
            },
        };

        var result = await Probe(handler, options: options).ReadAsync();

        result.Availability.Should().Be(UsageAvailability.Failed);
        handler.Calls.Should().Be(0, "a request that cannot be built must not be attempted");
    }

    [Fact]
    public async Task Usage_switched_off_in_configuration_reports_off()
    {
        var handler = new Handler(HttpStatusCode.OK, "{}");
        var options = new UsageOptions { Enabled = false };

        var result = await Probe(handler, options: options).ReadAsync();

        result.Availability.Should().Be(UsageAvailability.Off);
        result.IsUnavailable.Should().BeFalse();
        handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Every_unavailable_result_is_stamped_with_when_it_was_checked()
        => (await Probe(new Handler(HttpStatusCode.Unauthorized, "nope")).ReadAsync())
            .At.Should().Be(Now);

    [Fact]
    public void The_credentials_path_is_the_dialects_discovered_default()
        => Probe(new Handler(HttpStatusCode.OK, "{}")).CredentialsPath.Should().Be("credentials.json");

    [Theory]
    [InlineData(@"D:\elsewhere\auth.json", @"D:\elsewhere\auth.json")]
    [InlineData("", "credentials.json")]
    [InlineData("   ", "credentials.json")]
    public void A_configured_credentials_path_wins_and_a_blank_one_configures_nothing(
        string configured,
        string expected)
    {
        var options = new UsageOptions
        {
            Agents = new Dictionary<string, UsageAgentOptions>
            {
                ["ClaudeCode"] = new() { CredentialsPath = configured },
            },
        };

        Probe(new Handler(HttpStatusCode.OK, "{}"), options: options).CredentialsPath.Should().Be(expected);
    }

    private static HttpUsageProbe Probe(
        Handler handler,
        string? credentials = "{}",
        UsageToken? token = null,
        bool parses = true,
        UsageOptions? options = null)
        => new(
            new Dialect(token ?? UsageToken.Present("bearer"), parses),
            () => new HttpClient(handler),
            new Files(credentials),
            new FrozenClock(),
            options ?? new UsageOptions(),
            NullLogger<HttpUsageProbe>.Instance);

    private sealed class FrozenClock : IClock
    {
        public DateTimeOffset Now => HttpUsageProbeTests.Now;
    }

    private sealed class Files(string? credentials) : ITextFileReader
    {
        public string? Read(string path) => credentials;
    }

    private sealed class Dialect(UsageToken token, bool parses) : IUsageDialect
    {
        public AgentType Agent => AgentType.ClaudeCode;

        public string DefaultEndpoint => "https://example.invalid/usage";

        public string DefaultCredentialsPath() => "credentials.json";

        public UsageToken Token(string credentials, DateTimeOffset now) => token;

        public AgentUsage? Parse(string response, DateTimeOffset takenAt)
            => parses
                ? new AgentUsage(
                    Agent,
                    [new UsageWindow(UsageWindowKind.Session, 12, takenAt.AddHours(3))],
                    takenAt,
                    false,
                    null)
                : null;
    }

    private sealed class Handler : HttpMessageHandler
    {
        private readonly HttpStatusCode status;

        private readonly string body;

        private readonly Exception? failure;

        public Handler(HttpStatusCode status, string body)
        {
            this.status = status;
            this.body = body;
        }

        public Handler(Exception failure)
        {
            this.failure = failure;
            status = HttpStatusCode.OK;
            body = string.Empty;
        }

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;

            if (failure is not null)
                return Task.FromException<HttpResponseMessage>(failure);

            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
