using Act.App.Desktop;
using AwesomeAssertions;
using Microsoft.JSInterop;

namespace Act.App.UiTests;

// The browser-mode fallback. Worth pinning because its whole job is to be *silent* rather than
// correct: a fallback that throws turns "this mode does not have that" into a bug report, and the
// pump calls every one of these before it learns it should not have.
public class BrowserUpdaterTests
{
    private readonly BrowserUpdater updater = new();

    [Fact]
    public void There_is_no_installer_to_replace()
    {
        updater.IsSupported.Should().BeFalse();
        updater.IsReady.Should().BeFalse();
    }

    [Fact]
    public async Task Every_call_answers_rather_than_throws()
    {
        var call = async () =>
        {
            updater.Configure();
            updater.InstallAndRestart();

            (await updater.CheckAsync(CancellationToken.None)).Should().Be(UpdateCheck.Failed);
            (await updater.DownloadAsync(new Progress<int>(), CancellationToken.None)).Should().BeFalse();
        };

        await call.Should().NotThrowAsync();
    }

    // `Failed` rather than `UpToDate`: a browser tab has not established that this is the newest
    // release, it has established that it is not in a position to say.
    [Fact]
    public async Task A_check_reports_that_it_could_not_be_made()
    {
        var check = await updater.CheckAsync(CancellationToken.None);

        check.Completed.Should().BeFalse();
        check.Version.Should().BeNull();
    }

    [Fact]
    public void Installing_never_makes_it_ready()
    {
        updater.InstallAndRestart();

        updater.IsReady.Should().BeFalse();
    }
}

// The other half of the browser-mode fallback. The button that calls it is not rendered here — see
// `MainLayoutTests` — so this exists to keep that a layout decision rather than a crash if it ever
// stops being one.
public class BrowserDesktopBridgeTests
{
    [Fact]
    public async Task Restarting_for_an_update_does_nothing_rather_than_throwing()
    {
        var bridge = new BrowserDesktopBridge(new NoJs());

        bridge.IsDesktop.Should().BeFalse();

        var restart = async () => await bridge.RestartForUpdateAsync();

        await restart.Should().NotThrowAsync();
    }

    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new NotSupportedException("no interop in this test");

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
            => throw new NotSupportedException("no interop in this test");
    }
}
