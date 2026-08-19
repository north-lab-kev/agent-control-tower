namespace Act.Core.Agents;

// The keystrokes that end a turn, spelled once so an adapter can name the ones its own CLI honours
// without a bare control byte appearing in its command-line code.
//
// Both bytes are what a browser's xterm actually sends, read out of the vendored `xterm.js` rather
// than assumed: with Ctrl held, keyCode 65–90 becomes `String.fromCharCode(keyCode - 64)`, and
// keyCode 27 becomes `C0.ESC`. One `onData` call per keypress, so each arrives at `OnData` as exactly
// one of these strings and nothing longer. ACT's own terminal hands every Ctrl key to the agent
// except Ctrl+V (see `act-terminal.js`), so nothing intercepts either of them on the way.
//
// `Escape` is the *lone* escape byte, which is why matching the whole chunk matters more here than
// for Ctrl+C: every arrow key, function key and mouse report starts with the same byte and continues
// (`ESC [ A`), and Alt+Escape sends it twice. None of those are this string.
public static class TurnInterruptKeys
{
    public const string CtrlC = "\u0003";

    public const string Escape = "\u001b";
}
