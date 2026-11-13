using Deventry.Core.Remote.Telegram;

namespace Deventry.Core.Tests.Remote;

/// <summary>Telegram's HTML and the bot's buttons (DESIGN.md §18, "Telegram's limits").</summary>
public class TelegramFormatTests
{
    [Fact]
    public void Text_is_escaped_for_Telegrams_HTML()
    {
        Assert.Equal("a &lt;b&gt; &amp; c", TelegramFormat.Escape("a <b> & c"));
    }

    [Fact]
    public void A_replys_code_bold_and_links_are_kept_and_the_rest_is_plain()
    {
        var html = TelegramFormat.FromMarkdown("Use `npm test` and **then** see [the docs](https://example.com/a?b=1&c=2) <now>.\n_not italic_");

        Assert.Equal(
            "Use <code>npm test</code> and <b>then</b> see <a href=\"https://example.com/a?b=1&amp;c=2\">the docs</a> &lt;now&gt;.\n_not italic_",
            html);
    }

    [Fact]
    public void A_code_block_keeps_its_language_and_escapes_its_text()
    {
        var html = TelegramFormat.FromMarkdown("Run:\n```bash\nif [ a < b ]; then echo **x**; fi\n```\nDone.");

        Assert.Equal("Run:\n<pre><code class=\"language-bash\">if [ a &lt; b ]; then echo **x**; fi</code></pre>\nDone.", html);
    }

    [Fact]
    public void A_code_block_left_open_runs_to_the_end()
    {
        Assert.Equal("<pre>line one\nline two</pre>", TelegramFormat.FromMarkdown("```\nline one\nline two"));
    }

    [Fact]
    public void Headings_are_bold_and_bullets_are_dots()
    {
        Assert.Equal("<b>Plan</b>\n• one\n  • two", TelegramFormat.FromMarkdown("## Plan\n- one\n  * two"));
    }

    [Fact]
    public void Only_web_links_become_links()
    {
        Assert.Equal("[x](javascript:alert(1))", TelegramFormat.FromMarkdown("[x](javascript:alert(1))"));
    }

    [Fact]
    public void A_short_reply_is_all_there()
    {
        Assert.Equal(("All done.", false), TelegramFormat.FirstLines("All done.\n"));
    }

    [Fact]
    public void A_long_reply_keeps_its_first_whole_lines()
    {
        var reply = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"Line {i} of the reply, which goes on a while."));

        var (start, cut) = TelegramFormat.FirstLines(reply, 200);

        Assert.True(cut);
        Assert.True(start.Length <= 200);
        Assert.StartsWith("Line 1 of", start);
        Assert.EndsWith("goes on a while.", start);
    }

    [Fact]
    public void A_long_first_line_is_cut_at_a_word()
    {
        var (start, cut) = TelegramFormat.FirstLines(string.Join(' ', Enumerable.Repeat("word", 300)), 100);

        Assert.True(cut);
        Assert.EndsWith("word…", start);
        Assert.True(start.Length <= 100);
    }

    [Fact]
    public void Button_data_carries_its_run_and_fits_Telegrams_64_bytes()
    {
        var data = TelegramFormat.ButtonData("ab3x", TelegramFormat.Key(long.MaxValue));

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(data) <= TelegramBotApi.ButtonDataLimit);
        Assert.Equal(("ab3x", TelegramFormat.Key(long.MaxValue)), TelegramFormat.ParseButtonData(data));
        Assert.Null(TelegramFormat.ParseButtonData("nonsense"));
        Assert.Null(TelegramFormat.ParseButtonData(null));
    }

    [Fact]
    public void Keys_are_short_and_different()
    {
        Assert.Equal("1", TelegramFormat.Key(1));
        Assert.Equal("10", TelegramFormat.Key(36));
        Assert.Equal(1000, Enumerable.Range(1, 1000).Select(i => TelegramFormat.Key(i)).Distinct().Count());
    }
}
