using System.Net;
using System.Net.Http.Headers;
using Act.Core.Abstractions;
using Act.Core.Model;
using Microsoft.Extensions.Logging;

namespace Act.Infrastructure.Usage;

public sealed class HttpUsageProbe(
    IUsageDialect dialect,
    Func<HttpClient> clients,
    ITextFileReader files,
    IClock clock,
    UsageOptions options,
    ILogger<HttpUsageProbe> log) : IUsageProbe
{
    public const string ClientName = "act-usage";

    public AgentType Agent => dialect.Agent;

    public async Task<UsageProbeResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
            return Unavailable(UsageAvailability.Off);

        var configured = options.For(Agent);
        var path = Fallback(configured.CredentialsPath, dialect.DefaultCredentialsPath());

        if (files.Read(path) is not { } credentials)
        {
            log.LogDebug("No {Agent} credentials at {Path}; usage unavailable.", Agent, path);

            return Unavailable(UsageAvailability.NotSignedIn);
        }

        var token = dialect.Token(credentials, clock.Now);

        if (token.State is UsageTokenState.Lapsed)
        {
            log.LogDebug(
                "The {Agent} refresh token has lapsed as well; only an interactive sign-in renews it.",
                Agent);

            return Unavailable(UsageAvailability.SignInRequired);
        }

        if (token.State is UsageTokenState.Expired)
        {
            log.LogDebug("The {Agent} access token has expired; usage unavailable.", Agent);

            return Unavailable(UsageAvailability.Expired);
        }

        if (token is not { State: UsageTokenState.Present, Value: { Length: > 0 } bearer })
        {
            log.LogDebug("No usable {Agent} access token; usage unavailable.", Agent);

            return Unavailable(UsageAvailability.NotSignedIn);
        }

        return await RequestAsync(Fallback(configured.Endpoint, dialect.DefaultEndpoint), bearer, cancellationToken);
    }

    private async Task<UsageProbeResult> RequestAsync(
        string endpoint,
        string token,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = clients();
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                log.LogDebug("{Agent} usage endpoint answered {Status}.", Agent, (int)response.StatusCode);

                return Unavailable(Refused(response.StatusCode)
                    ? UsageAvailability.Unauthorized
                    : UsageAvailability.Failed);
            }

            if (dialect.Parse(await response.Content.ReadAsStringAsync(cancellationToken), clock.Now) is not { } usage)
            {
                log.LogDebug("The {Agent} usage response carried no window ACT could read.", Agent);

                return Unavailable(UsageAvailability.Failed);
            }

            return UsageProbeResult.Of(usage);
        }
        catch (HttpRequestException error)
        {
            log.LogDebug(error, "{Agent} usage request failed.", Agent);

            return Unavailable(UsageAvailability.Unreachable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            log.LogDebug("{Agent} usage request timed out.", Agent);

            return Unavailable(UsageAvailability.Unreachable);
        }
    }

    private static bool Refused(HttpStatusCode status)
        => status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    private UsageProbeResult Unavailable(UsageAvailability availability)
        => UsageProbeResult.Unavailable(Agent, availability, clock.Now);

    private static string Fallback(string? configured, string standard)
        => string.IsNullOrWhiteSpace(configured) ? standard : configured;
}
