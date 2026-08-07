using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Act.App.Components.Shared;

public partial class CardFilter
{
    [Parameter, EditorRequired]
    public string Query { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> QueryChanged { get; set; }

    [Parameter, EditorRequired]
    public string Placeholder { get; set; } = string.Empty;

    private Task OnInput(ChangeEventArgs args) => SetAsync(args.Value?.ToString() ?? string.Empty);

    private Task OnKeyDownAsync(KeyboardEventArgs args)
        => args.Key is "Escape" ? SetAsync(string.Empty) : Task.CompletedTask;

    private Task ClearAsync() => SetAsync(string.Empty);

    private Task SetAsync(string query)
    {
        if (query == Query)
            return Task.CompletedTask;

        Query = query;

        return QueryChanged.InvokeAsync(query);
    }
}
