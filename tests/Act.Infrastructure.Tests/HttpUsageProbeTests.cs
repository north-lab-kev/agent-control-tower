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
