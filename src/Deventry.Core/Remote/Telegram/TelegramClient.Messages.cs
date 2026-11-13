using System.Globalization;
using System.Text;
using static Deventry.Core.Remote.Telegram.TelegramFormat;

namespace Deventry.Core.Remote.Telegram;

/// <summary>The bot's messages as Telegram's HTML, and their buttons (DESIGN.md §18, "What the bot sends").</summary>
public sealed partial class TelegramClient
{
    /// <summary>How much of a command, a question or a file a prompt's message shows.</summary>
    private const int AboutLength = 3000;

    /// <summary>How many changed files a message lists.</summary>
    private const int FilesListed = 30;

    private static string HelpHtml() => string.Join("\n",
        "Reply to any of my messages to send it to that message's tab, or pick a tab with /to and just write.",
        "",
        "/tabs: the tabs and what they're doing",
        "/to: pick the tab your messages go to",
        "/stop: stop Claude in a tab",
        "/usage: the plan's usage",
        "/quiet: no messages for an hour (/quiet 30, /quiet off)",
        "/new: open a tab in a folder, when Deventry allows it",
        "/help: this");

    /// <summary>The tab's name, then its project and branch, unless the messages say just what happened.</summary>
    private string Header(RemoteTab tab)
    {
        if (Desk.Detail == RemoteDetail.JustWhatHappened)
        {
            return Bold(tab.Name);
        }
        var project = tab.Branch is { Length: > 0 } branch ? $"{tab.Project} ({branch})" : tab.Project;
        return $"{Bold(tab.Name)} · {Escape(project)}";
    }

    internal static string Glyph(RemoteTabStatus status) => status switch
    {
        RemoteTabStatus.Working or RemoteTabStatus.Starting => "●",
        RemoteTabStatus.NeedsInput => "❗",
        RemoteTabStatus.Unread => "✓",
        RemoteTabStatus.Error => "✕",
        _ => "○",
    };

    /// <summary>The status line under a tab's name: what it's doing only when the messages say everything.</summary>
    private string StatusLine(RemoteTab tab)
    {
        var shown = Desk.Detail == RemoteDetail.JustWhatHappened ? tab with { Doing = null } : tab;
        return RemoteMessages.StatusLine(shown, _time.GetUtcNow());
    }

    private string TabsHtml(IReadOnlyList<RemoteTab> tabs)
    {
        var html = new StringBuilder(Bold($"Tabs on {Desk.MachineName}"));
        foreach (var project in tabs.GroupBy(t => t.Project))
        {
            if (Desk.Detail == RemoteDetail.Everything)
            {
                html.Append("\n\n").Append(Bold(project.Key));
            }
            else
            {
                html.Append('\n');
            }
            foreach (var tab in project)
            {
                html.Append('\n').Append(Glyph(tab.Status)).Append(' ').Append(Escape(tab.Name)).Append(": ").Append(Escape(StatusLine(tab)));
            }
        }
        return html.ToString();
    }

    private string LiveHtml(string heading, RemoteTab tab) => $"{heading}\n{Escape(StatusLine(tab))}";

    private IReadOnlyList<IReadOnlyList<TelegramButton>>? LiveButtons(RemoteTab tab) =>
        tab.Status is RemoteTabStatus.Working or RemoteTabStatus.NeedsInput && RemoteAccess.Allows(Desk.Access, Desk.CanOpenTabs, RemoteAction.Stop)
            ? Rows([MakeButton("Stop", new StopButton(tab.Id))], 1)
            : null;

    /// <summary>A tab's card: where plain messages go, Stop, the reply and its files, and Start it again.</summary>
    private IReadOnlyList<IReadOnlyList<TelegramButton>> CardButtons(RemoteTab tab)
    {
        var rows = new List<IReadOnlyList<TelegramButton>>();
        var full = RemoteAccess.Allows(Desk.Access, Desk.CanOpenTabs, RemoteAction.Send);
        if (full && !tab.IsReadOnly)
        {
            var first = new List<TelegramButton> { MakeButton("Send my next messages here", new TargetButton(tab.Id)) };
            if (tab.Status is RemoteTabStatus.Working or RemoteTabStatus.NeedsInput)
            {
                first.Add(MakeButton("Stop", new StopButton(tab.Id)));
            }
            rows.Add(first);
        }
        rows.Add([MakeButton("Whole reply", new ReplyButton(tab.Id)), MakeButton("Changed files", new FilesButton(tab.Id))]);
        if (full && tab.CanRestart)
        {
            rows.Add([MakeButton("Start it again", new RestartButton(tab.Id))]);
        }
        return rows;
    }

    private string PromptHtml(RemoteTab tab, RemotePrompt prompt)
    {
        var html = new StringBuilder(Header(tab)).Append('\n');
        if (prompt.Asker is { Length: > 0 } asker)
        {
            html.Append(Italic($"From {asker}")).Append('\n');
        }
        html.Append(Bold(prompt.Parts > 1 ? $"{prompt.Title} ({prompt.Part} of {prompt.Parts})" : prompt.Title));
        if (prompt.About is { Length: > 0 } about)
        {
            var shown = RemoteMessages.Shorten(about, AboutLength);
            html.Append('\n').Append(prompt.Kind == RemotePromptKind.Command ? CodeBlock(shown, "bash")
                : prompt.Kind == RemotePromptKind.ThreadMessage ? $"<blockquote>{Escape(shown)}</blockquote>"
                : Escape(shown));
        }
        if (prompt.Kind == RemotePromptKind.Plan && prompt.Plan is { Length: > 0 })
        {
            html.Append('\n').Append(Escape("The plan is in the file above."));
        }
        if (prompt.AtDeskOnly)
        {
            html.Append('\n').Append(Italic(RemoteMessages.AtDesk));
        }
        else if (!RemoteAccess.Allows(Desk.Access, Desk.CanOpenTabs, RemoteAction.Answer))
        {
            html.Append('\n').Append(Italic("Remote clients can only watch here: answer this at the desk."));
        }
        return html.ToString();
    }

    private IReadOnlyList<IReadOnlyList<TelegramButton>>? PromptButtons(RemoteTab tab, RemotePrompt prompt)
    {
        if (prompt.AtDeskOnly || !RemoteAccess.Allows(Desk.Access, Desk.CanOpenTabs, RemoteAction.Answer))
        {
            return null;
        }
        var buttons = prompt.Options.Select(o => MakeButton(o.Label, new AnswerButton(tab.Id, prompt.Id, o))).ToList();
        // A command's Allow and Deny share a row; a question's options and a plan's approvals each have one.
        return prompt.Kind is RemotePromptKind.Command or RemotePromptKind.FileChange or RemotePromptKind.Tool or RemotePromptKind.ThreadMessage
            ? Rows(buttons, 2)
            : Rows(buttons, 1);
    }

    private string TurnHtml(RemoteTurnFinished turn)
    {
        var html = new StringBuilder(Header(turn.Tab)).Append('\n');
        var line = new List<string> { turn.Took is { } took ? $"Finished in {Formats.Duration(took)}" : "Finished" };
        if (turn.Tasks is { Length: > 0 } tasks)
        {
            line.Add(tasks);
        }
        html.Append(Escape(string.Join(" · ", line)));
        if (turn.Reply?.Trim() is { Length: > 0 } reply)
        {
            var (start, cut) = FirstLines(reply);
            html.Append("\n\n").Append(FromMarkdown(start));
            if (cut)
            {
                html.Append('\n').Append(Italic("…"));
            }
        }
        if (turn.Files.Count > 0)
        {
            html.Append("\n\n").Append(Escape(FilesLine(turn.Files)));
        }
        var text = html.ToString();
        return FitsMessage(text) ? text : $"{Header(turn.Tab)}\n{Escape(string.Join(" · ", line))}\n\n{Escape(FilesLine(turn.Files))}";
    }

    /// <summary>"Changed Player.cs +12 −3, Hook.cs +4 and 3 more".</summary>
    private static string FilesLine(IReadOnlyList<RemoteFileChange> files)
    {
        if (files.Count == 0)
        {
            return "";
        }
        var named = files.Take(3).Select(f => $"{Path.GetFileName(f.Path)} {Lines(f)}");
        var more = files.Count > 3 ? $" and {files.Count - 3} more" : "";
        return $"Changed {string.Join(", ", named)}{more}";
    }

    private static string Lines(RemoteFileChange file) => file switch
    {
        { IsNew: true } => string.Create(CultureInfo.InvariantCulture, $"(new, +{file.Added})"),
        { IsDeleted: true } => "(deleted)",
        _ when file.Removed == 0 => string.Create(CultureInfo.InvariantCulture, $"+{file.Added}"),
        _ => string.Create(CultureInfo.InvariantCulture, $"+{file.Added} −{file.Removed}"),
    };

    private static string ChangedFilesHtml(string tabName, IReadOnlyList<RemoteFileChange> files)
    {
        if (files.Count == 0)
        {
            return $"{Bold(tabName)}\nNo files changed.";
        }
        var html = new StringBuilder(Bold(tabName)).Append('\n').Append(Escape(files.Count == 1 ? "1 file changed" : $"{files.Count} files changed"));
        foreach (var file in files.Take(FilesListed))
        {
            html.Append('\n').Append(Code(file.Path)).Append(' ').Append(Escape(Lines(file)));
        }
        if (files.Count > FilesListed)
        {
            html.Append('\n').Append(Escape($"and {files.Count - FilesListed} more"));
        }
        return html.ToString();
    }
}
