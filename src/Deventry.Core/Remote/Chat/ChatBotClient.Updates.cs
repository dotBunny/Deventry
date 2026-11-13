using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Deventry.Core.Remote.Chat;

/// <summary>The bot's side of the chat (DESIGN.md §18, "Talking to a tab", "Commands"): messages, commands and buttons.</summary>
public abstract partial class ChatBotClient
{
    /// <summary>How many of its messages the bot remembers the tab of, for replies to them.</summary>
    public const int RememberedMessages = 1000;

    /// <summary>How many buttons a run keeps the actions of; older ones are stale.</summary>
    public const int RememberedButtons = 5000;

    /// <summary>The commands in the service's <b>/</b> menu, besides pairing's.</summary>
    public static IReadOnlyList<ChatCommand> Commands { get; } =
    [
        new("tabs", "The tabs and what they're doing"),
        new("to", "Pick the tab your messages go to", "tab", "The tab's name, or the start of it"),
        new("stop", "Stop Claude in a tab", "tab", "The tab's name, or the start of it"),
        new("usage", "The plan's usage"),
        new("quiet", "No messages for an hour (/quiet 30, /quiet off)", "minutes", "How many minutes, or off"),
        new("new", "Open a tab in a folder"),
        new("help", "What I can do"),
    ];

    protected IRemoteDesk Desk => _desk ?? throw new InvalidOperationException($"The {Name} client hasn't started.");

    /// <summary>Which tab each of the bot's messages was about, by chat and message, for the service's replies.</summary>
    private readonly Dictionary<(long Chat, long Message), string> _messageTabs = [];
    private readonly Queue<(long Chat, long Message)> _messageOrder = new();

    /// <summary>The tab plain messages go to, by chat: <c>/to</c>, or <b>Send my next messages here</b>.</summary>
    private readonly Dictionary<long, string> _targets = [];

    /// <summary>What the chat's next message answers, after a button that takes words.</summary>
    private readonly Dictionary<long, Func<string, CancellationToken, Task>> _awaiting = [];

    private readonly Dictionary<string, Button> _buttons = new(StringComparer.Ordinal);
    private readonly Queue<string> _buttonOrder = new();
    private long _lastKey;
    private int _passedOver;

    /// <summary>The desk holds messages: <c>/quiet</c>, or the header's <b>Quiet for an hour</b>.</summary>
    private bool IsQuiet => Desk.QuietUntil is { } until && until > _time.GetUtcNow();

    // ---- What a button does -----------------------------------------------------------------------------------------

    private abstract record Button;

    private sealed record AnswerButton(string TabId, string PromptId, RemoteOption Option) : Button;

    private sealed record TabButton(string TabId) : Button;

    private sealed record TargetButton(string TabId) : Button;

    private sealed record StopButton(string TabId) : Button;

    private sealed record RestartButton(string TabId) : Button;

    private sealed record ReplyButton(string TabId) : Button;

    private sealed record FilesButton(string TabId) : Button;

    private sealed record ShowButton(string TabId, string PromptId) : Button;

    private sealed record SendPendingButton(string TabId, string Text) : Button;

    private sealed record FolderButton(string Path) : Button;

    private ChatButton MakeButton(string label, Button action)
    {
        var key = ChatFormat.Key(++_lastKey);
        _buttons[key] = action;
        _buttonOrder.Enqueue(key);
        while (_buttonOrder.Count > RememberedButtons)
        {
            _buttons.Remove(_buttonOrder.Dequeue());
        }
        return new ChatButton(label, ChatFormat.ButtonData(_run, key));
    }

    private void Remember(long chat, long message, string tabId)
    {
        if (_messageTabs.TryAdd((chat, message), tabId))
        {
            _messageOrder.Enqueue((chat, message));
            while (_messageOrder.Count > RememberedMessages)
            {
                _messageTabs.Remove(_messageOrder.Dequeue());
            }
        }
    }

    // ---- Messages ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// A message to the bot, on the work loop: a command, words a button asked for, or text for a tab. A stale one,
    /// sent before Deventry started listening, is passed over and counted.
    /// </summary>
    protected async Task HandleMessageAsync(ChatMessage message, bool stale, CancellationToken cancellationToken)
    {
        // Only private chats count: a group's messages are no one's to answer.
        if (!message.IsPrivate || message.From is not { IsBot: false } from)
        {
            return;
        }
        var (command, argument) = ParseCommand(message.Text);
        if (!_pairing.IsPaired(from.Id))
        {
            if (command == PairCommand && !stale && argument is { Length: > 0 }
                && _pairing.TryPair(argument, from.Id, from.Name, from.Username) == PairingOutcome.Paired)
            {
                _logger.LogInformation("{Client}: paired an account.", Name);
                await SendAsync(message.Chat, Welcome(), cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            // Everyone else gets no answer at all, so the bot doesn't say it belongs to anyone.
            return;
        }
        if (stale)
        {
            _passedOver++;
            return;
        }
        Activity?.Invoke(RemoteActivity.Received);
        var chat = message.Chat;
        if (command is not null)
        {
            _awaiting.Remove(chat);
            await HandleCommandAsync(chat, command, argument, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (message.HasMedia)
        {
            await SendAsync(chat, "Only text for now.", replyTo: ReplyTo(message), silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }
        if (message.Text?.Trim() is not { Length: > 0 } text)
        {
            return;
        }
        if (_awaiting.Remove(chat, out var waiting))
        {
            await waiting(text, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (message.ReplyTo is { } replied && _messageTabs.TryGetValue((chat, replied), out var tabId))
        {
            await SendToTabAsync(chat, tabId, text, ReplyTo(message), cancellationToken).ConfigureAwait(false);
        }
        else if (_targets.TryGetValue(chat, out var target))
        {
            await SendToTabAsync(chat, target, text, ReplyTo(message), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await AskWhichTabAsync(chat, text, ReplyTo(message), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>A message to reply to: none for a command the service delivered without one.</summary>
    private static long? ReplyTo(ChatMessage message) => message.MessageId != 0 ? message.MessageId : null;

    /// <summary>What a newly paired account is told: where it's paired, and what the bot can do.</summary>
    protected string Welcome() => $"{Format.Escape(RemoteMessages.Paired(Desk.MachineName))}\n\n{HelpText()}";

    /// <summary>What an account paired already is told when it sends a code again.</summary>
    protected string AlreadyPaired() => $"{Format.Escape($"Already paired with Deventry on {Desk.MachineName}.")}\n\n{HelpText()}";

    /// <summary>"/to@deventry_bot Grappling" is ("to", "Grappling"); text that isn't a command is (null, null).</summary>
    internal static (string? Command, string? Argument) ParseCommand(string? text)
    {
        if (text?.Trim() is not ['/', ..] command)
        {
            return (null, null);
        }
        var space = command.IndexOfAny([' ', '\n']);
        var name = (space < 0 ? command[1..] : command[1..space]).Split('@')[0].ToLowerInvariant();
        var argument = space < 0 ? null : command[(space + 1)..].Trim();
        return name.Length == 0 ? (null, null) : (name, argument is { Length: > 0 } ? argument : null);
    }

    /// <summary>Once the messages kept while Deventry was closed are in, says how many it passed over.</summary>
    protected async Task ReportPassedOverAsync(CancellationToken cancellationToken)
    {
        if (_passedOver > 0)
        {
            var count = _passedOver;
            _passedOver = 0;
            await BroadcastAsync(Format.Escape(RemoteMessages.PassedOver(count)), silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Says, silently, that Deventry is back: it said it was closing last time.</summary>
    protected Task AnnounceBackAsync(CancellationToken cancellationToken) =>
        BroadcastAsync(Format.Escape(RemoteMessages.Back(Desk.MachineName)), silent: true, cancellationToken: cancellationToken);

    // ---- Commands ---------------------------------------------------------------------------------------------------

    private Task HandleCommandAsync(long chat, string command, string? argument, CancellationToken cancellationToken) => command switch
    {
        _ when command == PairCommand => SendAsync(chat, AlreadyPaired(), silent: true, cancellationToken: cancellationToken),
        "help" => SendAsync(chat, HelpText(), silent: true, cancellationToken: cancellationToken),
        "tabs" => ListTabsAsync(chat, cancellationToken),
        "to" => PickTargetAsync(chat, argument, cancellationToken),
        "stop" => StopCommandAsync(chat, argument, cancellationToken),
        "usage" => UsageAsync(chat, cancellationToken),
        "quiet" => QuietAsync(chat, argument, cancellationToken),
        "new" => NewTabAsync(chat, cancellationToken),
        _ => SendAsync(chat, $"{Format.Escape($"I don't know /{command}.")}\n\n{HelpText()}", silent: true, cancellationToken: cancellationToken),
    };

    private async Task ListTabsAsync(long chat, CancellationToken cancellationToken)
    {
        var tabs = await Desk.GetTabsAsync(cancellationToken).ConfigureAwait(false);
        if (tabs.Count == 0)
        {
            await SendAsync(chat, "No tabs are open.", silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }
        var buttons = Rows(tabs.Select(t => MakeButton($"{Glyph(t.Status)} {t.Name}", new TabButton(t.Id))), 2);
        await SendAsync(chat, TabsText(tabs), buttons, silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task PickTargetAsync(long chat, string? argument, CancellationToken cancellationToken)
    {
        var tabs = (await Desk.GetTabsAsync(cancellationToken).ConfigureAwait(false)).Where(t => !t.IsReadOnly).ToList();
        if (argument is not null && FindByName(tabs, argument) is { } named)
        {
            await SetTargetAsync(chat, named, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (tabs.Count == 0)
        {
            await SendAsync(chat, "No tabs are open.", silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }
        var buttons = Rows(tabs.Select(t => MakeButton(t.Name, new TargetButton(t.Id))), 2);
        await SendAsync(chat, "Which tab should your messages go to?", buttons, silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task SetTargetAsync(long chat, RemoteTab tab, CancellationToken cancellationToken)
    {
        _targets[chat] = tab.Id;
        await SendAsync(chat, $"Your messages go to {Format.Bold(tab.Name)} now. /to picks another.", silent: true, tabId: tab.Id, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task StopCommandAsync(long chat, string? argument, CancellationToken cancellationToken)
    {
        if (!RemoteAccess.Allows(Desk.Access, Desk.CanOpenTabs, RemoteAction.Stop))
        {
            await SendAsync(chat, Format.Escape(RemoteAccess.Refusal(RemoteAction.Stop)), silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }
        var working = (await Desk.GetTabsAsync(cancellationToken).ConfigureAwait(false))
            .Where(t => t.Status is RemoteTabStatus.Working or RemoteTabStatus.NeedsInput).ToList();
        var tab = argument is not null ? FindByName(working, argument) : working.Count == 1 ? working[0] : null;
        if (tab is not null)
        {
            await StopTabAsync(chat, tab.Id, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (working.Count == 0)
        {
            await SendAsync(chat, "No tab is working.", silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }
        var buttons = Rows(working.Select(t => MakeButton(t.Name, new StopButton(t.Id))), 2);
        await SendAsync(chat, "Stop Claude in which tab?", buttons, silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task StopTabAsync(long chat, string tabId, CancellationToken cancellationToken)
    {
        var result = await Desk.StopAsync(tabId, Name, cancellationToken).ConfigureAwait(false);
        var tab = await FindTabAsync(tabId, cancellationToken).ConfigureAwait(false);
        await SendAsync(chat, result.Done ? $"Stopped {Format.Bold(tab?.Name ?? "the tab")}." : Format.Escape(result.Reason ?? "Couldn't stop it."), silent: true,
            tabId: tabId, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task UsageAsync(long chat, CancellationToken cancellationToken)
    {
        var usage = await Desk.GetUsageAsync(cancellationToken).ConfigureAwait(false);
        var lines = RemoteMessages.Usage(usage, _time.GetUtcNow(), _time.LocalTimeZone);
        await SendAsync(chat, string.Join("\n", lines.Select(Format.Escape)), silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The longest <c>/quiet</c> holds: a day.</summary>
    private const int MaxQuietMinutes = 24 * 60;

    private Task QuietAsync(long chat, string? argument, CancellationToken cancellationToken)
    {
        if (argument is { } off && off.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            Desk.SetQuiet(null);
            return SendAsync(chat, "Messages are back on.", silent: true, cancellationToken: cancellationToken);
        }
        var minutes = argument is null ? 60
            : int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out var asked) ? Math.Clamp(asked, 1, MaxQuietMinutes)
            : -1;
        if (minutes < 0)
        {
            return SendAsync(chat, Format.Escape("Say how many minutes, as /quiet 30, or /quiet off."), silent: true, cancellationToken: cancellationToken);
        }
        var quietUntil = _time.GetUtcNow() + TimeSpan.FromMinutes(minutes);
        Desk.SetQuiet(quietUntil);
        var until = TimeZoneInfo.ConvertTime(quietUntil, _time.LocalTimeZone).ToString("t", CultureInfo.CurrentCulture);
        return SendAsync(chat, Format.Escape($"Quiet until {until}: no messages but answers to what you do here. /quiet off ends it sooner."), silent: true,
            cancellationToken: cancellationToken);
    }

    private async Task NewTabAsync(long chat, CancellationToken cancellationToken)
    {
        if (!Desk.CanOpenTabs)
        {
            await SendAsync(chat, Format.Escape(RemoteAccess.Refusal(RemoteAction.OpenTab)), silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }
        var folders = await Desk.GetFoldersAsync(cancellationToken).ConfigureAwait(false);
        if (folders.Count == 0)
        {
            await SendAsync(chat, "There are no favorite or recent folders to open a tab in.", silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }
        var buttons = Rows(folders.Take(20).Select(f => MakeButton(f.IsFavorite ? $"★ {f.Name}" : f.Name, new FolderButton(f.Path))), 2);
        await SendAsync(chat, "Open a tab in which folder?", buttons, silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    // ---- Talking to a tab -------------------------------------------------------------------------------------------

    private async Task SendToTabAsync(long chat, string tabId, string text, long? replyTo, CancellationToken cancellationToken)
    {
        var result = await Desk.SendAsync(tabId, text, Name, cancellationToken).ConfigureAwait(false);
        if (!result.Done)
        {
            await SendAsync(chat, Format.Escape(result.Reason ?? "Couldn't send it."), replyTo: replyTo, silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }
        _engaged.Add(tabId);
        if (await FindTabAsync(tabId, cancellationToken).ConfigureAwait(false) is not { } tab)
        {
            return;
        }
        // Its message shows what the tab is doing, edited in place: the turn's end is a new one.
        var heading = $"Sent to {Format.Bold(tab.Name)}.";
        var id = await SendAsync(chat, LiveText(heading, tab), LiveButtons(tab), replyTo, silent: true, tabId: tab.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
        StartLive(chat, id, heading, tab);
    }

    private async Task AskWhichTabAsync(long chat, string text, long? replyTo, CancellationToken cancellationToken)
    {
        var tabs = (await Desk.GetTabsAsync(cancellationToken).ConfigureAwait(false)).Where(t => !t.IsReadOnly).ToList();
        if (tabs.Count == 0)
        {
            await SendAsync(chat, "No tabs are open to send that to.", replyTo: replyTo, silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }
        var buttons = Rows(tabs.Select(t => MakeButton(t.Name, new SendPendingButton(t.Id, text))), 2);
        await SendAsync(chat, "Which tab is this for?", buttons, replyTo, silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<RemoteTab?> FindTabAsync(string tabId, CancellationToken cancellationToken) =>
        (await Desk.GetTabsAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(t => t.Id == tabId);

    /// <summary>The tab a name names: the one called that, or the only one whose name starts with it or holds it.</summary>
    internal static RemoteTab? FindByName(IReadOnlyList<RemoteTab> tabs, string name)
    {
        var wanted = name.Trim();
        return tabs.FirstOrDefault(t => t.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            ?? Only(tabs.Where(t => t.Name.StartsWith(wanted, StringComparison.OrdinalIgnoreCase)))
            ?? Only(tabs.Where(t => t.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase)));

        static RemoteTab? Only(IEnumerable<RemoteTab> found) => found.Take(2).ToList() is [var one] ? one : null;
    }

    // ---- Buttons ----------------------------------------------------------------------------------------------------

    /// <summary>A tap on one of the bot's buttons, on the work loop. A stranger's is answered, saying nothing, and does nothing.</summary>
    protected async Task HandleTapAsync(ChatTap tap, CancellationToken cancellationToken)
    {
        if (!_pairing.IsPaired(tap.From.Id))
        {
            // A tap still needs its answer, or its spinner turns; it says nothing.
            await PacedAsync(token => AnswerTapAsync(tap, null, token), cancellationToken).ConfigureAwait(false);
            return;
        }
        Activity?.Invoke(RemoteActivity.Received);
        var chat = tap.Chat ?? tap.From.Id;
        var parsed = ChatFormat.ParseButtonData(tap.Data);
        if (parsed is not var (run, key) || run != _run || !_buttons.TryGetValue(key, out var button))
        {
            var why = parsed is var (otherRun, _) && otherRun != _run ? "That button is from before Deventry restarted." : "That button has expired.";
            await PacedAsync(token => AnswerTapAsync(tap, why, token), cancellationToken).ConfigureAwait(false);
            if (tap is { Chat: { } staleChat, MessageId: { } staleMessage })
            {
                await PacedAsync(token => DeliverButtonsAsync(staleChat, staleMessage, null, token), cancellationToken).ConfigureAwait(false);
            }
            return;
        }
        switch (button)
        {
            case AnswerButton { Option.TakesText: true } answer:
                await Toast(tap, null, cancellationToken).ConfigureAwait(false);
                await AskForWordsAsync(chat, answer, cancellationToken).ConfigureAwait(false);
                break;
            case AnswerButton answer:
                await AnswerAsync(chat, tap, answer.TabId, answer.PromptId, answer.Option, null, cancellationToken).ConfigureAwait(false);
                break;
            case TabButton tab:
                await Toast(tap, null, cancellationToken).ConfigureAwait(false);
                await SendTabCardAsync(chat, tab.TabId, cancellationToken).ConfigureAwait(false);
                break;
            case TargetButton target:
                await Toast(tap, null, cancellationToken).ConfigureAwait(false);
                if (await FindTabAsync(target.TabId, cancellationToken).ConfigureAwait(false) is { } picked)
                {
                    await SetTargetAsync(chat, picked, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await SendAsync(chat, Format.Escape(RemoteMessages.TabGone), silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                break;
            case StopButton stop:
                await Toast(tap, null, cancellationToken).ConfigureAwait(false);
                await StopTabAsync(chat, stop.TabId, cancellationToken).ConfigureAwait(false);
                break;
            case RestartButton restart:
                var restarted = await Desk.RestartAsync(restart.TabId, Name, cancellationToken).ConfigureAwait(false);
                await Toast(tap, restarted.Done ? "Starting it again." : restarted.Reason, cancellationToken).ConfigureAwait(false);
                break;
            case ReplyButton reply:
                await Toast(tap, null, cancellationToken).ConfigureAwait(false);
                await SendWholeReplyAsync(chat, reply.TabId, cancellationToken).ConfigureAwait(false);
                break;
            case FilesButton files:
                await Toast(tap, null, cancellationToken).ConfigureAwait(false);
                await SendChangedFilesAsync(chat, files.TabId, cancellationToken).ConfigureAwait(false);
                break;
            case ShowButton show:
                await Toast(tap, null, cancellationToken).ConfigureAwait(false);
                await ShowPromptAsync(chat, show.TabId, show.PromptId, cancellationToken).ConfigureAwait(false);
                break;
            case SendPendingButton pending:
                await Toast(tap, null, cancellationToken).ConfigureAwait(false);
                _buttons.Remove(key);
                if (tap is { Chat: { } questionChat, MessageId: { } question })
                {
                    await PacedAsync(token => DeliverButtonsAsync(questionChat, question, null, token), cancellationToken).ConfigureAwait(false);
                }
                await SendToTabAsync(chat, pending.TabId, pending.Text, null, cancellationToken).ConfigureAwait(false);
                break;
            case FolderButton folder:
                var opened = await Desk.OpenTabAsync(folder.Path, Name, cancellationToken).ConfigureAwait(false);
                await Toast(tap, opened.Done ? null : opened.Reason, cancellationToken).ConfigureAwait(false);
                if (opened.Done && opened.TabId is { } openedId && await FindTabAsync(openedId, cancellationToken).ConfigureAwait(false) is { } openedTab)
                {
                    _targets[chat] = openedTab.Id;
                    await SendAsync(chat, $"Opened {Format.Bold(openedTab.Name)}. Your messages go to it now.", silent: true, tabId: openedTab.Id,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                break;
        }
    }

    private Task Toast(ChatTap tap, string? text, CancellationToken cancellationToken) =>
        PacedAsync(token => AnswerTapAsync(tap, text, token), cancellationToken);

    /// <summary>A button that takes words asks for them: the chat's next message is the answer.</summary>
    private async Task AskForWordsAsync(long chat, AnswerButton answer, CancellationToken cancellationToken)
    {
        _awaiting[chat] = (words, token) => AnswerAsync(chat, null, answer.TabId, answer.PromptId, answer.Option, words, token);
        var ask = answer.Option.Id switch
        {
            RemotePrompt.OptionIds.DenyWithMessage => "What should Claude do instead? Send it as your next message.",
            RemotePrompt.OptionIds.KeepPlanning => "What should change in the plan? Send it as your next message.",
            _ => "Send your answer as your next message.",
        };
        var buttons = answer.Option.NeedsText
            ? null
            : Rows([MakeButton("Without a message", answer with { Option = answer.Option with { TakesText = false } })], 1);
        await SendAsync(chat, Format.Escape(ask), buttons, silent: true, tabId: answer.TabId, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task AnswerAsync(long chat, ChatTap? tap, string tabId, string promptId, RemoteOption option, string? words, CancellationToken cancellationToken)
    {
        var result = await Desk.AnswerAsync(tabId, promptId, option.Id, words, Name, cancellationToken).ConfigureAwait(false);
        if (tap is not null)
        {
            await Toast(tap, result.Done ? option.Label.TrimEnd('…') : result.Reason, cancellationToken).ConfigureAwait(false);
        }
        else if (!result.Done)
        {
            await SendAsync(chat, Format.Escape(result.Reason ?? "Couldn't answer it."), silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        // Answered here, the prompt's message is edited once the desk says it closed. One that closed already, while
        // this run wasn't watching it, just loses its buttons.
        if (!result.Done && tap is { Chat: { } tapChat, MessageId: { } message } && !_promptMessages.ContainsKey(PromptKey(tabId, promptId)))
        {
            await PacedAsync(token => DeliverButtonsAsync(tapChat, message, null, token), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendWholeReplyAsync(long chat, string tabId, CancellationToken cancellationToken)
    {
        if (await Desk.GetLastReplyAsync(tabId, cancellationToken).ConfigureAwait(false) is not { } reply)
        {
            await SendAsync(chat, "There's no reply to show.", silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }
        var text = $"{Format.Bold(reply.TabName)}\n{Format.FromMarkdown(reply.Markdown)}";
        if (Format.FitsMessage(text))
        {
            await SendAsync(chat, text, silent: true, tabId: tabId, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }
        // Longer than a message: a Markdown file.
        await SendFileAsync(chat, $"{FileName(reply.TabName)} reply.md", reply.Markdown, $"{Format.Bold(reply.TabName)}: the whole reply", tabId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendChangedFilesAsync(long chat, string tabId, CancellationToken cancellationToken)
    {
        var files = await Desk.GetChangedFilesAsync(tabId, cancellationToken).ConfigureAwait(false);
        if (files is null)
        {
            await SendAsync(chat, Format.Escape(RemoteMessages.TabGone), silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }
        var tab = await FindTabAsync(tabId, cancellationToken).ConfigureAwait(false);
        await SendAsync(chat, ChangedFilesText(tab?.Name ?? "The tab", files), silent: true, tabId: tabId, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task ShowPromptAsync(long chat, string tabId, string promptId, CancellationToken cancellationToken)
    {
        if (await FindTabAsync(tabId, cancellationToken).ConfigureAwait(false) is { Prompt: { } prompt } tab && prompt.Id == promptId)
        {
            await SendPromptAsync([chat], tab, prompt, RemoteDetail.Everything, silent: true, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await SendAsync(chat, Format.Escape(RemoteMessages.AlreadyAnswered(RemotePromptEnd.Withdrawn, null)), silent: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }

    // ---- Sending ----------------------------------------------------------------------------------------------------

    /// <summary>Sends a message, as the service writes it. Returns its id.</summary>
    /// <param name="silent">The phone doesn't make a sound for it.</param>
    protected abstract Task<long> DeliverMessageAsync(long chat, string text, IReadOnlyList<IReadOnlyList<ChatButton>>? buttons, long? replyTo, bool silent,
        CancellationToken cancellationToken);

    /// <summary>Changes a message's text, and its buttons: none takes them away.</summary>
    protected abstract Task DeliverEditAsync(long chat, long message, string text, IReadOnlyList<IReadOnlyList<ChatButton>>? buttons, CancellationToken cancellationToken);

    /// <summary>Changes only a message's buttons: none takes them away.</summary>
    protected abstract Task DeliverButtonsAsync(long chat, long message, IReadOnlyList<IReadOnlyList<ChatButton>>? buttons, CancellationToken cancellationToken);

    /// <summary>Sends a file, with a caption as the service writes it. Returns its message's id.</summary>
    protected abstract Task<long> DeliverFileAsync(long chat, string fileName, byte[] content, string caption, bool silent, CancellationToken cancellationToken);

    /// <summary>Answers a tap, with a short note only its sender sees when there's one.</summary>
    protected abstract Task AnswerTapAsync(ChatTap tap, string? text, CancellationToken cancellationToken);

    /// <summary>The paired accounts' private chats with the bot; one that can't be had is logged and left out.</summary>
    protected abstract Task<IReadOnlyList<long>> PairedChatsAsync(CancellationToken cancellationToken);

    /// <summary>Sends <paramref name="text"/>, written as the service writes, as the bot's other messages go: for what a service says of its own.</summary>
    protected Task<long> SendTextAsync(long chat, string text, bool silent, CancellationToken cancellationToken) =>
        SendAsync(chat, text, silent: silent, cancellationToken: cancellationToken);

    private async Task<long> SendAsync(long chat, string text, IReadOnlyList<IReadOnlyList<ChatButton>>? buttons = null, long? replyTo = null,
        bool silent = false, string? tabId = null, CancellationToken cancellationToken = default)
    {
        var id = await PacedAsync(token => DeliverMessageAsync(chat, text, buttons, replyTo, silent, token), cancellationToken).ConfigureAwait(false);
        Activity?.Invoke(RemoteActivity.Sent);
        if (tabId is not null)
        {
            Remember(chat, id, tabId);
        }
        return id;
    }

    private async Task SendFileAsync(long chat, string fileName, string markdown, string caption, string? tabId, CancellationToken cancellationToken, bool silent = true)
    {
        var id = await PacedAsync(token => DeliverFileAsync(chat, fileName, System.Text.Encoding.UTF8.GetBytes(markdown), caption, silent, token), cancellationToken)
            .ConfigureAwait(false);
        Activity?.Invoke(RemoteActivity.Sent);
        if (tabId is not null)
        {
            Remember(chat, id, tabId);
        }
    }

    /// <summary>Sends to every paired account. Returns the messages sent, by chat; one that can't go is logged and skipped.</summary>
    private async Task<List<(long Chat, long Message)>> BroadcastAsync(string text, IReadOnlyList<IReadOnlyList<ChatButton>>? buttons = null, bool silent = false,
        string? tabId = null, CancellationToken cancellationToken = default)
    {
        var sent = new List<(long, long)>();
        foreach (var chat in await PairedChatsAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                sent.Add((chat, await SendAsync(chat, text, buttons, null, silent, tabId, cancellationToken).ConfigureAwait(false)));
            }
            catch (ChatApiException ex)
            {
                _logger.LogWarning("{Client}: couldn't send a message: {Error}", Name, ex.Message);
            }
        }
        return sent;
    }

    private static IReadOnlyList<IReadOnlyList<ChatButton>> Rows(IEnumerable<ChatButton> buttons, int perRow) =>
        [.. buttons.Chunk(perRow).Select(row => (IReadOnlyList<ChatButton>)row)];

    /// <summary>A file's name from a tab's: what a file system takes.</summary>
    private static string FileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var safe = new string([.. name.Select(c => invalid.Contains(c) ? '-' : c)]).Trim();
        return safe.Length > 0 ? safe : "Deventry";
    }
}
