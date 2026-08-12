using Act.App.Cards;
using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.App.UiTests;

// One property matters more than all the others here: **it always answers**. A save that failed
// because a CLI was missing would be a worse form than the one that made the user type a title, so
// every failure path is pinned rather than only the happy one.
public class TaskTitlesTests
{
    [Fact]
    public async Task An_agents_answer_becomes_the_title()
    {
        var (titles, adapter) = ServiceOf();

        adapter.Answer = "  \"Consolidate the badge colours\"  ";

        var result = await titles.SuggestAsync("make the badge colours come from one place", adapter.Agent);

        result!.Title.Should().Be("Consolidate the badge colours");
        result.Generated.Should().BeTrue();
    }

    [Fact]
    public async Task The_agent_is_asked_about_the_prompt_and_is_told_what_a_title_is()
    {
        var (titles, adapter) = ServiceOf();

        await titles.SuggestAsync("make the badge colours come from one place", adapter.Agent);

        adapter.Queries.Should().HaveCount(1);
        adapter.Queries[0].Prompt.Should().Contain("make the badge colours come from one place");
        adapter.Queries[0].Prompt.Should().NotBe("make the badge colours come from one place");
    }

    // Where the binary lives is the machine's answer, and a query needs it exactly as much as a
    // launch does — an off-`PATH` Codex would otherwise be unreachable for this one call.
    [Fact]
    public async Task The_machines_install_is_passed_through()
    {
        var (titles, adapter, settings) = ServiceAndSettings();

        settings.SetDefaults(new AgentDefaults { Agent = adapter.Agent, Binary = @"C:\tools\cli.exe" });

        await titles.SuggestAsync("name this", adapter.Agent);

        adapter.Queries[0].Machine!.Binary.Should().Be(@"C:\tools\cli.exe");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task No_answer_falls_back_to_the_prompts_opening_words(string? answer)
    {
        var (titles, adapter) = ServiceOf();

        adapter.Answer = answer;

        var result = await titles.SuggestAsync("Fix the timeline rail.\n\nThen the dots.", adapter.Agent);

        result!.Title.Should().Be("Fix the timeline rail");
        result.Generated.Should().BeFalse();
    }

    // A CLI that is not installed throws out of the adapter, and it must not reach the form as an
    // exception — the user pressed Save, not "diagnose my install".
    [Fact]
    public async Task A_throwing_adapter_falls_back_rather_than_failing()
    {
        var (titles, adapter) = ServiceOf();

        adapter.QueryFails = new FileNotFoundException("no such cli");

        var result = await titles.SuggestAsync("Fix the timeline rail", adapter.Agent);

        result!.Title.Should().Be("Fix the timeline rail");
        result.Generated.Should().BeFalse();
    }

    // Cancellation is the one thing that is not a fallback: the page has gone, and there is nobody
    // left to hand a title to.
    [Fact]
    public async Task Cancellation_propagates_instead_of_falling_back()
    {
        var (titles, adapter) = ServiceOf();

        adapter.QueryFails = new OperationCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => titles.SuggestAsync("Fix the timeline rail", adapter.Agent));
    }

    [Fact]
    public async Task An_agent_with_no_adapter_still_yields_a_title()
    {
        var (titles, adapter) = ServiceOf();

        var other = adapter.Agent is AgentType.Codex ? AgentType.ClaudeCode : AgentType.Codex;

        var result = await titles.SuggestAsync("Fix the timeline rail", other);

        result!.Title.Should().Be("Fix the timeline rail");
        result.Generated.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n ")]
    public async Task Nothing_is_asked_about_an_empty_prompt(string prompt)
    {
        var (titles, adapter) = ServiceOf();

        (await titles.SuggestAsync(prompt, adapter.Agent)).Should().BeNull();
        adapter.Queries.Should().BeEmpty();
    }

    private static (TaskTitles, MockAgentAdapter) ServiceOf()
    {
        var (titles, adapter, _) = ServiceAndSettings();

        return (titles, adapter);
    }

    private static (TaskTitles, MockAgentAdapter, UserSettingsService) ServiceAndSettings()
    {
        var adapter = new MockAgentAdapter();
        var settings = new UserSettingsService(new FakeSettingsStore(), new AppCulture());

        return (new TaskTitles([adapter], settings, NullLogger<TaskTitles>.Instance), adapter, settings);
    }
}
