using Act.Core.Abstractions;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.Agents.Tests;

// One suite every adapter must pass. It deliberately exercises only the surface that needs
// no process — identity, capabilities, config resolution — because ACT does not run real
// CLIs in automated tests; live-session behavior is verified per roadmap step by hand.
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
            capabilities.Models.Should().Contain(model);
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
            var resolution = adapter.Resolve(ConfigFor(adapter, model: model));

            resolution.CanLaunch.Should().BeTrue($"{model} is one of {adapter.Agent}'s own models");
            resolution.Resolved.Model.Should().Be(model);
            resolution.Adjustments.Should().NotContain(
                adjustment => adjustment.Field == nameof(LaunchConfig.Model));
        }
    }

    [Fact]
    public void Every_effort_it_offers_resolves_untouched()
    {
        var adapter = CreateAdapter();

        foreach (var effort in adapter.Capabilities.Efforts)
        {
            var resolution = adapter.Resolve(ConfigFor(adapter, effort: effort));

            resolution.CanLaunch.Should().BeTrue($"{effort} is one of {adapter.Agent}'s own efforts");
            resolution.Resolved.Effort.Should().Be(effort);
            resolution.Adjustments.Should().NotContain(
                adjustment => adjustment.Field == nameof(LaunchConfig.Effort));
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

        // An empty effort list is how an adapter says it does not model effort at all, in which
        // case handing the value through untouched is the honest outcome.
        if (adapter.Capabilities.Efforts.Count == 0)
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
