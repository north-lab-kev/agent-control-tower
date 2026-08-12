using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Radzen;

namespace Act.App.Components.Shared;

// A dialog rather than a page, by the same rule the follow-ups prompt follows: it interrupts a save
// already under way, on a form that may never have been saved, and there is no url that means "name
// the template I am about to make".
public partial class TemplateNameDialog(DialogService dialogService)
{
    private string name = string.Empty;

    [Parameter]
    public string Suggested { get; set; } = string.Empty;

    private bool Named => !string.IsNullOrWhiteSpace(name);

    protected override void OnParametersSet() => name = Suggested;

    private void OnKeyDown(KeyboardEventArgs args)
    {
        if (args.Key is "Enter" && Named)
            Accept();
    }

    private void Accept() => dialogService.Close(name.Trim());

    private void Cancel() => dialogService.Close(null);
}
