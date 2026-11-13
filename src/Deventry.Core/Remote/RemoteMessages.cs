using System.Globalization;

namespace Deventry.Core.Remote;

/// <summary>
/// What remote clients' messages say (DESIGN.md §18, "What messages say"), shared so every client says the same thing,
/// and the same as the tabs' OS notifications (§10), which use it too. Each client only formats it.
/// </summary>
public static class RemoteMessages
{
    /// <summary>How much of a reply a notification shows.</summary>
    public const int NotificationLength = 140;

    // ---- Prompts ------------------------------------------------------------------------------------------------

    /// <summary>"Claude has a question", or "Claude has some questions".</summary>
    public static string QuestionTitle(string agent, int questions = 1) => questions > 1 ? $"{agent} has some questions" : $"{agent} has a question";

    public static string PlanTitle(string agent) => $"{agent} has a plan for you to review.";

    /// <summary>"Send this to Explore?": a thread's message to one of its sub-threads.</summary>
    public static string ThreadMessageTitle(string subThread) => $"Send this to {subThread}?";

    public static string AtDesk => "Answer this at the desk.";

    /// <summary>
    /// What a waiting prompt asks, in a line: the notification's text. "Allow this command? npm test", "Claude has a
    /// question: Which database?".
    /// </summary>
    public static string PromptLine(RemotePrompt prompt, string agent = "Claude") => prompt.Kind switch
    {
        RemotePromptKind.Question => prompt.About is { Length: > 0 } question
            ? $"{QuestionTitle(agent)}: {Shorten(question, 120)}"
            : $"{QuestionTitle(agent)}.",
        RemotePromptKind.Plan => PlanTitle(agent),
        RemotePromptKind.ThreadMessage => $"{agent} wants to send a message to {prompt.Recipient ?? "a sub-thread"}.",
        _ => prompt.About is { Length: > 0 } about ? $"{prompt.Title} {Shorten(about, 120)}" : prompt.Title,
    };

    // ---- Turns and errors ---------------------------------------------------------------------------------------

    /// <summary>A finished turn, in a line: where the task list stands, then the reply's first line.</summary>
    public static string TurnLine(string? tasks, string? reply, string agent = "Claude")
    {
        var first = reply?.Trim() is { Length: > 0 } text ? text.Split('\n')[0].Trim() : null;
        return (tasks, first) switch
        {
            ({ } t, { Length: > 0 } line) => Shorten($"{t} · {line}", NotificationLength),
            ({ } t, _) => t,
            (null, { Length: > 0 } line) => Shorten(line, NotificationLength),
            _ => $"{agent} finished its turn.",
        };
    }

    /// <summary>"Claude Code stopped unexpectedly (exit code 3)."</summary>
    public static string StoppedUnexpectedly(int? exitCode) => exitCode is { } code
        ? $"Claude Code stopped unexpectedly (exit code {code})."
        : "Claude Code stopped unexpectedly.";

    public static string SignInAtDesk => "Claude Code needs you to sign in again. Sign in at the desk.";

    // ---- Just what happened -------------------------------------------------------------------------------------

    public static string NeedsYou(string tab) => $"{tab} needs you.";

    public static string Finished(string tab) => $"{tab} finished.";

    public static string Stopped(string tab) => $"{tab} stopped with an error.";

    // ---- The desk ---------------------------------------------------------------------------------------------------

    public static string Paired(string machine) => $"Paired with Deventry on {machine}.";

    public static string Closing(string machine) => $"Deventry on {machine} is closing. Its tabs are kept, and I'll answer again when it's back.";

    public static string Back(string machine) => $"Deventry on {machine} is back.";

    /// <summary>"Passed over 3 messages sent while Deventry was closed."</summary>
    public static string PassedOver(int count) => count == 1
        ? "Passed over 1 message sent while Deventry was closed. Send it again if it's still wanted."
        : $"Passed over {count} messages sent while Deventry was closed. Send them again if they're still wanted.";

    /// <summary>The line a closed prompt's message gets: "Allowed from Telegram at 14:09", "Answered at the desk at 14:31".</summary>
    public static string Closed(RemotePromptEnd how, string outcome, string? client, DateTimeOffset at, TimeZoneInfo zone)
    {
        var time = TimeZoneInfo.ConvertTime(at, zone).ToString("t", CultureInfo.CurrentCulture);
        return how switch
        {
            RemotePromptEnd.AnsweredFromClient => $"{outcome} from {client ?? "a remote client"} at {time}",
            RemotePromptEnd.AnsweredAtDesk => $"{outcome}. Answered at the desk at {time}",
            _ => outcome is { Length: > 0 } and not WithdrawnOutcome ? outcome : WithdrawnOutcome,
        };
    }

    /// <summary>What a prompt its agent withdrew says, as its card does.</summary>
    public const string WithdrawnOutcome = "No longer needed";

    /// <summary>What a card answered from a client says under its outcome: "Answered from Telegram".</summary>
    public static string AnsweredFrom(string client) => $"Answered from {client}";

    /// <summary>A client tried a prompt that's closed.</summary>
    public static string AlreadyAnswered(RemotePromptEnd how, string? client) => how switch
    {
        RemotePromptEnd.AnsweredFromClient => $"Already answered from {client ?? "another remote client"}.",
        RemotePromptEnd.Withdrawn => "That's no longer needed.",
        _ => "Already answered at the desk.",
    };

    // ---- Tabs -------------------------------------------------------------------------------------------------------

    public static string TabGone => "That tab is closed.";

    public static string Status(RemoteTabStatus status) => status switch
    {
        RemoteTabStatus.NotStarted => "Not started",
        RemoteTabStatus.Starting => "Starting",
        RemoteTabStatus.Idle => "Idle",
        RemoteTabStatus.Working => "Working",
        RemoteTabStatus.NeedsInput => "Needs you",
        RemoteTabStatus.Unread => "Finished",
        RemoteTabStatus.Error => "Stopped with an error",
        _ => "Exited",
    };

    /// <summary>"Working · Reading Player.cs · 2m 10s".</summary>
    public static string StatusLine(RemoteTab tab, DateTimeOffset now)
    {
        var parts = new List<string> { Status(tab.Status) };
        if (tab.Status == RemoteTabStatus.Working && tab.Doing is { Length: > 0 } doing)
        {
            parts.Add(doing);
        }
        if (tab.Status is RemoteTabStatus.Working or RemoteTabStatus.NeedsInput && tab.Since is { } since && now > since)
        {
            parts.Add(Formats.Elapsed(now - since));
        }
        if (tab.Tasks is { Total: > 0 } tasks)
        {
            parts.Add($"{tasks.Done} of {tasks.Total} tasks done");
        }
        return string.Join(" · ", parts);
    }

    /// <summary>The usage, a line each: "Session: 42% used, resets at 15:00".</summary>
    public static IReadOnlyList<string> Usage(RemoteUsage usage, DateTimeOffset now, TimeZoneInfo zone)
    {
        var lines = new List<string>();
        foreach (var meter in usage.Meters)
        {
            var resets = meter.ResetsAt is { } at ? $", resets {Resets(at, now, zone)}" : "";
            lines.Add($"{meter.Name}: {meter.Percent:0}% used{resets}");
        }
        foreach (var spend in usage.Spend)
        {
            lines.Add($"{spend.Account}: {spend.Today} today");
        }
        if (lines.Count == 0)
        {
            lines.Add("No plan usage to show: Deventry hasn't read it yet, or the tabs use an API key.");
        }
        return lines;
    }

    private static string Resets(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(at, zone);
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        var time = local.ToString("t", CultureInfo.CurrentCulture);
        return local.Date == today ? $"at {time}" : $"{local.ToString("ddd", CultureInfo.CurrentCulture)} at {time}";
    }

    /// <summary>Cuts text to about <paramref name="max"/> characters, at a word boundary where there is one, with an ellipsis.</summary>
    public static string Shorten(string text, int max)
    {
        if (text.Length <= max)
        {
            return text;
        }
        var cut = text[..max];
        var space = cut.LastIndexOf(' ');
        return (space > max / 2 ? cut[..space] : cut[..(max - 1)]).TrimEnd() + "…";
    }
}
