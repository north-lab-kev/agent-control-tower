using System.Globalization;
using Act.Core.Agents;
using AwesomeAssertions;

namespace Act.Core.Tests;

// The culture is pinned in every case because the header is localised: a test reading the ambient
// culture would pass or fail per machine.
public class AttachmentInstructionTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr");

    [Fact]
    public void A_task_with_no_files_keeps_its_prompt_verbatim()
        => In(English, () => AttachmentInstruction.Append("do the thing", []).Should().Be("do the thing"));

    [Fact]
    public void Every_path_is_listed_after_the_prompt_one_per_line()
        => In(English, () =>
        {
            var composed = AttachmentInstruction.Append(
                "do the thing",
                [@"C:\act\attachments\a\spec.md", @"C:\act\attachments\a\trace.log"]);

            composed.Should().StartWith("do the thing");
            composed.Should().Contain("Files attached to this task, by absolute path:");
            composed.Should().EndWith("spec.md\nC:\\act\\attachments\\a\\trace.log");
        });

    // Paths, never contents: the prompt is a positional command-line argument and Windows caps one
    // at ~32,767 characters, so a file's bytes have no route through here.
    [Fact]
    public void The_prompt_carries_the_path_and_nothing_the_file_contains()
        => In(English, () => AttachmentInstruction.Append("do the thing", [@"C:\act\a\notes.txt"])
            .Should().HaveLength("do the thing".Length
                + "\n\nFiles attached to this task, by absolute path:\n".Length
                + @"C:\act\a\notes.txt".Length));

    [Fact]
    public void The_header_follows_the_ui_language()
        => In(French, () => AttachmentInstruction.Append("fais la chose", [@"C:\act\a\notes.txt"])
            .Should().Contain("Fichiers joints à cette tâche, par chemin absolu :"));

    private static void In(CultureInfo culture, Action assert)
    {
        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = culture;

        try
        {
            assert();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
