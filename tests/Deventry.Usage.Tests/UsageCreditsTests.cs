using Deventry.Usage.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.Usage.Tests;

/// <summary>When usage credits pay for Claude, and what the notice says (DESIGN.md §6, "Usage credits").</summary>
public sealed class UsageCreditsTests
{
    private static readonly DateTimeOffset Now = UsageFixtures.RecordedAt;
    private static readonly DateTimeOffset SessionResets = Now.AddHours(2);

    private static readonly UsageCredits On = new(true, 1240, 5000, "USD", Now);

    [Fact]
    public void Claude_Code_saying_credits_pay_is_believed_until_the_limit_it_named_resets()
    {
        var snapshot = Session(100) with { CreditsReport = new CreditsReport(true, "five_hour", SessionResets, Now) };

        var use = CreditUse.From(snapshot, Now);

        Assert.NotNull(use);
        Assert.Equal("session limit", use.LimitName);
        Assert.Equal(SessionResets, use.Until);
        Assert.Null(CreditUse.From(snapshot, SessionResets));
    }

    [Fact]
    public void Claude_Code_names_the_limit_even_one_the_poll_doesnt_count()
    {
        // A model's own limit: only Claude Code can say its requests are paying.
        var snapshot = UsageFixtures.Snapshot(Now, 40, SessionResets, weekly: 60, fable: 100) with
        {
            Credits = On,
            CreditsReport = new CreditsReport(true, "seven_day_opus", UsageFixtures.WeekResetsAt, Now.AddMinutes(1)),
        };

        Assert.Equal("Opus limit", CreditUse.From(snapshot, Now.AddMinutes(1))?.LimitName);
        Assert.Null(CreditUse.From(snapshot with { CreditsReport = null }, Now.AddMinutes(1)));
    }

    [Fact]
    public void Without_a_resets_at_it_lasts_until_the_used_up_limit_resets()
    {
        var snapshot = Session(100) with { CreditsReport = new CreditsReport(true, "five_hour", null, Now) };

        Assert.Equal(SessionResets, CreditUse.From(snapshot, Now)?.Until);
        Assert.Null(CreditUse.From(snapshot, SessionResets));
    }

    [Fact]
    public void Without_a_word_from_Claude_Code_credits_that_are_on_pay_once_the_session_or_the_week_is_used_up()
    {
        Assert.Null(CreditUse.From(Session(99.5) with { Credits = On }, Now));

        var session = CreditUse.From(Session(100) with { Credits = On }, Now);
        Assert.Equal("session limit", session?.LimitName);
        Assert.Equal(SessionResets, session?.Until);
        Assert.Same(On, session?.Credits);

        // Both used up: credits pay until the later one resets.
        var both = CreditUse.From(UsageFixtures.Snapshot(Now, 100, SessionResets, weekly: 100) with { Credits = On }, Now);
        Assert.Equal("weekly limit", both?.LimitName);
        Assert.Equal(UsageFixtures.WeekResetsAt, both?.Until);

        Assert.Null(CreditUse.From(UsageFixtures.Snapshot(Now, 40, SessionResets, fable: 100) with { Credits = On }, Now));
        Assert.Null(CreditUse.From(Session(100) with { Credits = On with { IsEnabled = false } }, Now));
        Assert.Null(CreditUse.From(Session(100), Now));
        Assert.Null(CreditUse.From(Session(100) with { Credits = On }, SessionResets));
        Assert.Null(CreditUse.From(null, Now));
    }

    [Fact]
    public void The_newer_word_decides()
    {
        var paying = new CreditsReport(true, "five_hour", SessionResets, Now);
        var notPaying = paying with { IsUsing = false };

        // Credits turned off, or their cap reached, after Claude Code said they were paying.
        Assert.Null(CreditUse.From(Session(100) with { Credits = On with { IsEnabled = false, At = Now.AddMinutes(1) }, CreditsReport = paying }, Now.AddMinutes(1)));
        // Off before it said they were paying: Claude Code's word is newer.
        Assert.NotNull(CreditUse.From(Session(100) with { Credits = On with { IsEnabled = false, At = Now.AddMinutes(-1) }, CreditsReport = paying }, Now));

        // Claude Code said they aren't paying after the last poll.
        Assert.Null(CreditUse.From(Session(100) with { Credits = On, CreditsReport = notPaying with { At = Now.AddMinutes(1) } }, Now.AddMinutes(1)));
        // It said so before the poll that found the session used up, as when it's used up elsewhere, on claude.ai.
        Assert.NotNull(CreditUse.From(Session(100) with { Credits = On with { At = Now.AddMinutes(1) }, CreditsReport = notPaying }, Now.AddMinutes(1)));
        // It said so of a request let through past the limit, which doesn't say they stopped: the poll decides.
        var pastTheLimit = notPaying with { At = Now.AddMinutes(1), StillUsedUp = true };
        Assert.NotNull(CreditUse.From(Session(100) with { Credits = On, CreditsReport = pastTheLimit }, Now.AddMinutes(1)));
        Assert.Null(CreditUse.From(Session(100) with { CreditsReport = pastTheLimit }, Now.AddMinutes(1)));
    }

    [Fact]
    public void The_notice_says_which_limit_until_when_and_what_has_been_spent()
    {
        var clock = new FakeTimeProvider(Now);

        Assert.Equal(
            "Your session limit is used up, so your usage credits pay for Claude until it resets at 20:00. $12.40 of $50.00 spent this month.",
            new CreditUse("five_hour", SessionResets, On).Describe(clock));
        Assert.Equal(
            "Your weekly limit is used up, so your usage credits pay for Claude until it resets on Wed at 11:00. $12.40 spent this month.",
            new CreditUse("seven_day", UsageFixtures.WeekResetsAt, On with { MonthlyLimit = null }).Describe(clock));
        Assert.Equal(
            "Your usage limit is used up, so your usage credits pay for Claude.",
            new CreditUse(null, null, On with { Used = null }).Describe(clock));
        Assert.Equal(
            "Your session limit is used up, so your usage credits pay for Claude until it resets at 20:00.",
            new CreditUse("five_hour", SessionResets, null).Describe(clock));
    }

    [Theory]
    [InlineData(1240, "USD", "$12.40")]
    [InlineData(500, null, "$5.00")]
    [InlineData(800, "eur", "€8.00")]
    [InlineData(1999, "GBP", "£19.99")]
    [InlineData(1999, "CAD", "19.99 CAD")]
    [InlineData(1200, "JPY", "1200 JPY")]
    public void Amounts_are_in_the_currencys_minor_units(double minor, string? currency, string expected) =>
        Assert.Equal(expected, UsageCredits.Amount(minor, currency));

    private static UsageSnapshot Session(double percent) => UsageFixtures.Snapshot(Now, percent, SessionResets);
}
