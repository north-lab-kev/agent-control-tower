using Act.Infrastructure.Terminal;
using AwesomeAssertions;

namespace Act.Infrastructure.Tests;

// The rules are `CommandLineToArgvW`'s, and the expectations below are what that function would give
// back if it re-split `"<escaped>"` — which is exactly what the receiving process does.
public class WindowsArgumentTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("with spaces and punctuation!", "with spaces and punctuation!")]
    [InlineData("", "")]
    public void Text_with_nothing_to_escape_is_left_alone(string argument, string expected)
        => WindowsArgument.Escape(argument).Should().Be(expected);

    // The case that was breaking every launch.
    [Fact]
    public void A_quote_becomes_an_escaped_quote()
        => WindowsArgument.Escape("""{ "state": "ready_for_review" }""")
            .Should().Be("""{ \"state\": \"ready_for_review\" }""");

    // A backslash is only special next to a quote, so a path in the middle of an argument stays as
    // the user typed it.
    [Fact]
    public void A_backslash_away_from_a_quote_is_literal()
        => WindowsArgument.Escape(@"C:\Dev\act\src").Should().Be(@"C:\Dev\act\src");

    // The one that eats the closing quote if you forget it: `"C:\dir\"` would escape the quote that
    // was supposed to end the argument.
    [Fact]
    public void Trailing_backslashes_are_doubled_because_a_closing_quote_follows()
    {
        WindowsArgument.Escape(@"C:\dir\").Should().Be(@"C:\dir\\");
        WindowsArgument.Escape(@"C:\dir\\").Should().Be(@"C:\dir\\\\");
    }

    // Backslashes before a quote are doubled *and* the quote gets its own — 2n+1.
    [Theory]
    [InlineData(@"a\""b", @"a\\\""b")]
    [InlineData(@"a\\""b", @"a\\\\\""b")]
    public void Backslashes_before_a_quote_are_doubled_and_the_quote_escaped(
        string argument,
        string expected)
        => WindowsArgument.Escape(argument).Should().Be(expected);

    [Fact]
    public void A_lone_trailing_quote_is_escaped_rather_than_ending_the_argument()
        => WindowsArgument.Escape("text then a quote \"").Should().Be("text then a quote \\\"");

    // What the pty host actually hands over: the escaped content inside its own quotes.
    [Fact]
    public void Quoting_wraps_the_escaped_content()
    {
        WindowsArgument.Quote("plain").Should().Be("\"plain\"");
        WindowsArgument.Quote("""{ "state": ok }""").Should().Be(""""
            "{ \"state\": ok }"
            """".Trim());
    }

    // Round-trip against the reference implementation: whatever we produce, wrapped in the quotes
    // the pty host adds, has to split back into exactly the argument we started with.
    [Theory]
    [InlineData("""{ "state": "needs_input", "question": "what now?" }""")]
    [InlineData(@"C:\Dev\act\")]
    [InlineData("say \"hello\" then stop")]
    [InlineData("a\\\"b\\\\\"c")]
    [InlineData("multi\nline\nwith \"quotes\"")]
    [InlineData("plain")]
    public void Escaping_survives_being_re_split_by_the_platform(string argument)
    {
        var commandLine = $"program {WindowsArgument.Quote(argument)}";

        CommandLineSplitter.Split(commandLine).Should().Equal(["program", argument]);
    }

    // The whole argument list, as the pty host sends it — the case that was broken end to end.
    [Fact]
    public void A_whole_command_line_of_acts_own_arguments_round_trips()
    {
        string[] arguments =
        [
            "--session-id",
            "39694aa9-918e-471b-8623-61d0d948ffbb",
            "--permission-mode",
            "default",
            "## ACT session conventions\n\n    { \"state\": \"ready_for_review\" }\n"
                + "    { \"state\": \"needs_input\", \"question\": \"what you need\" }\n\n"
                + "Say \"hello\" in C:\\Dev\\act\\",
        ];

        var commandLine = string.Join(' ', WindowsArgument.ForCommandLine(arguments));

        CommandLineSplitter.Split(commandLine).Should().Equal(arguments);
    }
}

// A local implementation of the `CommandLineToArgvW` rules, so the test asserts against the
// documented algorithm rather than against the same code it is testing.
internal static class CommandLineSplitter
{
    public static IReadOnlyList<string> Split(string commandLine)
    {
        var arguments = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        var backslashes = 0;
        var started = false;

        void FlushBackslashes(int count)
        {
            current.Append('\\', count);
        }

        foreach (var character in commandLine)
        {
            if (character == '\\')
            {
                backslashes++;
                started = true;

                continue;
            }

            if (character == '"')
            {
                FlushBackslashes(backslashes / 2);

                if (backslashes % 2 == 1)
                    current.Append('"');
                else
                    quoted = !quoted;

                backslashes = 0;
                started = true;

                continue;
            }

            FlushBackslashes(backslashes);
            backslashes = 0;

            if (!quoted && (character == ' ' || character == '\t'))
            {
                if (started)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                    started = false;
                }

                continue;
            }

            current.Append(character);
            started = true;
        }

        FlushBackslashes(backslashes);

        if (started)
            arguments.Add(current.ToString());

        return arguments;
    }
}
