using AwesomeAssertions;
using Microsoft.Playwright;

namespace Act.App.E2eTests;

public sealed class TemplateLayoutTests : BrowserTest
{
    private const string Bottom = "element => element.getBoundingClientRect().bottom";

    private const string Top = "element => element.getBoundingClientRect().top";

    [Fact]
    public async Task The_default_notice_is_spaced_like_every_other_block_on_the_sheet()
    {
        await GoAsync(DefaultTemplateRoute);

        await Assertions.Expect(Page.Locator(".sheet .rz-alert")).ToBeVisibleAsync();

        var alertBottom = await Page.Locator(".sheet .rz-alert").EvaluateAsync<double>(Bottom);
        var firstTop = await Page.Locator(".sheet .rz-fieldset").Nth(0).EvaluateAsync<double>(Top);
        var firstBottom = await Page.Locator(".sheet .rz-fieldset").Nth(0).EvaluateAsync<double>(Bottom);
        var secondTop = await Page.Locator(".sheet .rz-fieldset").Nth(1).EvaluateAsync<double>(Top);

        var betweenFieldsets = secondTop - firstBottom;

        betweenFieldsets.Should().BeGreaterThan(0);
        (firstTop - alertBottom).Should().BeApproximately(betweenFieldsets, 0.5);
    }

    [Fact]
    public async Task The_browse_button_keeps_its_padding_beside_the_path_box()
    {
        await GoAsync(DefaultTemplateRoute);

        var button = Page.Locator(".dirrow .rz-button");

        await Assertions.Expect(button).ToBeVisibleAsync();

        var padding = await button.EvaluateAsync<double>(
            "element => parseFloat(getComputedStyle(element).paddingRight)");

        var slack = await button.EvaluateAsync<double>(
            """
            element => element.getBoundingClientRect().right
                - element.querySelector('.rz-button-text').getBoundingClientRect().right
            """);

        padding.Should().BeGreaterThan(0);
        slack.Should().BeApproximately(padding, 0.5, "a shrunk button pulls its label onto the edge");
    }

    private string DefaultTemplateRoute => $"/template/{App.Settings.DefaultTemplate.Id}";
}
