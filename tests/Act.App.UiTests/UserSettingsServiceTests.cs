using Act.App.Settings;
using Act.Core.Model;
using Act.Core.Rules;
using Act.Core.Scheduling;
using AwesomeAssertions;

namespace Act.App.UiTests;

// The settings page writes through this on every keystroke that matters, and the queue reads its
// policy back out of it — so the two things worth pinning are that a change persists and announces
// itself, and that a non-change does neither.
public class UserSettingsServiceTests
{
    [Fact]
    public void The_shipped_defaults_are_what_a_fresh_install_gets()
    {
        var (settings, _) = ServiceOf();

        settings.Language.Should().Be(LanguagePreference.System);
        settings.Theme.Should().Be(ThemePreference.System);
        settings.Density.Should().Be(BoardDensity.Detailed);
        settings.KeepAwake.Should().BeTrue();
        settings.Notifications.Should().BeTrue();
        settings.PreventConcurrentWorkingDir.Should().BeTrue();
        settings.AutoArchiveCompleted.Should().BeTrue();

        // The master switch ships *off*: auto-execution is the point of the queue.
        settings.AutoExecutionPaused.Should().BeFalse();
    }

    [Fact]
    public void A_change_is_saved_and_announced()
    {
        var (settings, store) = ServiceOf();
        var announcements = 0;

        settings.Changed += () => announcements++;

        settings.SetDensity(BoardDensity.Compact);

        settings.Density.Should().Be(BoardDensity.Compact);
        store.Load().Density.Should().Be(BoardDensity.Compact);
        announcements.Should().Be(1);
    }

    // The guard on every setter. Without it the settings page would wake the queue runner and every
    // circuit on each re-render that happened to re-send the value it already had.
    [Fact]
    public void Setting_a_value_to_what_it_already_is_announces_nothing()
    {
        var (settings, _) = ServiceOf();
        var announcements = 0;

        settings.Changed += () => announcements++;

        settings.SetDensity(settings.Density);
        settings.SetTheme(settings.Theme);
        settings.SetLanguage(settings.Language);
        settings.SetKeepAwake(settings.KeepAwake);
        settings.SetNotifications(settings.Notifications);
        settings.SetCloseToTray(settings.CloseToTray);
        settings.SetBlinkYourTurn(settings.BlinkYourTurn);
        settings.SetAutoExecutionPaused(settings.AutoExecutionPaused);
        settings.SetPreventConcurrentWorkingDir(settings.PreventConcurrentWorkingDir);
        settings.SetMaxConcurrent(settings.MaxConcurrent);
        settings.SetAutoArchiveCompleted(settings.AutoArchiveCompleted);
        settings.SetAutoArchiveCompletedAfterDays(settings.AutoArchiveCompletedAfterDays);

        announcements.Should().Be(0);
    }

    // A real resource ceiling rather than a politeness setting, so it is clamped on the way in
    // rather than trusted from a spin box.
    [Theory]
    [InlineData(0, ConcurrencySlots.Minimum)]
    [InlineData(-5, ConcurrencySlots.Minimum)]
    [InlineData(999, ConcurrencySlots.Maximum)]
    [InlineData(3, 3)]
    public void The_concurrency_cap_is_clamped(int asked, int expected)
    {
        var (settings, store) = ServiceOf();

        settings.SetMaxConcurrent(asked);

        settings.MaxConcurrent.Should().Be(expected);
        store.Load().MaxConcurrent.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, CompletedRetention.MinimumDays)]
    [InlineData(100_000, CompletedRetention.MaximumDays)]
    [InlineData(30, 30)]
    public void The_retention_window_is_clamped(int asked, int expected)
    {
        var (settings, _) = ServiceOf();

        settings.SetAutoArchiveCompletedAfterDays(asked);

        settings.AutoArchiveCompletedAfterDays.Should().Be(expected);
        settings.CompletedRetentionWindow.Should().Be(TimeSpan.FromDays(expected));
    }

    // Off is not "zero days" — it is no window at all, which is what stops the sweep.
    [Fact]
    public void Turning_auto_archive_off_leaves_no_window()
    {
        var (settings, _) = ServiceOf();

        settings.SetAutoArchiveCompleted(false);

        settings.CompletedRetentionWindow.Should().BeNull();
    }

    // A stored settings object handed out by reference would let a form edit the settings in place
    // and skip the save entirely.
    [Fact]
    public void Agent_defaults_are_handed_out_as_a_copy()
    {
        var (settings, _) = ServiceOf();

        settings.SetDefaults(new AgentDefaults { Agent = AgentType.Codex, Binary = "codex.exe" });

        var borrowed = settings.Defaults(AgentType.Codex);
        borrowed.Binary = "tampered.exe";

        settings.Defaults(AgentType.Codex).Binary.Should().Be("codex.exe");
    }

    [Fact]
    public void Task_defaults_are_handed_out_as_a_copy()
    {
        var (settings, _) = ServiceOf();

        settings.SetTaskDefaults(new TaskDefaults { WorkingDir = "/dev/act" });

        var borrowed = settings.TaskDefaults;
        borrowed.WorkingDir = "/tampered";

        settings.TaskDefaults.WorkingDir.Should().Be("/dev/act");
    }

    [Fact]
    public void An_agent_ACT_has_never_seen_still_answers_with_a_default()
    {
        var (settings, _) = ServiceOf();

        var defaults = settings.Defaults(AgentType.ClaudeCode);

        defaults.Agent.Should().Be(AgentType.ClaudeCode);
        defaults.Binary.Should().BeNull("empty means resolve the bare name on PATH");
        defaults.Enabled.Should().BeTrue();
    }

    // Saving the same agent twice must replace it, not accumulate — the list is keyed by agent even
    // though it is stored as a list.
    [Fact]
    public void Saving_an_agent_twice_replaces_rather_than_duplicates()
    {
        var (settings, store) = ServiceOf();

        settings.SetDefaults(new AgentDefaults { Agent = AgentType.Codex, Binary = "first.exe" });
        settings.SetDefaults(new AgentDefaults { Agent = AgentType.Codex, Binary = "second.exe" });

        store.Load().Agents.Should().ContainSingle().Which.Binary.Should().Be("second.exe");
    }

    [Fact]
    public void Only_the_agents_whose_switch_is_on_are_offered()
    {
        var (settings, _) = ServiceOf();

        settings.SetDefaults(new AgentDefaults { Agent = AgentType.ClaudeCode, Enabled = true });
        settings.SetDefaults(new AgentDefaults { Agent = AgentType.Codex, Enabled = false });

        settings.EnabledAgents.Should().Equal(AgentType.ClaudeCode);
    }

    // Everything the queue reads, in one object, so the runner and the board ask the same question
    // of the same values rather than each assembling their own.
    [Fact]
    public void The_queue_policy_carries_the_settings_the_queue_reads()
    {
        var (settings, _) = ServiceOf();

        settings.SetMaxConcurrent(4);
        settings.SetAutoExecutionPaused(true);
        settings.SetPreventConcurrentWorkingDir(false);
        settings.SetDefaults(new AgentDefaults { Agent = AgentType.Codex, Enabled = true });

        var policy = settings.QueuePolicy;

        policy.MaxConcurrent.Should().Be(4);
        policy.AutoExecutionPaused.Should().BeTrue();
        policy.PreventConcurrentWorkingDir.Should().BeFalse();
        policy.EnabledAgents.Should().Equal([AgentType.Codex]);
    }

    // Dragging a window edge fires this many times a second and nothing on screen is bound to it,
    // so it goes straight to the store without waking anyone.
    [Fact]
    public void Remembering_the_window_announces_nothing()
    {
        var (settings, store) = ServiceOf();
        var announcements = 0;

        settings.Changed += () => announcements++;

        settings.SetWindow(new WindowBounds { X = 10, Y = 20, Width = 1400, Height = 900 });

        announcements.Should().Be(0);
        store.Load().Window!.Width.Should().Be(1400);
        settings.Window!.Width.Should().Be(1400);
    }

    [Fact]
    public void The_remembered_window_is_handed_out_as_a_copy()
    {
        var (settings, _) = ServiceOf();

        settings.SetWindow(new WindowBounds { Width = 1400, Height = 900 });

        var borrowed = settings.Window!;
        borrowed.Width = 1;

        settings.Window!.Width.Should().Be(1400);
    }

    [Fact]
    public void Nothing_is_remembered_about_the_window_until_the_shell_has_run()
    {
        var (settings, _) = ServiceOf();

        settings.Window.Should().BeNull();
    }

    // What makes "disable an agent ACT cannot find" a first impression rather than a rule that keeps
    // undoing the user.
    [Fact]
    public void Discovery_records_that_it_has_run()
    {
        var (settings, store) = ServiceOf();

        settings.AgentInstallsProbed.Should().BeFalse();

        settings.MarkAgentInstallsProbed();

        settings.AgentInstallsProbed.Should().BeTrue();
        store.Load().AgentInstallsProbed.Should().BeTrue();
    }

    [Fact]
    public void Settings_are_read_back_from_the_store_on_construction()
    {
        var store = new FakeSettingsStore();

        store.Save(new UserSettings { Density = BoardDensity.Compact, MaxConcurrent = 9 });

        var settings = new UserSettingsService(store, new AppCulture());

        settings.Density.Should().Be(BoardDensity.Compact);
        settings.MaxConcurrent.Should().Be(9);
    }

    private static (UserSettingsService, FakeSettingsStore) ServiceOf()
    {
        var store = new FakeSettingsStore();

        return (new UserSettingsService(store, new AppCulture()), store);
    }
}
