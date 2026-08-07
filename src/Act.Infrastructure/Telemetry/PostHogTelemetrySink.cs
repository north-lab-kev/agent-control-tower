using Act.Core.Abstractions;
using Act.Core.Telemetry;
using Microsoft.Extensions.Logging;
using PostHog;

namespace Act.Infrastructure.Telemetry;

public sealed class PostHogTelemetrySink(
    IPostHogClient client,
    string installId,
    ILogger<PostHogTelemetrySink> log) : ITelemetrySink
{
    public void Capture(TelemetryEvent telemetry)
    {
        if (TelemetryPayload.Sanitize(telemetry) is not { } allowed)
        {
            log.LogWarning("Telemetry event {Event} is not declared; nothing was sent.", telemetry.Name);

            return;
        }

        // Never a reason to disturb the caller: every call site is a launch, a sign-off or a startup
        // step, and none of them should fail because a metric could not be queued.
        //
        // A warning rather than debug because the shipped log level is Information, so debug would
        // make this invisible on an install — and rather than an error because nothing the user is
        // doing has failed. Note that a request that could not be sent never reaches here: the client
        // owns the batch and logs its own HTTP failures, so this is a full queue or a disposed client.
        try
        {
            client.Capture(installId, allowed.Name, Properties(allowed));
        }
        catch (Exception error)
        {
            log.LogWarning(error, "Capturing {Event} failed; the event was dropped.", allowed.Name);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await client.FlushAsync();
        }
        catch (Exception error)
        {
            log.LogWarning(error, "Flushing telemetry failed; the queued events were dropped.");
        }
    }

    // `Sanitize` has already dropped every null, so the values are known to be non-null here.
    private static Dictionary<string, object> Properties(TelemetryEvent telemetry)
        => telemetry.Properties.ToDictionary(
            property => property.Key,
            property => property.Value!,
            StringComparer.Ordinal);
}
