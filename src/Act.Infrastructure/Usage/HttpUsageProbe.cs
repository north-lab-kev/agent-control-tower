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

    public async Task<AgentUsage?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var configured = options.For(Agent);

        if (!options.Enabled || !configured.Enabled)
            return null;

        var path = Fallback(configured.CredentialsPath, dialect.DefaultCredentialsPath());

        if (files.Read(path) is not { } credentials)
        {
            log.LogDebug("No {Agent} credentials at {Path}; usage unavailable.", Agent, path);

            return null;
        }

        if (dialect.Token(credentials, clock.Now) is not { Length: > 0 } token)
        {
            log.LogDebug("No usable {Agent} access token; usage unavailable.", Agent);

            return null;
        }

        return await RequestAsync(Fallback(configured.Endpoint, dialect.DefaultEndpoint), token, cancellationToken);
    }

    private async Task<AgentUsage?> RequestAsync(string endpoint, string token, CancellationToken cancellationToken)
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

                return null;
            }

            return dialect.Parse(await response.Content.ReadAsStringAsync(cancellationToken), clock.Now);
        }
        catch (HttpRequestException error)
        {
            log.LogDebug(error, "{Agent} usage request failed.", Agent);

            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            log.LogDebug("{Agent} usage request timed out.", Agent);

            return null;
        }
    }

    private static string Fallback(string? configured, string standard)
        => string.IsNullOrWhiteSpace(configured) ? standard : configured;
}
