using Act.App.Desktop;
using AwesomeAssertions;
using ElectronNET.API.Entities;

namespace Act.App.UiTests;

// The application menu exists for one reason and it is not a menu: Electron takes the clipboard
// accelerators from it, so the empty menu ACT used to set — set only to keep the bar off the window
// — unbound Ctrl+V everywhere, and no agent in the terminal could be pasted into.
//
// Both halves are load-bearing, so both are pinned here. An empty menu is the regression that
// started this; a *fuller* menu is the regression that would follow it, because a menu accelerator
// wins over the page and every other editing role wants a key the CLIs need for themselves.
public class DesktopMenuTests
{
    private static readonly MenuRole[] TerminalKeys =
    [
        MenuRole.copy,
        MenuRole.cut,
        MenuRole.selectAll,
        MenuRole.undo,
        MenuRole.redo,
        MenuRole.delete,
        MenuRole.pasteAndMatchStyle,
    ];

    [Fact]
    public void The_menu_binds_paste()
        => Roles().Should().Contain(MenuRole.paste);

    // Not "is not empty" — an empty menu is exactly what Electron is handed when the accelerators go
    // missing, so the assertion has to be that something is actually there to carry one.
    [Fact]
    public void The_menu_is_never_empty()
        => DesktopShell.EditMenu().Should().NotBeEmpty();

    // Ctrl+C is the interrupt, Ctrl+X opens Claude Code's `ctrl+x ctrl+e`, Ctrl+A is
    // beginning-of-line and Ctrl+Z suspends. Binding any of them here takes the key away from the
    // agent for good, and no screen would show it — the terminal would simply stop responding to it.
    [Theory]
    [MemberData(nameof(ForbiddenRoles))]
    public void The_menu_leaves_the_terminal_its_own_keys(MenuRole role)
        => Roles().Should().NotContain(role);

    [Fact]
    public void The_menu_binds_nothing_but_paste()
        => Roles().Should().Equal(MenuRole.paste);

    // The native role is the whole point: a Click handler in its place would be ACT's own command,
    // which never touches the clipboard. It also guards the trap in `MenuRole` — the enum is not
    // nullable and its first member is `undo`, so an item left without a role does not read as
    // "no role", it reads as one of the keys the terminal needs. The role assertions above catch
    // that; this one keeps the two ways of filling an item from being confused in the first place.
    [Fact]
    public void Every_leaf_is_a_native_role_and_not_a_handler()
        => Leaves().Should().OnlyContain(item => item.Click == null);

    public static TheoryData<MenuRole> ForbiddenRoles()
    {
        var data = new TheoryData<MenuRole>();

        foreach (var role in TerminalKeys)
            data.Add(role);

        return data;
    }

    private static IEnumerable<MenuRole> Roles()
        => Leaves().Select(item => item.Role);

    private static IEnumerable<MenuItem> Leaves()
        => Flatten(DesktopShell.EditMenu()).Where(item => item.Submenu is null or []);

    private static IEnumerable<MenuItem> Flatten(IEnumerable<MenuItem>? items)
        => items?.SelectMany(item => Flatten(item.Submenu).Prepend(item)) ?? [];
}
