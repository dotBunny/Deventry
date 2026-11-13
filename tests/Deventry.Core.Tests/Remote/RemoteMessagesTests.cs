using Deventry.Core.Remote;

namespace Deventry.Core.Tests.Remote;

/// <summary>What remote clients' messages, and the tabs' notifications, say (DESIGN.md §18, "What messages say").</summary>
public class RemoteMessagesTests
{
    [Fact]
    public void A_command_prompt_reads_as_its_title_and_the_command()
    {
        Assert.Equal("Allow this command? npm test", RemoteMessages.PromptLine(FakeRemoteDesk.Command()));
    }

    [Fact]
    public void A_question_and_a_plan_name_the_agent()
    {
        var question = new RemotePrompt { Id = "q", Kind = RemotePromptKind.Question, Title = "Claude has a question", About = "Which database?" };
        var plan = new RemotePrompt { Id = "p", Kind = RemotePromptKind.Plan, Title = "Claude's plan" };

        Assert.Equal("Codex has a question: Which database?", RemoteMessages.PromptLine(question, "Codex"));
        Assert.Equal("Claude has a plan for you to review.", RemoteMessages.PromptLine(plan));
    }

    [Fact]
    public void A_threads_message_names_the_sub_thread()
    {
        var prompt = new RemotePrompt { Id = "t", Kind = RemotePromptKind.ThreadMessage, Title = "Send this to Explore?", About = "Look at it", Recipient = "Explore" };

        Assert.Equal("Claude wants to send a message to Explore.", RemoteMessages.PromptLine(prompt));
    }

    [Theory]
    [InlineData("3 of 5 tasks done", "Done.\nMore", "3 of 5 tasks done · Done.")]
    [InlineData("3 of 5 tasks done", null, "3 of 5 tasks done")]
    [InlineData(null, "  First line  \nSecond", "First line")]
    [InlineData(null, "", "Claude finished its turn.")]
    public void A_finished_turn_is_the_tasks_then_the_replys_first_line(string? tasks, string? reply, string expected)
    {
        Assert.Equal(expected, RemoteMessages.TurnLine(tasks, reply));
    }

    [Fact]
    public void A_closed_prompt_says_how_and_when()
    {
        var at = new DateTimeOffset(2026, 11, 12, 14, 9, 0, TimeSpan.Zero);

        Assert.Equal("Allowed from Telegram at 14:09", RemoteMessages.Closed(RemotePromptEnd.AnsweredFromClient, "Allowed", "Telegram", at, TimeZoneInfo.Utc));
        Assert.Equal("Denied. Answered at the desk at 14:09", RemoteMessages.Closed(RemotePromptEnd.AnsweredAtDesk, "Denied", null, at, TimeZoneInfo.Utc));
        Assert.Equal("No longer needed", RemoteMessages.Closed(RemotePromptEnd.Withdrawn, "No longer needed", null, at, TimeZoneInfo.Utc));
    }

    [Fact]
    public void A_working_tabs_status_says_what_it_is_doing_and_for_how_long()
    {
        var now = new DateTimeOffset(2026, 11, 12, 14, 0, 0, TimeSpan.Zero);
        var tab = FakeRemoteDesk.Tab("t", "Grappling hook", RemoteTabStatus.Working) with
        {
            Doing = "Reading Player.cs",
            Since = now - TimeSpan.FromSeconds(80),
            Tasks = new RemoteTaskProgress(3, 5),
        };

        Assert.Equal("Working · Reading Player.cs · 1m 20s · 3 of 5 tasks done", RemoteMessages.StatusLine(tab, now));
    }

    [Fact]
    public void Usage_is_a_line_for_each_meter_and_account()
    {
        var now = new DateTimeOffset(2026, 11, 12, 14, 0, 0, TimeSpan.Zero);
        var usage = new RemoteUsage(
            [new RemoteUsageMeter("Session", 42, now.AddHours(1)), new RemoteUsageMeter("Week", 18, null)],
            [new RemoteSpend("Studio", "$3.20")]);

        Assert.Equal(["Session: 42% used, resets at 15:00", "Week: 18% used", "Studio: $3.20 today"], RemoteMessages.Usage(usage, now, TimeZoneInfo.Utc));
        Assert.Single(RemoteMessages.Usage(RemoteUsage.None, now, TimeZoneInfo.Utc));
    }
}
