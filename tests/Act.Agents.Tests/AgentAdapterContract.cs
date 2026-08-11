using Act.Core.Abstractions;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.Agents.Tests;

// One suite every adapter must pass. It deliberately exercises only the surface that needs
// no process — identity, capabilities, config resolution — because ACT does not run real
// CLIs in automated tests; live-session behavior is verified by hand against the pinned CLIs.
public abstract class AgentAdapterContract
{
    protected abstract IAgentAdapter CreateAdapter();

    [Fact]
    public void It_offers_at_least_one_model()
        => CreateAdapter().Capabilities.Models.Should().NotBeEmpty();

    [Fact]
    public void Its_default_model_is_one_of_the_models_it_offers()
    {
        var capabilities = CreateAdapter().Capabilities;

        if (capabilities.DefaultModel is { } model)
            capabilities.Model(model).Should().NotBeNull();
    }

    [Fact]
    public void Every_model_it_offers_is_usably_described()
    {
        foreach (var model in CreateAdapter().Capabilities.Models)
        {
            model.Slug.Should().NotBeNullOrWhiteSpace();
            model.DisplayName.Should().NotBeNullOrWhiteSpace();

            // A default effort that is not on the model's own ladder would be substituted the
            // moment anyone accepted it, which makes it a bug rather than a default.
            if (model.DefaultEffort is { } effort)
                model.Efforts.Should().Contain(effort);
        }
    }

    // The model ACT spends its own tokens on has to be one the agent actually offers, for the same
    // reason the default does — and it must resolve, because `QueryAsync` builds a command line from
    // it with no resolver in the way to catch a typo.
    [Fact]
    public void Its_utility_model_is_one_of_the_models_it_offers()
    {
        var capabilities = CreateAdapter().Capabilities;

        if (capabilities.UtilityModel is not { } model)
            return;

        capabilities.Model(model).Should().NotBeNull();
        capabilities.Utility!.Slug.Should().Be(model);
    }

    [Fact]
    public void It_honours_at_least_one_permission_mode()
        => CreateAdapter().Capabilities.PermissionModes.Should().NotBeEmpty();

    [Fact]
    public void Every_model_it_offers_resolves_untouched()
    {
        var adapter = CreateAdapter();

        foreach (var model in adapter.Capabilities.Models)
        {
            var resolution = adapter.Resolve(ConfigFor(adapter, model: model.Slug));

            resolution.CanLaunch.Should().BeTrue($"{model.Slug} is one of {adapter.Agent}'s own models");
            resolution.Resolved.Model.Should().Be(model.Slug);
            resolution.Adjustments.Should().NotContain(
                adjustment => adjustment.Field == nameof(LaunchConfig.Model));
        }
    }

    // Per model, not per agent: Codex gives each model a different ladder, so an effort that is
    // valid on one of an agent's models can be invalid on another.
    [Fact]
    public void Every_effort_a_model_offers_resolves_untouched_on_that_model()
    {
        var adapter = CreateAdapter();

        foreach (var model in adapter.Capabilities.Models)
        {
            foreach (var effort in model.Efforts)
            {
                var resolution = adapter.Resolve(ConfigFor(adapter, model: model.Slug, effort: effort));

                resolution.CanLaunch.Should().BeTrue($"{effort} is on {model.Slug}'s own ladder");
                resolution.Resolved.Effort.Should().Be(effort);
                resolution.Adjustments.Should().NotContain(
                    adjustment => adjustment.Field == nameof(LaunchConfig.Effort));
            }
        }
    }

    // The reason the per-model shape exists at all: an effort one model accepts must not be
    // waved through on a model whose ladder stops short of it.
    [Fact]
    public void An_effort_from_another_models_ladder_is_never_silently_dropped()
    {
        var adapter = CreateAdapter();
        var ladders = adapter.Capabilities.Models;

        foreach (var model in ladders)
        {
            var foreign = ladders
                .SelectMany(other => other.Efforts)
                .FirstOrDefault(effort => !model.Efforts.Contains(effort));

            if (foreign is null)
                continue;

            var resolution = adapter.Resolve(ConfigFor(adapter, model: model.Slug, effort: foreign));

            AssertSubstitutedOrRejected(resolution, nameof(LaunchConfig.Effort), foreign);
        }
    }

    [Fact]
    public void An_omitted_model_resolves_to_the_default()
    {
        var adapter = CreateAdapter();

        var resolution = adapter.Resolve(ConfigFor(adapter));

        resolution.CanLaunch.Should().BeTrue();
        resolution.Resolved.Model.Should().Be(adapter.Capabilities.DefaultModel);
    }

    [Fact]
    public void An_unknown_model_is_never_silently_dropped()
    {
        var adapter = CreateAdapter();

        var resolution = adapter.Resolve(ConfigFor(adapter, model: "no-such-model"));

        AssertSubstitutedOrRejected(resolution, nameof(LaunchConfig.Model), "no-such-model");
    }

    [Fact]
    public void An_unknown_effort_is_never_silently_dropped()
    {
        var adapter = CreateAdapter();

        var resolution = adapter.Resolve(ConfigFor(adapter, effort: "no-such-effort"));

        // An empty effort list is how a model says it does not model effort at all, in which
        // case handing the value through untouched is the honest outcome.
        if (adapter.Capabilities.EffortsFor(adapter.Capabilities.DefaultModel).Count == 0)
        {
            resolution.Resolved.Effort.Should().Be("no-such-effort");

            return;
        }

        AssertSubstitutedOrRejected(resolution, nameof(LaunchConfig.Effort), "no-such-effort");
    }

    [Fact]
    public void Every_permission_mode_is_either_honoured_or_rejected()
    {
        var adapter = CreateAdapter();

        foreach (var mode in Enum.GetValues<PermissionMode>())
        {
            var resolution = adapter.Resolve(new LaunchConfig { PermissionMode = mode });

            if (adapter.Capabilities.PermissionModes.Contains(mode))
                resolution.CanLaunch.Should().BeTrue($"{adapter.Agent} declares it honours {mode}");
            else
                resolution.Rejections.Should().NotBeEmpty($"{adapter.Agent} cannot honour {mode}");
        }
    }

    // The desktop handoff is optional per agent, so the contract holds it to the only rule
    // that must be true either way: an adapter that declares it produces a usable url for
    // the session, and one that does not never produces a url at all.
    [Fact]
    public void The_desktop_handoff_matches_what_the_adapter_declares()
    {
        var adapter = CreateAdapter();
        const string sessionId = "6f0d5d5c-0000-4a2c-9f4d-2f0a3f7c1e11";

        var url = adapter.DesktopHandoffUrl(sessionId, "C:/repo");

        if (!adapter.Capabilities.DesktopHandoff)
        {
            url.Should().BeNull($"{adapter.Agent} declares it has no desktop app");

            return;
        }

        url.Should().NotBeNullOrWhiteSpace();
        url.Should().Contain(sessionId, "the handoff has to name the session it opens");
        Uri.IsWellFormedUriString(url, UriKind.Absolute).Should().BeTrue();
    }

    [Fact]
    public void Resolving_leaves_the_requested_config_alone()
    {
        var adapter = CreateAdapter();
        var requested = ConfigFor(adapter, model: "no-such-model", effort: "no-such-effort");

        adapter.Resolve(requested);

        requested.Model.Should().Be("no-such-model");
        requested.Effort.Should().Be("no-such-effort");
    }

    private static void AssertSubstitutedOrRejected(
        LaunchConfigResolution resolution,
        string field,
        string requested)
    {
        if (!resolution.CanLaunch)
        {
            resolution.Rejections.Should().NotBeEmpty();
            resolution.Rejections.Should().AllSatisfy(
                rejection => rejection.Should().NotBeNullOrWhiteSpace());

            return;
        }

        resolution.Adjustments.Should().Contain(adjustment => adjustment.Field == field);
        resolution.Adjustments
            .Where(adjustment => adjustment.Field == field)
            .Should().AllSatisfy(adjustment =>
            {
                adjustment.Requested.Should().Be(requested);
                adjustment.Substituted.Should().NotBe(requested);
                adjustment.Reason.Should().NotBeNullOrWhiteSpace();
            });
    }

    private static LaunchConfig ConfigFor(IAgentAdapter adapter, string? model = null, string? effort = null)
        => new()
        {
            Model = model,
            Effort = effort,
            PermissionMode = adapter.Capabilities.PermissionModes.First(),
        };
}
