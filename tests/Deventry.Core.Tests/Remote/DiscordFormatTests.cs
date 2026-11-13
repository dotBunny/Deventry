using Deventry.Core.Remote.Discord;

namespace Deventry.Core.Tests.Remote;

/// <summary>Discord's Markdown (DESIGN.md §18, "Discord's limits").</summary>
public class DiscordFormatTests
{
    private static readonly DiscordFormat Format = DiscordFormat.Instance;

    [Fact]
    public void What_Discord_reads_as_formatting_anywhere_is_escaped()
    {
        Assert.Equal(@"a \*b\* \_c\_ \~\~d\~\~ \`e\` \|\|f\|\| \<@1> \[g](h) \\", Format.Escape(@"a *b* _c_ ~~d~~ `e` ||f|| <@1> [g](h) \"));
    }

    [Theory]
    [InlineData("> not a quote", @"\> not a quote")]
    [InlineData("# not a heading", @"\# not a heading")]
    [InlineData("- not a list", @"\- not a list")]
    [InlineData("-# not small", @"\-# not small")]
    [InlineData("1. not numbered", @"1\. not numbered")]
    [InlineData("  + nor this", @"  \+ nor this")]
    [InlineData("a - b # c > d 2. e", "a - b # c > d 2. e")]
    public void What_makes_a_quote_a_heading_or_a_list_is_escaped_only_at_a_lines_start(string text, string escaped)
    {
        Assert.Equal(escaped, Format.Escape(text));
    }

    [Fact]
    public void Bold_italic_and_code_wrap_their_text()
    {
        Assert.Equal(@"**Grappling\_hook**", Format.Bold("Grappling_hook"));
        Assert.Equal("*From the planner*", Format.Italic("From the planner"));
        Assert.Equal("`Assets/Hook.cs`", Format.Code("Assets/Hook.cs"));
        Assert.Equal("`` a`b ``", Format.Code("a`b"));
    }

    [Fact]
    public void A_code_block_keeps_its_language_and_cannot_be_ended_from_inside()
    {
        Assert.Equal("```bash\nnpm test\n```", Format.CodeBlock("npm test", "bash"));
        Assert.Equal("```\nx\n```", Format.CodeBlock("x", "no spaces allowed"));
        Assert.DoesNotContain("\n```\n", Format.CodeBlock("before\n```\nafter")[3..^4], StringComparison.Ordinal);
    }

    [Fact]
    public void A_quote_marks_each_line()
    {
        Assert.Equal("> Hello\n> \\*there\\*", Format.Quote("Hello\r\n*there*"));
    }

    [Fact]
    public void A_reply_goes_as_it_is_but_for_headings_Discord_cannot_show()
    {
        Assert.Equal("## Plan\n- **one**\n**Detail**\n```bash\n#### not a heading here\n```",
            Format.FromMarkdown("## Plan\r\n- **one**\n#### Detail\n```bash\n#### not a heading here\n```"));
    }

    [Fact]
    public void A_code_block_left_open_is_closed_and_tildes_become_backticks()
    {
        Assert.Equal("```\nline one\n```", Format.FromMarkdown("~~~\nline one"));
    }

    [Fact]
    public void A_message_fits_Discords_2000_characters()
    {
        Assert.True(Format.FitsMessage(new string('a', 2000)));
        Assert.False(Format.FitsMessage(new string('a', 2001)));
    }
}
