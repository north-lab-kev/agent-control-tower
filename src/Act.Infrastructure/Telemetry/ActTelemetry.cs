using Act.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PostHog;
using PostHog.Config;

namespace Act.Infrastructure.Telemetry;

// THE ONLY FOLDER THAT NAMES THE TELEMETRY VENDOR (a test enforces it), on the same rule `Logging/`
// holds for its log sink: the vendor is a detail behind `ITelemetrySink`, and swapping it should be
// one folder rather than a search.
//
// The sink is *composed by the caller* rather than registered here, because consent belongs to the
// app — see `ConsentedTelemetrySink`. What this owns is the client and the second of the two gates.
public static class ActTelemetry
{
    // `consent` is a *factory*: it resolves whatever answers the question once, while the container is
    // alive, and hands back a predicate holding that object. It must not be a delegate that resolves
    // per call.
    //
    // The gate below runs on the client's own send path, and the last send of a run happens while the
    // app is shutting down — by which point the provider it was resolved from may already be disposed.
    // Resolving there threw `ObjectDisposedException` out of the flush (2026-08-05), taking the
    // closing batch with it; `The_consent_gate_survives_the_container_it_was_built_from` pins the fix.
    public static IServiceCollection AddActTelemetryClient(
        this IServiceCollection services,
        TelemetryOptions options,
        Func<IServiceProvider, Func<bool>> consent)
    {
        if (!options.Configured || options.HostUri is not { } host || options.Token is not { } token)
            return services;

        services.AddPostHog();

        services.AddSingleton<IConfigureOptions<PostHogOptions>>(provider =>
            new ConfigureOptions<PostHogOptions>(
                posthog => Apply(posthog, options, host, token, consent(provider))));

        return services;
    }

    public static ITelemetrySink Transport(IServiceProvider provider, TelemetryOptions options, string installId)
        => options.Configured
            ? new PostHogTelemetrySink(
                provider.GetRequiredService<IPostHogClient>(),
                installId,
                provider.GetRequiredService<ILogger<PostHogTelemetrySink>>())
            : new NullTelemetrySink();

    private static void Apply(
        PostHogOptions posthog,
        TelemetryOptions options,
        Uri host,
        string token,
        Func<bool> consented)
    {
        posthog.ProjectToken = token;
        posthog.HostUrl = host;
        posthog.FlushAt = options.FlushBatch;
        posthog.FlushInterval = options.FlushInterval;

        // The back gate, and the reason turning the switch off is immediate rather than eventual:
        // `ConsentedTelemetrySink` stops new events being queued, but the client may already be
        // holding a batch, and its own timer would send it. PostHog drops an event whose `BeforeSend`
        // returns null, so this is what makes "off means nothing leaves" true of the queue as well.
        // The `null!` is that contract — see `PostHogClient.CaptureBatchAsync`.
        //
        // Failing closed rather than letting anything escape: this runs inside the client's batch
        // send, where a throw both loses the batch and is reported from a stack no call site owns. A
        // consent check that cannot answer must mean "do not send", never "send anyway".
        posthog.BeforeSend = captured =>
        {
            try
            {
                return consented() ? captured : null!;
            }
            catch
            {
                return null!;
            }
        };
    }
}
