using Act.App.Cards;
using Act.App.Settings;
using Act.Core.Model;
using Act.Core.Rules;
using Act.Core.Scheduling;
using Act.Core.Spawning;
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
        settings.Telemetry.Should().BeTrue();

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
        settings.SetTelemetry(settings.Telemetry);
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

    // A runaway-loop backstop, so it is clamped like the others — but **zero is a legitimate value**
    // here and must survive, because it is how a user turns agent-spawned work off entirely.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, SpawnQuota.Minimum)]
    [InlineData(50_000, SpawnQuota.Maximum)]
    [InlineData(25, 25)]
    public void The_follow_up_cap_is_clamped_but_zero_survives(int asked, int expected)
    {
        var (settings, store) = ServiceOf();

        settings.SetMaxFollowUpsPerCard(asked);

        settings.MaxFollowUpsPerCard.Should().Be(expected);
        store.Load().MaxFollowUpsPerCard.Should().Be(expected);
    }

    // A board that has never been told otherwise still accepts follow-ups: the default is a ceiling no
    // real plan reaches, not a switch a user has to find and turn on.
    [Fact]
    public void The_follow_up_cap_defaults_to_the_backstop_rather_than_to_zero()
        => ServiceOf().Item1.MaxFollowUpsPerCard.Should().Be(SpawnQuota.Default);

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

    // The invariant every reader of a template leans on: there is always exactly one default, so
    // nothing has to handle its absence. A fresh install has no settings document at all, which is why
    // it is seeded on the way in rather than by the schema migration.
    [Fact]
    public void A_fresh_install_already_has_one_default_template()
    {
        var (settings, store) = ServiceOf();

        var template = settings.Templates.Should().ContainSingle().Subject;

        template.IsDefault.Should().BeTrue();
        template.Id.Should().NotBeEmpty();

        store.Load().Templates.Should().ContainSingle();
    }

    // The default's name is *not stored*: it is not the user's to change, and a stored translation
    // would freeze the language it was written in. What it reads as comes from the resources, so the
    // only thing worth pinning here is that the two halves agree.
    [Fact]
    public void The_default_templates_name_is_rendered_rather_than_stored()
    {
        var (settings, _) = ServiceOf();

        var template = settings.DefaultTemplate;

        template.Name.Should().BeEmpty();
        TaskLabels.Template(template).Should().NotBeNullOrWhiteSpace();
    }

    // The default is what the bare New task button starts from, so a title or a prompt on it would
    // make every task begin as a copy of the last. Enforced in the store rather than only by the page
    // that hides the fields, and re-applied on load so a store an earlier build wrote is corrected.
    [Fact]
    public void The_default_template_cannot_be_given_a_name_title_or_prompt()
    {
        var (settings, store) = ServiceOf();

        var edited = settings.DefaultTemplate;

        edited.Name = "Renamed";
        edited.Title = "Nightly sweep";
        edited.Prompt = "Update the packages";
        edited.WorkingDir = "C:/dev/act";

        settings.SaveTemplate(edited);

        var saved = settings.DefaultTemplate;

        saved.Name.Should().BeEmpty();
        saved.Title.Should().BeEmpty();
        saved.Prompt.Should().BeEmpty();

        // Everything else about it is still the user's to set — that is what the template is for.
        saved.WorkingDir.Should().Be("C:/dev/act");
        store.Load().Templates.Should().ContainSingle().Which.Name.Should().BeEmpty();
    }

    [Fact]
    public void A_default_a_previous_build_let_keep_a_title_is_cleared_on_the_way_in()
    {
        var store = new FakeSettingsStore();

        store.Save(new UserSettings
        {
            Templates =
            [
                new TaskTemplate
                {
                    Id = Guid.NewGuid(),
                    IsDefault = true,
                    Name = "Default",
                    Title = "test",
                    Prompt = "do the thing",
                    WorkingDir = "C:/dev/act",
                },
                new TaskTemplate { Id = Guid.NewGuid(), Name = "Bugfix", Title = "Fix it" },
            ],
        });

        var settings = new UserSettingsService(store, new AppCulture());

        var restricted = settings.DefaultTemplate;

        restricted.Name.Should().BeEmpty();
        restricted.Title.Should().BeEmpty();
        restricted.Prompt.Should().BeEmpty();
        restricted.WorkingDir.Should().Be("C:/dev/act");

        // Only the default is restricted; a named template keeps both.
        var named = settings.Templates.Single(template => !template.IsDefault);

        named.Name.Should().Be("Bugfix");
        named.Title.Should().Be("Fix it");

        // And the correction is persisted, not re-applied on every load.
        store.Load().Templates.Single(template => template.IsDefault).Title.Should().BeEmpty();
    }

    [Fact]
    public void Templates_are_handed_out_as_copies()
    {
        var (settings, _) = ServiceOf();

        var borrowed = settings.DefaultTemplate;
        borrowed.WorkingDir = "/tampered";

        settings.DefaultTemplate.WorkingDir.Should().BeEmpty();
    }

    [Fact]
    public void A_saved_template_gets_an_id_and_joins_the_list()
    {
        var (settings, store) = ServiceOf();

        settings.SaveTemplate(new TaskTemplate { Name = "Bugfix", WorkingDir = "/dev/act" });

        var saved = settings.Templates.Should().HaveCount(2).And
            .ContainSingle(template => template.Name == "Bugfix").Subject;

        saved.Id.Should().NotBeEmpty();
        saved.IsDefault.Should().BeFalse();
        store.Load().Templates.Should().HaveCount(2);
    }

    [Fact]
    public void Saving_a_template_twice_replaces_rather_than_duplicates()
    {
        var (settings, _) = ServiceOf();

        settings.SaveTemplate(new TaskTemplate { Name = "Bugfix" });

        var saved = settings.Templates.Single(template => !template.IsDefault);

        saved.Name = "Bugfix v2";
        settings.SaveTemplate(saved);

        settings.Templates.Should().HaveCount(2)
            .And.ContainSingle(template => template.Name == "Bugfix v2");
    }

    // Which template is the default is the store's business, not a form's: a save that carried the
    // flag would let the editor promote a template by round-tripping it, and there is deliberately no
    // other way to move it — the shipped default is the default for good.
    [Fact]
    public void Saving_a_template_cannot_change_which_one_is_the_default()
    {
        var (settings, _) = ServiceOf();

        var original = settings.DefaultTemplate;

        settings.SaveTemplate(new TaskTemplate { Name = "Bugfix", IsDefault = true });

        settings.DefaultTemplate.Id.Should().Be(original.Id);
        settings.Templates.Should().ContainSingle(template => template.IsDefault);
    }

    // Sorted by name, not by creation: this list is read to *find* a template, and insertion order is
    // an order only the person who made them knows. Case folds, so a lower-cased name is not exiled
    // to the end.
    [Fact]
    public void Templates_are_listed_alphabetically_whatever_their_case()
    {
        var (settings, _) = ServiceOf();

        settings.SaveTemplate(new TaskTemplate { Name = "zebra" });
        settings.SaveTemplate(new TaskTemplate { Name = "Alpha" });
        settings.SaveTemplate(new TaskTemplate { Name = "beta" });

        // The shipped default is left out of the expectation rather than positioned in it: its name is
        // localised, so where it sorts depends on the language and is not what this pins.
        settings.Templates.Select(template => template.Name)
            .Where(name => name is "Alpha" or "beta" or "zebra")
            .Should().Equal("Alpha", "beta", "zebra");
    }

    // It sorts on the name the user *reads*, which for the default is not the empty string it stores —
    // otherwise it would pin itself to the top of every list whatever it is called.
    [Fact]
    public void The_default_sorts_by_the_name_it_renders_as()
    {
        var (settings, _) = ServiceOf();

        settings.SaveTemplate(new TaskTemplate { Name = "aaa" });

        settings.Templates.Select(TaskLabels.Template)
            .Should().Equal("aaa", TaskLabels.Template(settings.DefaultTemplate));
    }

    // A board whose New task button has nothing to start from is not a state to offer, so the refusal
    // lives here rather than only in the page that hides the button.
    [Fact]
    public void The_default_template_cannot_be_deleted()
    {
        var (settings, _) = ServiceOf();

        settings.DeleteTemplate(settings.DefaultTemplate.Id);

        settings.Templates.Should().ContainSingle().Which.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void Any_other_template_can_be_deleted()
    {
        var (settings, store) = ServiceOf();

        settings.SaveTemplate(new TaskTemplate { Name = "Bugfix" });

        settings.DeleteTemplate(settings.Templates.Single(template => template.Name == "Bugfix").Id);

        settings.Templates.Should().ContainSingle();
        store.Load().Templates.Should().ContainSingle();
    }

    // What `/card/new?template=…` resolves with. An id that no longer names anything falls back to
    // the default rather than leaving the page with nothing to pre-fill from.
    [Fact]
    public void An_unknown_template_id_falls_back_to_the_default()
    {
        var (settings, _) = ServiceOf();

        settings.TemplateOrDefault(Guid.NewGuid()).Id.Should().Be(settings.DefaultTemplate.Id);
        settings.TemplateOrDefault(null).Id.Should().Be(settings.DefaultTemplate.Id);
    }

    [Fact]
    public void A_known_template_id_resolves_to_that_template()
    {
        var (settings, _) = ServiceOf();

        settings.SaveTemplate(new TaskTemplate { Name = "Bugfix" });

        var bugfix = settings.Templates.Single(template => template.Name == "Bugfix");

        settings.TemplateOrDefault(bugfix.Id).Name.Should().Be("Bugfix");
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

    // Seeded rather than migrated: a fresh install has no settings document for a migration to find,
    // so the id is established on the way in and every reader can assume it.
    [Fact]
    public void A_fresh_install_is_given_an_install_id_and_keeps_it()
    {
        var store = new FakeSettingsStore();

        var first = new UserSettingsService(store, new AppCulture()).InstallId;

        Guid.TryParse(first, out _).Should().BeTrue();
        store.Load().InstallId.Should().Be(first);

        new UserSettingsService(store, new AppCulture()).InstallId.Should().Be(first);
    }

    [Fact]
    public void Turning_telemetry_off_does_not_take_the_install_id_away()
    {
        var (settings, store) = ServiceOf();

        settings.SetTelemetry(false);

        settings.InstallId.Should().NotBeEmpty();
        store.Load().InstallId.Should().Be(settings.InstallId);
    }

    private static (UserSettingsService, FakeSettingsStore) ServiceOf()
    {
        var store = new FakeSettingsStore();

        return (new UserSettingsService(store, new AppCulture()), store);
    }
}
