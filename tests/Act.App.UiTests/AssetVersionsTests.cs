using System.Reflection;
using Act.App;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Act.App.UiTests;

// One token per assembly for the whole process. The stability is the point rather than the value: a
// differing `href` between the prerendered markup and the interactive re-render makes Blazor patch
// the stylesheet links, and the browser repaints unstyled while it refetches them.
public class AssetVersionsTests
{
    private static readonly Assembly App = typeof(AssetVersions).Assembly;

    private static readonly Assembly Other = typeof(AssetVersionsTests).Assembly;

    [Fact]
    public void A_token_is_a_query_string_ready_to_append()
        => Versions("Production").For(App).Should().StartWith("?v=");

    // The invariant the class exists for. Asked twice inside one process, the answer has to be the
    // same string — in development it is a fresh guid, so a per-call token would differ every time.
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void The_same_assembly_gets_the_same_token_for_the_life_of_the_process(string environment)
    {
        var versions = Versions(environment);

        versions.For(App).Should().Be(versions.For(App));
    }

    [Fact]
    public void A_release_build_is_versioned_by_the_assembly_rather_than_by_the_run()
    {
        var versions = Versions("Production");

        versions.For(App).Should().Be($"?v={App.GetName().Version}");
    }

    // A fresh token each run still defeats the cache after a rebuild in development, which is the
    // whole reason the two environments answer differently.
    [Fact]
    public void A_development_build_gets_a_token_that_is_new_every_run()
    {
        var first = Versions("Development").For(App);
        var second = Versions("Development").For(App);

        first.Should().NotBe(second);
        first.Should().NotBe($"?v={App.GetName().Version}");
    }

    [Fact]
    public void Each_assembly_is_versioned_on_its_own()
    {
        var versions = Versions("Development");

        versions.For(App).Should().NotBe(versions.For(Other));
    }

    private static IAssetVersions Versions(string environment)
        => new AssetVersions(new StubEnvironment(environment));

    private sealed class StubEnvironment(string environment) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environment;

        public string ApplicationName { get; set; } = "Act.App";

        public string WebRootPath { get; set; } = "/app/wwwroot";

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = "/app";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
