using Act.App.Hosting;
using AwesomeAssertions;

namespace Act.App.UiTests;

// One reader, because the updater made a disagreement matter: the string ACT shows and the string
// electron-updater compares against a release tag have to be the same string.
public class AppVersionTests
{
    [Theory]
    [InlineData("1.2.3+abc1234", "1.2.3")]
    [InlineData("1.2.3-beta.1+abc1234", "1.2.3-beta.1")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3-beta.1", "1.2.3-beta.1")]
    public void The_build_metadata_is_cut_off(string informational, string expected)
        => AppVersion.Trim(informational).Should().Be(expected);

    // A pre-release suffix is not metadata and must survive: it is the difference between the
    // version that stays put and the version that rolls forward.
    [Fact]
    public void A_prerelease_suffix_is_kept()
        => AppVersion.Trim("0.1.0-beta.1+deadbee").Should().Be("0.1.0-beta.1");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_to_read_is_null_rather_than_an_empty_version(string? informational)
        => AppVersion.Trim(informational).Should().BeNull();

    // Degenerate, but it must not throw: a version that is only metadata would otherwise index
    // past nothing.
    [Fact]
    public void A_version_that_is_only_metadata_trims_to_empty()
        => AppVersion.Trim("+abc1234").Should().BeEmpty();

    // Read off `Act.App` rather than the entry assembly, which under a test host is the test host.
    [Fact]
    public void The_current_version_is_this_assembly_and_carries_no_metadata()
    {
        AppVersion.Current.Should().NotBeNullOrWhiteSpace().And.NotContain("+");
        AppVersion.Full.Should().NotBeNullOrWhiteSpace();
        AppVersion.Current.Should().NotBe(AppVersion.Unknown);
    }
}
