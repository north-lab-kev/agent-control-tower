using System.Globalization;
using Act.App.Cards;
using Act.App.Resources;
using Act.Core.Model;
using Act.Core.Rules;
using Microsoft.AspNetCore.Components;
using Radzen;

namespace Act.App.Components.Pages;

// Everything the user deleted, and the only two things you can do with it: put one back, or empty
// the whole thing. Deliberately not a second board — an archived card is not work in progress, so
// it gets a list rather than a column.
public partial class ArchiveView(
    BoardState board,
    DialogService dialogService,
    NavigationManager navigation) : IDisposable
{
    private bool purging;

    private bool duplicating;

    private bool seeded;

    private string query = string.Empty;

    // The board hands its query over rather than making the user type it twice — it is the same
    // question, asked of the half of the store the board cannot show.
    [SupplyParameterFromQuery(Name = "q")]
    private string? InitialQuery { get; set; }

    private bool Filtering => CardSearch.IsActive(query);

    private IReadOnlyList<Card> Rows => CardSearch.Filter(board.Archived, query);

    protected override void OnInitialized() => board.Changed += OnChanged;

    // Once: a cascading value changing must not throw away what the user has typed since.
    protected override void OnParametersSet()
    {
        if (seeded)
            return;

        seeded = true;
        query = InitialQuery ?? string.Empty;
    }

    public void Dispose() => board.Changed -= OnChanged;

    private void OnChanged() => _ = InvokeAsync(StateHasChanged);

    private Task RestoreAsync(Card card) => board.RestoreAsync(card);

    // The archived card stays archived: the copy is a live task in Preparing, and opening it is
    // what makes that obvious — the archive itself would look unchanged.
    private async Task DuplicateAsync(Card card)
    {
        if (duplicating)
            return;

        duplicating = true;

        try
        {
            var copy = await board.DuplicateAsync(card);

            navigation.NavigateTo($"/card/{copy.Id}/edit");
        }
        finally
        {
            duplicating = false;
        }
    }

    private async Task PurgeAsync()
    {
        if (purging)
            return;

        var confirmed = await dialogService.Confirm(
            Text.Format(Strings.Archive_ClearConfirm, board.Archived.Count),
            Strings.Archive_Clear,
            new ConfirmOptions
            {
                OkButtonText = Strings.Archive_ClearYes,
                CancelButtonText = Strings.NewTask_Cancel,
                CssClass = "act-dialog",
            });

        if (confirmed is not true)
            return;

        purging = true;

        try
        {
            await board.PurgeArchivedAsync();
        }
        finally
        {
            purging = false;
        }
    }

    private void BackToBoard() => navigation.NavigateTo("/");

    // Where it came from, when it went, and whether the user or the retention window put it here —
    // enough to recognise a card without opening it, which is the whole job of this list.
    private static string Meta(Card card)
    {
        var at = (card.DeletedAt ?? card.ArchivedAt)?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        var how = card.IsDeleted ? null : Strings.Archive_AutoArchived;

        return string.Join(
            " · ",
            new[] { CardVisuals.Column(card.Column), card.WorkingDir, at, how }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}
