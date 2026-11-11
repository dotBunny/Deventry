using Deventry.Core.Media;

namespace Deventry.Core.Tests.Media;

/// <summary>Which files are media, and the paths to them a message, a reply or a command names (DESIGN.md §18, "Media").</summary>
public sealed class MediaFilesTests
{
    [Theory]
    [InlineData("shot.png", MediaKind.Image)]
    [InlineData("Shot.JPEG", MediaKind.Image)]
    [InlineData("logo.svg", MediaKind.Image)]
    [InlineData("clip.mp4", MediaKind.Video)]
    [InlineData("C:/Captures/run.MOV", MediaKind.Video)]
    [InlineData("hit.wav", MediaKind.Audio)]
    [InlineData("theme.ogg", MediaKind.Audio)]
    public void Media_is_known_by_its_extension(string path, MediaKind kind) => Assert.Equal(kind, MediaFiles.KindOf(path));

    [Theory]
    [InlineData("notes.md")]
    [InlineData("report.pdf")]
    [InlineData("setup.exe")]
    [InlineData("png")]
    [InlineData("shots/")]
    public void Anything_else_isnt_media(string path) => Assert.Null(MediaFiles.KindOf(path));

    [Fact]
    public void A_message_names_media_by_path_name_mention_quote_and_link()
    {
        var text = """
            The menu is wrong in shots/menu.png and @docs/bug.png, see "My Shots/wide.png".
            Compare `before.jpg` with C:\Captures\after.webp; the clip is ~/Desktop/run.mp4.
            ![chart](out/chart.png) and @"Sound Bank/hit.wav" too.
            """;

        var paths = MediaFiles.InText(text).Select(r => r.Path).ToArray();

        Assert.Equal(
            ["shots/menu.png", "docs/bug.png", "My Shots/wide.png", "before.jpg", @"C:\Captures\after.webp", "~/Desktop/run.mp4", "out/chart.png", "Sound Bank/hit.wav"],
            paths);
    }

    [Fact]
    public void Words_in_a_quote_that_isnt_a_path_still_count()
    {
        Assert.Equal(["shot.png"], MediaFiles.InText("He said \"see shot.png\" earlier.").Select(r => r.Path));
    }

    [Fact]
    public void Each_file_is_named_once()
    {
        Assert.Equal(["shot.png"], MediaFiles.InText("shot.png, then shot.png again, and `shot.png`.").Select(r => r.Path));
    }

    [Theory]
    [InlineData("https://example.com/shot.png")]
    [InlineData(@"\\server\share\shot.png")]
    [InlineData("//server/share/shot.png")]
    [InlineData("*.png")]
    [InlineData("the notes.md and report.pdf")]
    [InlineData("")]
    public void Web_addresses_network_paths_globs_and_other_files_arent_named(string text) => Assert.Empty(MediaFiles.InText(text));

    [Fact]
    public void A_file_address_is_its_path()
    {
        var reference = Assert.Single(MediaFiles.InText(OperatingSystem.IsWindows() ? "file:///C:/Shots/a.png" : "file:///tmp/a.png"));

        Assert.Equal(OperatingSystem.IsWindows() ? @"C:\Shots\a.png" : "/tmp/a.png", reference.Path);
    }

    [Fact]
    public void A_long_text_gives_at_most_the_limit()
    {
        var text = string.Join(' ', Enumerable.Range(0, 50).Select(i => $"shot{i}.png"));

        Assert.Equal(MediaFiles.MaxPerText, MediaFiles.InText(text).Count);
    }

    [Theory]
    [InlineData("ffmpeg -i in.mp4 -vf scale=320:-1 out.gif", new[] { "in.mp4", "out.gif" })]
    [InlineData("python plot.py --out=charts/chart.png", new[] { "charts/chart.png" })]
    [InlineData("convert \"a b.png\" 'c d.jpg' e.webp", new[] { "a b.png", "c d.jpg", "e.webp" })]
    [InlineData("echo done > shot.png && ls", new[] { "shot.png" })]
    [InlineData("dotnet test", new string[0])]
    public void A_command_names_the_media_it_reads_or_writes(string command, string[] paths) =>
        Assert.Equal(paths, MediaFiles.InCommand(command).Select(r => r.Path));
}
