using Act.Core.Abstractions;
using Act.Core.Model;
using AwesomeAssertions;
using LiteDB;
using Microsoft.Extensions.DependencyInjection;

namespace Act.Infrastructure.Tests;

public class CardStoreTests
{
    [Fact]
    public async Task A_new_store_holds_no_cards()
    {
        using var temp = new TempDirectory();
        using var provider = Provider(temp.Path);

        var cards = await provider.GetRequiredService<ICardStore>().GetAllAsync();

        cards.Should().BeEmpty();
    }

    [Fact]
    public async Task A_card_round_trips_every_field()
    {
        using var temp = new TempDirectory();
        var parentId = Guid.NewGuid();
        var dependency = Guid.NewGuid();
        var created = new DateTimeOffset(2026, 7, 28, 14, 30, 0, TimeSpan.FromHours(-4));

        var card = new Card
        {
            Number = 4711,
            SessionId = "3c9a51e6-77b2-4d0f-8a41-9e5b0c72d183",
            Title = "Refactor session binding",
            InitialPrompt = "Rework how sessions bind to cards.",
            Column = BoardColumn.YourTurn,
            Badge = Badge.NeedsPermission,
            AgentType = AgentType.Codex,
            WorkingDir = "~/dev/act",
            LaunchConfig = new LaunchConfig
            {
                AgentBinary = "/usr/local/bin/codex",
                Model = "opus",
                Effort = "xhigh",
                PermissionMode = PermissionMode.Bypass,
                ExtraFlags = ["--verbose"],
                Env = new Dictionary<string, string> { ["ACT_TEST"] = "1" },
            },
            Schedule = TaskSchedule.SpecificDateTime,
            ScheduledFor = created.AddDays(1),
            Origin = TaskOrigin.Spawned,
            ParentId = parentId,
            Children = [Guid.NewGuid(), Guid.NewGuid()],
            SpawnAuthor = SpawnAuthor.Agent,
            DependsOn = [dependency],
            CreatedAt = created,
            LaunchedAt = created.AddMinutes(3),
            CompletedAt = created.AddMinutes(40),
            DeletedAt = created.AddMinutes(55),
            ArchivedAt = created.AddMinutes(50),
            KeepOnBoard = true,
            Transitions =
            [
                new Transition { At = created, Column = BoardColumn.Ready, Note = "queued" },
                new Transition { At = created.AddMinutes(3), Column = BoardColumn.Executing, Badge = Badge.Running },
            ],
            ObservedModel = "opus 4.8",
            Metrics = new CardMetrics
            {
                TokensIn = 312_400,
                TokensOut = 44_100,
                Compactions = 2,
                ContextUsed = 142_000,
                ContextLimit = 200_000,
                TurnCount = 18,
                ToolCalls = 97,
                LastActivityAt = created.AddMinutes(38),
                ActiveTime = TimeSpan.FromMinutes(41),
            },
        };

        using (var writing = Provider(temp.Path))
            await writing.GetRequiredService<ICardStore>().AddAsync(card);

        using var reading = Provider(temp.Path);

        var stored = await reading.GetRequiredService<ICardStore>().GetAsync(card.Id);

        stored.Should().NotBeNull();
        stored.Should().BeEquivalentTo(card);
    }

    [Fact]
    public async Task Enums_are_stored_by_name_so_reordering_them_cannot_shift_stored_values()
    {
        using var temp = new TempDirectory();
        var card = new Card
        {
            Title = "Named enums",
            Column = BoardColumn.YourTurn,
            Badge = Badge.ReadyForReview,
            AgentType = AgentType.Codex,
            Schedule = TaskSchedule.NextWindow,
            LaunchConfig = new LaunchConfig { PermissionMode = PermissionMode.DontAsk },
        };

        using (var provider = Provider(temp.Path))
            await provider.GetRequiredService<ICardStore>().AddAsync(card);

        using var database = new LiteDatabase(Path.Combine(temp.Path, "act.db"));

        var stored = database.GetCollection("cards").FindById(card.Id);

        stored["Column"].AsString.Should().Be(nameof(BoardColumn.YourTurn));
        stored["Badge"].AsString.Should().Be(nameof(Badge.ReadyForReview));
        stored["AgentType"].AsString.Should().Be(nameof(AgentType.Codex));
        stored["Schedule"].AsString.Should().Be(nameof(TaskSchedule.NextWindow));
        stored["LaunchConfig"]["PermissionMode"].AsString.Should().Be(nameof(PermissionMode.DontAsk));
    }

    // A stored field the model no longer binds must not stop the card loading. No converter is
    // involved — the mapper simply has nothing to bind it to — but this repo has been surprised once
    // by what a stored value does to a read, so it is pinned rather than assumed.
    [Fact]
    public async Task A_field_this_build_no_longer_has_does_not_stop_the_card_loading()
    {
        using var temp = new TempDirectory();
        var card = new Card { Title = "Retired field", Column = BoardColumn.YourTurn };

        using (var provider = Provider(temp.Path))
            await provider.GetRequiredService<ICardStore>().AddAsync(card);

        using (var database = new LiteDatabase(Path.Combine(temp.Path, "act.db")))
        {
            var cards = database.GetCollection("cards");
            var stored = cards.FindById(card.Id);

            stored["LastMessage"] = "Waiting on permission to delete src/Api/V1.";
            cards.Update(stored);
        }

        using var reading = Provider(temp.Path);

        var reloaded = await reading.GetRequiredService<ICardStore>().GetAsync(card.Id);

        reloaded!.Title.Should().Be("Retired field");
        reloaded.Column.Should().Be(BoardColumn.YourTurn);
    }

    [Fact]
    public async Task Cards_survive_a_restart()
    {
        using var temp = new TempDirectory();

        using (var writing = Provider(temp.Path))
        {
            var store = writing.GetRequiredService<ICardStore>();

            await store.AddAsync(new Card { Title = "First", Column = BoardColumn.Preparing });
            await store.AddAsync(new Card { Title = "Second", Column = BoardColumn.Ready });
        }

        using var reading = Provider(temp.Path);

        var cards = await reading.GetRequiredService<ICardStore>().GetAllAsync();

        cards.Select(card => card.Title).Should().BeEquivalentTo(["First", "Second"]);
    }

    [Fact]
    public async Task Cards_come_back_highest_number_first()
    {
        using var temp = new TempDirectory();
        using var provider = Provider(temp.Path);
        var store = provider.GetRequiredService<ICardStore>();

        await store.AddAsync(new Card { Number = 1031, Title = "Older" });
        await store.AddAsync(new Card { Number = 1051, Title = "Newer" });
        await store.AddAsync(new Card { Number = 1042, Title = "Middle" });

        var cards = await store.GetAllAsync();

        cards.Select(card => card.Number).Should().ContainInOrder(1051, 1042, 1031);
    }

    [Fact]
    public async Task The_first_minted_number_is_1000()
    {
        using var temp = new TempDirectory();
        using var provider = Provider(temp.Path);

        var number = await provider.GetRequiredService<ICardStore>().NextNumberAsync();

        number.Should().Be(1000);
    }

    [Fact]
    public async Task Minted_numbers_are_sequential()
    {
        using var temp = new TempDirectory();
        using var provider = Provider(temp.Path);
        var store = provider.GetRequiredService<ICardStore>();

        var numbers = new[]
        {
            await store.NextNumberAsync(),
            await store.NextNumberAsync(),
            await store.NextNumberAsync(),
        };

        numbers.Should().ContainInOrder(1000, 1001, 1002);
    }

    [Fact]
    public async Task The_number_sequence_survives_a_restart()
    {
        using var temp = new TempDirectory();

        using (var writing = Provider(temp.Path))
            await writing.GetRequiredService<ICardStore>().NextNumberAsync();

        using var reading = Provider(temp.Path);

        var number = await reading.GetRequiredService<ICardStore>().NextNumberAsync();

        number.Should().Be(1001);
    }

    [Fact]
    public async Task The_sequence_starts_above_the_highest_seeded_card()
    {
        using var temp = new TempDirectory();
        using var provider = Provider(temp.Path);
        var store = provider.GetRequiredService<ICardStore>();

        await store.AddAsync(new Card { Number = 1051, Title = "Seeded" });

        var number = await store.NextNumberAsync();

        number.Should().Be(1052);
    }

    [Fact]
    public async Task Add_mints_a_number_when_the_card_has_none()
    {
        using var temp = new TempDirectory();
        using var provider = Provider(temp.Path);
        var card = new Card { Title = "Unnumbered" };

        await provider.GetRequiredService<ICardStore>().AddAsync(card);

        card.Number.Should().Be(1000);
    }

    [Fact]
    public async Task Add_keeps_an_explicit_number()
    {
        using var temp = new TempDirectory();
        using var provider = Provider(temp.Path);
        var card = new Card { Number = 1039, Title = "Numbered" };

        await provider.GetRequiredService<ICardStore>().AddAsync(card);

        card.Number.Should().Be(1039);
    }

    [Fact]
    public async Task A_duplicate_number_is_rejected()
    {
        using var temp = new TempDirectory();
        using var provider = Provider(temp.Path);
        var store = provider.GetRequiredService<ICardStore>();

        await store.AddAsync(new Card { Number = 1039, Title = "First" });

        var duplicate = async () => await store.AddAsync(new Card { Number = 1039, Title = "Second" });

        await duplicate.Should().ThrowAsync<LiteException>();
    }

    [Fact]
    public async Task Update_replaces_the_stored_card()
    {
        using var temp = new TempDirectory();
        using var provider = Provider(temp.Path);
        var store = provider.GetRequiredService<ICardStore>();
        var card = new Card { Title = "Draft", Column = BoardColumn.Preparing };

        await store.AddAsync(card);

        card.Column = BoardColumn.Ready;
        card.Schedule = TaskSchedule.Now;
        await store.UpdateAsync(card);

        var stored = await store.GetAsync(card.Id);

        stored!.Column.Should().Be(BoardColumn.Ready);
        stored.Schedule.Should().Be(TaskSchedule.Now);
    }

    [Fact]
    public async Task Update_rejects_a_card_that_is_not_stored()
    {
        using var temp = new TempDirectory();
        using var provider = Provider(temp.Path);

        var update = async () => await provider.GetRequiredService<ICardStore>()
            .UpdateAsync(new Card { Number = 1039, Title = "Ghost" });

        await update.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Delete_removes_the_card()
    {
        using var temp = new TempDirectory();
        using var provider = Provider(temp.Path);
        var store = provider.GetRequiredService<ICardStore>();
        var card = new Card { Title = "Doomed" };

        await store.AddAsync(card);

        (await store.DeleteAsync(card.Id)).Should().BeTrue();
        (await store.GetAsync(card.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Delete_reports_an_unknown_card()
    {
        using var temp = new TempDirectory();
        using var provider = Provider(temp.Path);

        var deleted = await provider.GetRequiredService<ICardStore>().DeleteAsync(Guid.NewGuid());

        deleted.Should().BeFalse();
    }

    [Fact]
    public async Task An_unknown_card_reads_back_as_null()
    {
        using var temp = new TempDirectory();
        using var provider = Provider(temp.Path);

        var card = await provider.GetRequiredService<ICardStore>().GetAsync(Guid.NewGuid());

        card.Should().BeNull();
    }

    private static ServiceProvider Provider(string dataDirectory)
        => new ServiceCollection().AddActInfrastructure(dataDirectory).BuildServiceProvider();
}
