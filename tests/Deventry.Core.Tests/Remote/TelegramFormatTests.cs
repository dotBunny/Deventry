using Deventry.Core.Remote.Telegram;

namespace Deventry.Core.Tests.Remote;

/// <summary>Telegram's HTML (DESIGN.md §18, "Telegram's limits").</summary>
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
}
