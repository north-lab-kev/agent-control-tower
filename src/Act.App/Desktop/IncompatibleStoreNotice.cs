using Act.App.Resources;
using Act.Infrastructure.Storage;
using ElectronNET.API;
using ElectronNET.API.Entities;

namespace Act.App.Desktop;

// What a downgraded ACT says instead of opening. The store is from a newer build, so nothing may be
// read — a window would be a board that cannot load and a Settings page that cannot answer — and the
// only honest move is to say so and go.
//
// **No parent window, on purpose.** Everywhere else a parentless Electron message box shows nothing at
// all and `DesktopShell` reveals the window first to carry the question; here there is no window to
// reveal and never will be, so `ShowErrorBox` is used instead. It is the one dialog in Electron that
// needs no parent and works before any window exists.
//
// **The language is the OS's, not the user's.** The preference lives in the store this cannot read, so
// `ApplyLanguage` never ran. Leaving the culture alone means the resources resolve against
// `CurrentUICulture`, which is the closest thing to the right answer that is still knowable.
internal static class IncompatibleStoreNotice
{
    public static async Task ShowAndExitAsync(StoreCompatibility store)
    {
        Electron.Dialog.ShowErrorBox(
            Strings.Store_Incompatible_Title,
            Text.Format(Strings.Store_Incompatible_Body, store.Stored, store.Understood));

        // `ShowErrorBox` is fire-and-forget across the bridge, so the exit is given a moment to leave
        // after it rather than racing the socket write that draws it.
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        Electron.App.Exit(1);
    }
}
