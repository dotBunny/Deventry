using Deventry.Core.Remote.Chat;
using Deventry.Core.Remote.Telegram;

namespace Deventry.Core.Tests.Remote;

/// <summary>What every chat service's messages share: the start of a long reply, and the bot's buttons (DESIGN.md §18).</summary>
public class ChatFormatTests
{
    [Fact]
    public void A_short_reply_is_all_there()
    {
        Assert.Equal(("All done.", false), ChatFormat.FirstLines("All done.\n"));
    }

    [Fact]
    public void A_long_reply_keeps_its_first_whole_lines()
    {
        var reply = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"Line {i} of the reply, which goes on a while."));

        var (start, cut) = ChatFormat.FirstLines(reply, 200);

        Assert.True(cut);
        Assert.True(start.Length <= 200);
        Assert.StartsWith("Line 1 of", start);
        Assert.EndsWith("goes on a while.", start);
    }

    [Fact]
    public void A_long_first_line_is_cut_at_a_word()
    {
        var (start, cut) = ChatFormat.FirstLines(string.Join(' ', Enumerable.Repeat("word", 300)), 100);

        Assert.True(cut);
        Assert.EndsWith("word…", start);
        Assert.True(start.Length <= 100);
    }

    [Fact]
    public void Button_data_carries_its_run_and_fits_every_services_limit()
    {
        var data = ChatFormat.ButtonData("ab3x", ChatFormat.Key(long.MaxValue));

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(data) <= TelegramBotApi.ButtonDataLimit);
        Assert.Equal(("ab3x", ChatFormat.Key(long.MaxValue)), ChatFormat.ParseButtonData(data));
        Assert.Null(ChatFormat.ParseButtonData("nonsense"));
        Assert.Null(ChatFormat.ParseButtonData(null));
    }

    [Fact]
    public void Keys_are_short_and_different()
    {
        Assert.Equal("1", ChatFormat.Key(1));
        Assert.Equal("10", ChatFormat.Key(36));
        Assert.Equal(1000, Enumerable.Range(1, 1000).Select(i => ChatFormat.Key(i)).Distinct().Count());
    }
}
