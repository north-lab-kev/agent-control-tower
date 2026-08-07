using Act.App.Components.Pages;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;
using Act.Core.Scheduling;
using Act.Core.Spawning;
using AwesomeAssertions;
using Bunit;

namespace Act.App.UiTests;

// Every setting applies and persists the moment it changes, so there is no Save and no Cancel — which makes
// "the switch wrote through" the whole contract of the page, and a switch that only moved on screen the
// failure it has to rule out. The other half is the agent sections: what they say about an install is the one
// thing a user reads when a launch fails with "not found".
public class SettingsViewTests : ComponentTest
{
    private const string Row = RadzenDom.SettingsRow;

    [Fact]
    public void There_is_nothing_to_save_and_nothing_to_cancel()
    {
        var buttons = Show().FindAll("button").Select(RadzenDom.ButtonText).ToList();

        buttons.Should().NotContain("Save").And.NotContain("Cancel");
    }

    [Theory]
    [InlineData("Keep the computer awake")]
    [InlineData("Blink cards in Your turn")]
    [InlineData("Pause automatic execution")]
    [InlineData("One task at a time per folder")]
    [InlineData("Archive completed tasks automatically")]
    [InlineData("Help improve ACT")]
    public void Every_switch_writes_through_the_moment_it_moves(string label)
    {
        var cut = Show();

        var before = RadzenDom.IsOn(cut, label, Row);

        RadzenDom.Toggle(cut, label, Row);

        RadzenDom.IsOn(cut, label, Row).Should().Be(!before);
        SettingsStore.Load().Should().NotBeNull();
        Show().Pipe(fresh => RadzenDom.IsOn(fresh, label, Row).Should().Be(!before));
    }

    [Fact]
    public void The_theme_persists()
    {
        var cut = Show();

        RadzenDom.Choose(cut, "Theme", "Dark", Row);

        Settings.Theme.Should().Be(ThemePreference.Dark);
    }

    [Fact]
    public void The_display_mode_persists()
    {
        var cut = Show();

        RadzenDom.Choose(cut, "Display mode", "Compact", Row);

        Settings.Density.Should().Be(BoardDensity.Compact);
    }

    [Fact]
    public void The_concurrency_cap_persists()
    {
        var cut = Show();

        RadzenDom.Numeric(cut, "Tasks running at once", Row).Change("3");

        Settings.MaxConcurrent.Should().Be(3);
    }

    // Held between the rule's own bounds rather than by the box, so a value typed past them still lands
    // inside — `ConcurrencySlots` owns the ceiling and the page only renders it.
    [Fact]
    public void The_concurrency_cap_is_held_inside_its_bounds()
    {
        var box = RadzenDom.Numeric(Show(), "Tasks running at once", Row);

        box.GetAttribute("aria-valuemin").Should().Be(ConcurrencySlots.Minimum.ToString());
        box.GetAttribute("aria-valuemax").Should().Be(ConcurrencySlots.Maximum.ToString());
    }

    [Fact]
    public void The_usage_ceiling_persists()
    {
        var cut = Show();

        RadzenDom.Numeric(cut, "Hold the queue at", Row).Change("80");

        Settings.UsageCeilingPercent.Should().Be(80);
    }

    [Fact]
    public void The_follow_up_cap_persists()
    {
        var cut = Show();

        RadzenDom.Numeric(cut, "Follow-ups an agent may create per task", Row).Change("40");

        Settings.MaxFollowUpsPerCard.Should().Be(40);
    }

    // `SpawnQuota` owns the bounds and the page only renders them — zero included, because zero is
    // the legitimate "no agent-spawned work on this board" answer.
    [Fact]
    public void The_follow_up_cap_is_held_inside_the_quota_bounds()
    {
        var box = RadzenDom.Numeric(Show(), "Follow-ups an agent may create per task", Row);

        box.GetAttribute("aria-valuemin").Should().Be(SpawnQuota.Minimum.ToString());
        box.GetAttribute("aria-valuemax").Should().Be(SpawnQuota.Maximum.ToString());
    }

    // The day box only exists while the sweep does: a retention window for a sweep nobody runs is a number
    // that governs nothing.
    [Fact]
    public void The_retention_window_shows_only_while_the_sweep_is_on()
    {
        var cut = Show();

        RadzenDom.HasRow(cut, "Archive after", Row).Should().BeTrue();

        RadzenDom.Toggle(cut, "Archive completed tasks automatically", Row);

        RadzenDom.HasRow(cut, "Archive after", Row).Should().BeFalse();
    }

    [Fact]
    public void The_retention_window_is_held_inside_the_rule_bounds()
    {
        var box = RadzenDom.Numeric(Show(), "Archive after", Row);

        box.GetAttribute("aria-valuemin").Should().Be(CompletedRetention.MinimumDays.ToString());
        box.GetAttribute("aria-valuemax").Should().Be(CompletedRetention.MaximumDays.ToString());
    }

    // A language change has to re-run the whole render tree under the new culture, so it reloads.
    [Fact]
    public void A_language_change_persists_and_reloads()
    {
        var cut = Show();

        RadzenDom.Choose(cut, "Language", "Français", Row);

        Settings.Language.Should().Be(LanguagePreference.French);
    }

    [Fact]
    public void Choosing_the_language_it_already_has_changes_nothing()
    {
        var cut = Show();

        RadzenDom.Choose(cut, "Language", "System default", Row);

        Settings.Language.Should().Be(LanguagePreference.System);
    }

    // Desktop-only, both of them: a browser tab has no OS notification worth the permission prompt and no
    // window to close into a tray, and a switch that governs nothing is worse than no switch.
    [Fact]
    public void The_shell_only_switches_are_absent_in_a_browser()
    {
        var cut = Show();

        RadzenDom.HasRow(cut, "Desktop notifications", Row).Should().BeFalse();
        RadzenDom.HasRow(cut, "Close to the notification area", Row).Should().BeFalse();
    }

    [Fact]
    public void The_shell_only_switches_appear_under_the_desktop_shell()
    {
        Desktop.IsDesktop = true;

        var cut = Show();

        RadzenDom.HasRow(cut, "Desktop notifications", Row).Should().BeTrue();
        RadzenDom.HasRow(cut, "Close to the notification area", Row).Should().BeTrue();
    }

    // One section per registered adapter, in the order they were registered — the settings model holds a list
    // rather than a property per agent for the same reason.
    [Fact]
    public void There_is_a_section_per_registered_agent_in_registration_order()
    {
        var cut = Show();

        var names = cut.FindAll("h6.rz-text-subtitle2").Select(name => name.TextContent).ToList();

        names.Should().Equal("claude", "codex");
    }

    [Fact]
    public void An_agent_switch_writes_through()
    {
        var cut = Show();

        RadzenDom.Toggle(cut, "claude", Row);

        Settings.EnabledAgents.Should().Equal(AgentType.Codex);
    }

    // An empty box means "resolve the bare name on PATH", so what it reports has to be about `PATH` and
    // nothing else — the reassurance that hides a launch which will fail is the failure mode here.
    [Fact]
    public void An_agent_on_path_is_reported_as_found()
    {
        var cut = Show();

        cut.FindAll("div.binwarn").Should().BeEmpty();
        cut.FindAll("div.binok").Should().HaveCount(2);
    }

    [Fact]
    public void An_agent_that_is_installed_nowhere_says_so()
    {
        Claude.Install = AgentInstall.Missing;
        Codex.Install = AgentInstall.Missing;

        var cut = Show();

        cut.FindAll("div.binwarn").Should().HaveCount(2);
        cut.Find("div.binwarn").TextContent.Should().Contain("may not be installed");
    }

    // The fix for "installed but not on PATH" is one click, and making the user retype what ACT just found
    // would be perverse.
    [Fact]
    public void An_agent_installed_off_path_is_offered_the_path_that_was_found()
    {
        Claude.Install = AgentInstall.At("/opt/claude/claude");

        var cut = Show();

        var warning = cut.Find("div.binwarn");

        warning.TextContent.Should().Contain("/opt/claude/claude");
        RadzenDom.ButtonText(warning.QuerySelector("button")!).Should().Be("Use it");
    }

    [Fact]
    public void Adopting_the_found_path_writes_it_through()
    {
        Claude.Install = AgentInstall.At("/opt/claude/claude");
        Probe.Files.Add("/opt/claude/claude");

        var cut = Show();

        cut.Find("div.binwarn button").Click();

        Settings.Defaults(AgentType.ClaudeCode).Binary.Should().Be("/opt/claude/claude");
    }

    // An agent with nothing to adopt gets the warning and no button, because there is no path to offer.
    [Fact]
    public void An_agent_with_nothing_to_adopt_is_offered_nothing()
    {
        Claude.Install = AgentInstall.Missing;
        Codex.Install = AgentInstall.Missing;

        Show().FindAll("div.binwarn").Should().HaveCount(2).And.AllSatisfy(
            warning => warning.QuerySelectorAll("button").Should().BeEmpty());
    }

    [Fact]
    public void A_typed_path_that_is_not_there_is_reported()
    {
        var cut = Show();

        cut.FindAll("div.dirrow input")[0].Change("/nope/claude");

        Settings.Defaults(AgentType.ClaudeCode).Binary.Should().Be("/nope/claude");
        cut.Find("div.binwarn").TextContent.Should().Contain("Nothing is there");
    }

    [Fact]
    public void A_typed_path_that_exists_is_reported_as_found()
    {
        Probe.Files.Add("/opt/claude/claude");

        var cut = Show();

        cut.FindAll("div.dirrow input")[0].Change("/opt/claude/claude");

        cut.Find("div.binok").TextContent.Should().Contain("Found");
    }

    // One picker at a time, keyed by agent: two open at once would be two file trees on a settings page, and
    // only one of them can be the one you meant.
    [Fact]
    public void Only_one_binary_picker_is_open_at_a_time()
    {
        Directories.WithFiles("/usr/bin", [], "claude", "codex");

        var cut = Show();

        cut.FindAll("div.dirrow button")[0].Click();
        cut.FindAll("div.picker").Should().ContainSingle();

        cut.FindAll("div.dirrow button")[1].Click();
        cut.FindAll("div.picker").Should().ContainSingle();
    }

    [Fact]
    public void Clicking_the_browse_button_again_closes_the_picker()
    {
        Directories.WithFiles("/usr/bin", [], "claude");

        var cut = Show();

        cut.FindAll("div.dirrow button")[0].Click();
        cut.FindAll("div.dirrow button")[0].Click();

        cut.FindAll("div.picker").Should().BeEmpty();
    }

    [Fact]
    public void A_picked_binary_is_written_through()
    {
        Directories.Home = "/usr/bin";
        Directories.WithFiles("/usr/bin", [], "claude");
        Probe.Files.Add("/usr/bin/claude");

        var cut = Show();

        cut.FindAll("div.dirrow button")[0].Click();
        cut.Find("button.entry.file").Click();

        Settings.Defaults(AgentType.ClaudeCode).Binary.Should().Be("/usr/bin/claude");
        cut.FindAll("div.picker").Should().BeEmpty();
    }

    [Fact]
    public void Extra_flags_and_environment_are_written_through()
    {
        var cut = Show();

        // Re-found between the two writes: the first re-renders the section, which retires the handler the
        // second was about to use.
        cut.FindAll("textarea")[0].Change("--verbose");
        cut.FindAll("textarea")[1].Change("HTTP_PROXY=http://proxy");

        var defaults = Settings.Defaults(AgentType.ClaudeCode);

        defaults.ExtraFlags.Should().Equal("--verbose");
        defaults.Env.Should().ContainKey("HTTP_PROXY");
    }

    [Fact]
    public void The_diagnostics_section_states_the_install_id_and_the_log_folder()
    {
        var cut = Show();

        cut.Markup.Should().Contain(Settings.InstallId).And.Contain("/logs/act");
    }

    [Fact]
    public void Opening_the_log_folder_hands_it_to_the_shell()
    {
        var cut = Show();

        cut.FindAll("button").Single(button => RadzenDom.ButtonText(button) == "Open log folder").Click();

        Desktop.Opened.Should().Equal("/logs/act");
    }

    // The bridge *reports* a refusal instead of throwing it — a path the OS will not open is ordinary rather
    // than exceptional — and a click that appears to do nothing is the worst version of that.
    [Fact]
    public void A_log_folder_the_os_will_not_open_says_so()
    {
        Desktop.Refusal = "No application is associated with this path.";

        var cut = Show();

        cut.FindAll("div.binwarn").Should().BeEmpty();

        cut.FindAll("button").Single(button => RadzenDom.ButtonText(button) == "Open log folder").Click();

        cut.Find("div.binwarn").TextContent.Should().Contain("/logs/act");
    }

    [Fact]
    public void An_interop_failure_is_reported_rather_than_thrown()
    {
        Desktop.Fails = new InvalidOperationException("The circuit is gone.");

        var cut = Show();

        var open = () => cut.FindAll("button")
            .Single(button => RadzenDom.ButtonText(button) == "Open log folder").Click();

        open.Should().NotThrow();
        cut.FindAll("div.binwarn").Should().ContainSingle();
    }

    // A pointer to the page that owns them rather than a second place to edit the same thing.
    [Fact]
    public void The_templates_section_points_at_the_page_that_owns_them()
    {
        var cut = Show();

        cut.FindAll("button").Single(button => RadzenDom.ButtonText(button) == "Manage templates").Click();

        Route.Should().Be("templates");
    }

    [Fact]
    public void Leaving_goes_back_to_the_board()
    {
        Show().Find("header.bar button").Click();

        Route.Should().BeEmpty();
    }

    private IRenderedComponent<SettingsView> Show() => Render<SettingsView>();
}

internal static class PipeExtensions
{
    // Reads better than a discard when a fresh render is only there to be asserted against.
    internal static void Pipe<T>(this T value, Action<T> assert) => assert(value);
}
