using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace Act.App.UiTests;

// A release page lists both platforms' assets in one flat list, so every asset has to say which OS
// it belongs to: the installers say it in the filename electron-builder renders, and the two update
// feeds — whose names electron-updater fixes — say it in the GitHub asset label the release job
// attaches. Nothing compiles against either file, and the release workflow is manual and slow, so a
// rename on one side and not the other would surface as a failed release at best and a mislabelled
// public download at worst.
public class ReleaseAssetNamingTests
{
    [Fact]
    public void The_installer_names_carry_the_operating_system()
    {
        WindowsArtifactTemplate().Should().Contain("win");
        LinuxArtifactTemplate().Should().Contain("linux");
    }

    [Fact]
    public void The_release_workflow_looks_for_the_name_electron_builder_renders_on_windows()
    {
        var expected = Render(WindowsArtifactTemplate(), arch: "x64", ext: "exe");

        expected.Should().Be("ACT-Setup-$env:VERSION-win-x64.exe");
        ReleaseWorkflow().Should().Contain($"$expected = \"{expected}\"");
    }

    [Fact]
    public void The_release_workflow_looks_for_the_name_electron_builder_renders_on_linux()
    {
        // The arch is globbed rather than pinned, matching the workflow: electron-builder renders it
        // uname-style for an AppImage, and a cosmetic change there must not fail a release.
        var expected = Render(LinuxArtifactTemplate(), arch: "*", ext: "AppImage");

        expected.Should().Be("ACT-$env:VERSION-linux-*.AppImage");
        ReleaseWorkflow().Should().Contain($"-Filter \"{expected}\"");
    }

    [Fact]
    public void Every_published_asset_is_labelled_with_its_operating_system()
    {
        var labels = AssetLabels();

        labels.Should().HaveCount(5);
        labels.Should().OnlyContain(pair => pair.Value.StartsWith("Windows") || pair.Value.StartsWith("Linux"));

        // The two the filename cannot help with.
        labels.Should().Contain(pair => pair.Key.Contains("'latest.yml'") && pair.Value.StartsWith("Windows"));
        labels.Should().Contain(pair => pair.Key.Contains("'latest-linux.yml'") && pair.Value.StartsWith("Linux"));
    }

    [Fact]
    public void The_asset_labels_are_ascii()
    {
        foreach (var (_, label) in AssetLabels())
            label.Should().MatchRegex("^[\\x20-\\x7E]+$");
    }

    private static IReadOnlyDictionary<string, string> AssetLabels()
    {
        var workflow = ReleaseWorkflow();

        var assignments = Regex.Matches(workflow, @"\$assets\[(?<key>[^\]]+)\]\s*=\s*'(?<label>[^']+)'")
            .Select(match => (Key: match.Groups["key"].Value, Label: match.Groups["label"].Value));

        var literal = Regex.Matches(workflow, @"^\s*(?<key>\(Join-Path[^)]+\))\s*=\s*'(?<label>[^']+)'\s*$", RegexOptions.Multiline)
            .Select(match => (Key: match.Groups["key"].Value, Label: match.Groups["label"].Value));

        return assignments.Concat(literal).ToDictionary(entry => entry.Key, entry => entry.Label);
    }

    private static string WindowsArtifactTemplate() => ArtifactTemplate("nsis");

    private static string LinuxArtifactTemplate() => ArtifactTemplate("linux");

    private static string ArtifactTemplate(string section)
    {
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip };

        using var document = JsonDocument.Parse(File.ReadAllText(ElectronBuilderPath()), options);

        return document.RootElement.GetProperty(section).GetProperty("artifactName").GetString()!;
    }

    private static string Render(string template, string arch, string ext)
        => template.Replace("${version}", "$env:VERSION").Replace("${arch}", arch).Replace("${ext}", ext);

    private static string ReleaseWorkflow() => ReleaseWorkflowFile.Text();

    private static string ElectronBuilderPath()
        => Path.Combine(ReleaseWorkflowFile.RepositoryRoot(), "src", "Act.App", "Properties", "electron-builder.json");
}
