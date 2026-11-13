using System.Globalization;
using System.Text;

namespace Deventry.Core.Remote.Chat;

/// <summary>The bot's messages, as the service writes them, and their buttons (DESIGN.md §18, "What the bot sends").</summary>
public abstract partial class ChatBotClient
{
    /// <summary>How much of a command, a question or a file a prompt's message shows, leaving the message room for the rest.</summary>
    private int AboutLength => Math.Min(3000, Format.MessageLimit - 600);

    /// <summary>The most changed files a message lists.</summary>
    private const int FilesListed = 30;

    private string HelpText() => string.Join("\n", new[]
    {
        "Reply to any of my messages to send it to that message's tab, or pick a tab with /to and just write.",
        "",
        "/tabs: the tabs and what they're doing",
        "/to: pick the tab your messages go to",
        "/stop: stop Claude in a tab",
        "/usage: the plan's usage",
        "/quiet: no messages for an hour (/quiet 30, /quiet off)",
        "/new: open a tab in a folder, when Deventry allows it",
        "/help: this",
    }.Select(Format.Escape));

    /// <summary>The tab's name, then its project and branch, unless the messages say just what happened.</summary>
    private string Header(RemoteTab tab)
    {
        if (Desk.Detail == RemoteDetail.JustWhatHappened)
        {
            return Format.Bold(tab.Name);
        }
        var project = tab.Branch is { Length: > 0 } branch ? $"{tab.Project} ({branch})" : tab.Project;
        return $"{Format.Bold(tab.Name)} · {Format.Escape(project)}";
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

    private string TabsText(IReadOnlyList<RemoteTab> tabs)
    {
        var text = new StringBuilder(Format.Bold($"Tabs on {Desk.MachineName}"));
        foreach (var project in tabs.GroupBy(t => t.Project))
        {
            if (Desk.Detail == RemoteDetail.Everything)
            {
                text.Append("\n\n").Append(Format.Bold(project.Key));
            }
            else
            {
                text.Append('\n');
            }
            foreach (var tab in project)
            {
                text.Append('\n').Append(Glyph(tab.Status)).Append(' ').Append(Format.Escape(tab.Name)).Append(": ").Append(Format.Escape(StatusLine(tab)));
            }
        }
        return text.ToString();
    }

    private string LiveText(string heading, RemoteTab tab) => $"{heading}\n{Format.Escape(StatusLine(tab))}";

    private IReadOnlyList<IReadOnlyList<ChatButton>>? LiveButtons(RemoteTab tab) =>
        tab.Status is RemoteTabStatus.Working or RemoteTabStatus.NeedsInput && RemoteAccess.Allows(Desk.Access, Desk.CanOpenTabs, RemoteAction.Stop)
            ? Rows([MakeButton("Stop", new StopButton(tab.Id))], 1)
            : null;

    /// <summary>A tab's card: where plain messages go, Stop, the reply and its files, and Start it again.</summary>
    private IReadOnlyList<IReadOnlyList<ChatButton>> CardButtons(RemoteTab tab)
    {
        var rows = new List<IReadOnlyList<ChatButton>>();
        var full = RemoteAccess.Allows(Desk.Access, Desk.CanOpenTabs, RemoteAction.Send);
        if (full && !tab.IsReadOnly)
        {
            var first = new List<ChatButton> { MakeButton("Send my next messages here", new TargetButton(tab.Id)) };
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

    private string PromptText(RemoteTab tab, RemotePrompt prompt)
    {
        var text = new StringBuilder(Header(tab)).Append('\n');
        if (prompt.Asker is { Length: > 0 } asker)
        {
            text.Append(Format.Italic($"From {asker}")).Append('\n');
        }
        text.Append(Format.Bold(prompt.Parts > 1 ? $"{prompt.Title} ({prompt.Part} of {prompt.Parts})" : prompt.Title));
        if (prompt.About is { Length: > 0 } about)
        {
            var shown = RemoteMessages.Shorten(about, AboutLength);
            text.Append('\n').Append(prompt.Kind == RemotePromptKind.Command ? Format.CodeBlock(shown, "bash")
                : prompt.Kind == RemotePromptKind.ThreadMessage ? Format.Quote(shown)
                : Format.Escape(shown));
        }
        if (prompt.Kind == RemotePromptKind.Plan && prompt.Plan is { Length: > 0 })
        {
            text.Append('\n').Append(Format.Escape("The plan is in the file above."));
        }
        if (prompt.AtDeskOnly)
        {
            text.Append('\n').Append(Format.Italic(RemoteMessages.AtDesk));
        }
        else if (!RemoteAccess.Allows(Desk.Access, Desk.CanOpenTabs, RemoteAction.Answer))
        {
            text.Append('\n').Append(Format.Italic("Remote clients can only watch here: answer this at the desk."));
        }
        return text.ToString();
    }

    private IReadOnlyList<IReadOnlyList<ChatButton>>? PromptButtons(RemoteTab tab, RemotePrompt prompt)
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

    private string TurnText(RemoteTurnFinished turn)
    {
        var text = new StringBuilder(Header(turn.Tab)).Append('\n');
        var line = new List<string> { turn.Took is { } took ? $"Finished in {Formats.Duration(took)}" : "Finished" };
        if (turn.Tasks is { Length: > 0 } tasks)
        {
            line.Add(tasks);
        }
        text.Append(Format.Escape(string.Join(" · ", line)));
        if (turn.Reply?.Trim() is { Length: > 0 } reply)
        {
            var (start, cut) = ChatFormat.FirstLines(reply);
            text.Append("\n\n").Append(Format.FromMarkdown(start));
            if (cut)
            {
                text.Append('\n').Append(Format.Italic("…"));
            }
        }
        if (turn.Files.Count > 0)
        {
            text.Append("\n\n").Append(Format.Escape(FilesLine(turn.Files)));
        }
        var full = text.ToString();
        return Format.FitsMessage(full) ? full : $"{Header(turn.Tab)}\n{Format.Escape(string.Join(" · ", line))}\n\n{Format.Escape(FilesLine(turn.Files))}";
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

    /// <summary>The files a tab changed, as many as fit a message, up to <see cref="FilesListed"/>.</summary>
    private string ChangedFilesText(string tabName, IReadOnlyList<RemoteFileChange> files)
    {
        if (files.Count == 0)
        {
            return $"{Format.Bold(tabName)}\n{Format.Escape("No files changed.")}";
        }
        var text = new StringBuilder(Format.Bold(tabName)).Append('\n').Append(Format.Escape(files.Count == 1 ? "1 file changed" : $"{files.Count} files changed"));
        var listed = 0;
        foreach (var file in files.Take(FilesListed))
        {
            var line = $"\n{Format.Code(file.Path)} {Format.Escape(Lines(file))}";
            // Room is kept for the line that says how many more.
            if (text.Length + line.Length > Format.MessageLimit - 40)
            {
                break;
            }
            text.Append(line);
            listed++;
        }
        if (files.Count > listed)
        {
            text.Append('\n').Append(Format.Escape($"and {files.Count - listed} more"));
        }
        return text.ToString();
    }
}
