using System.Globalization;
using Microsoft.Extensions.Logging;
using static Deventry.Core.Remote.Telegram.TelegramFormat;

namespace Deventry.Core.Remote.Telegram;

/// <summary>The bot's side of the chat (DESIGN.md §18, "Talking to a tab", "Commands"): messages, commands and buttons.</summary>
public sealed partial class TelegramClient
{
    /// <summary>How many of its messages the bot remembers the tab of, for replies to them.</summary>
    public const int RememberedMessages = 1000;

    /// <summary>How many buttons a run keeps the actions of; older ones are stale.</summary>
    public const int RememberedButtons = 5000;

    /// <summary>The commands in Telegram's <b>/</b> menu.</summary>
    public static IReadOnlyList<TelegramCommand> Commands { get; } =
    [
        new("tabs", "The tabs and what they're doing"),
        new("to", "Pick the tab your messages go to"),
        new("stop", "Stop Claude in a tab"),
        new("usage", "The plan's usage"),
        new("quiet", "No messages for an hour (/quiet 30, /quiet off)"),
        new("new", "Open a tab in a folder"),
        new("help", "What I can do"),
    ];

    private IRemoteDesk Desk => _desk ?? throw new InvalidOperationException("The Telegram client hasn't started.");

    /// <summary>Which tab each of the bot's messages was about, by chat and message, for Telegram's replies.</summary>
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
    private DateTimeOffset? _quietUntil;

    private bool IsQuiet => _quietUntil is { } until && until > _time.GetUtcNow();

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

    private TelegramButton MakeButton(string label, Button action)
    {
        var key = Key(++_lastKey);
        _buttons[key] = action;
        _buttonOrder.Enqueue(key);
        while (_buttonOrder.Count > RememberedButtons)
        {
            _buttons.Remove(_buttonOrder.Dequeue());
        }
        return new TelegramButton(label, ButtonData(_run, key));
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

    // ---- Updates ----------------------------------------------------------------------------------------------------

    private async Task HandleUpdateAsync(TelegramUpdate update, bool stale, CancellationToken cancellationToken)
    {
        if (update.Callback is { } callback)
        {
            await HandleCallbackAsync(callback, cancellationToken).ConfigureAwait(false);
        }
        else if (update.Message is { } message)
        {
            await HandleMessageAsync(message, stale, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleMessageAsync(TelegramMessage message, bool stale, CancellationToken cancellationToken)
    {
        // Only private chats count: a group's messages are no one's to answer.
        if (!message.Chat.IsPrivate || message.From is not { IsBot: false } from)
        {
            return;
        }
        var (command, argument) = ParseCommand(message.Text);
        if (!_pairing.IsPaired(from.Id))
        {
            if (command == "start" && !stale && argument is { Length: > 0 }
                && _pairing.TryPair(argument, from.Id, from.Name, from.Username) == PairingOutcome.Paired)
            {
                _logger.LogInformation("Telegram: paired an account.");
                await SendAsync(message.Chat.Id, $"{Escape(RemoteMessages.Paired(Desk.MachineName))}\n\n{HelpHtml()}", cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            // Everyone else gets no answer at all, so the bot doesn't say it belongs to anyone.
            return;
        }
        if (stale)
        {
            _passedOver++;
            return;
        }
        var chat = message.Chat.Id;
        if (command is not null)
        {
            _awaiting.Remove(chat);
            await HandleCommandAsync(chat, command, argument, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (message.HasMedia)
        {
            await SendAsync(chat, "Only text for now.", replyTo: message.MessageId, silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
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
            await SendToTabAsync(chat, tabId, text, message.MessageId, cancellationToken).ConfigureAwait(false);
        }
        else if (_targets.TryGetValue(chat, out var target))
        {
            await SendToTabAsync(chat, target, text, message.MessageId, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await AskWhichTabAsync(chat, text, message.MessageId, cancellationToken).ConfigureAwait(false);
        }
    }

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

    private async Task ReportPassedOverAsync(CancellationToken cancellationToken)
    {
        if (_passedOver > 0)
        {
            var count = _passedOver;
            _passedOver = 0;
            await BroadcastAsync(Escape(RemoteMessages.PassedOver(count)), silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    // ---- Commands ---------------------------------------------------------------------------------------------------

    private Task HandleCommandAsync(long chat, string command, string? argument, CancellationToken cancellationToken) => command switch
    {
        "start" => SendAsync(chat, $"{Escape($"Already paired with Deventry on {Desk.MachineName}.")}\n\n{HelpHtml()}", silent: true, cancellationToken: cancellationToken),
        "help" => SendAsync(chat, HelpHtml(), silent: true, cancellationToken: cancellationToken),
        "tabs" => ListTabsAsync(chat, cancellationToken),
        "to" => PickTargetAsync(chat, argument, cancellationToken),
        "stop" => StopCommandAsync(chat, argument, cancellationToken),
        "usage" => UsageAsync(chat, cancellationToken),
        "quiet" => QuietAsync(chat, argument, cancellationToken),
        "new" => NewTabAsync(chat, cancellationToken),
        _ => SendAsync(chat, $"{Escape($"I don't know /{command}.")}\n\n{HelpHtml()}", silent: true, cancellationToken: cancellationToken),
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
        await SendAsync(chat, TabsHtml(tabs), buttons, silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
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
        await SendAsync(chat, $"Your messages go to {Bold(tab.Name)} now. /to picks another.", silent: true, tabId: tab.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task StopCommandAsync(long chat, string? argument, CancellationToken cancellationToken)
    {
        if (!RemoteAccess.Allows(Desk.Access, Desk.CanOpenTabs, RemoteAction.Stop))
        {
            await SendAsync(chat, Escape(RemoteAccess.Refusal(RemoteAction.Stop)), silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
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
        await SendAsync(chat, result.Done ? $"Stopped {Bold(tab?.Name ?? "the tab")}." : Escape(result.Reason ?? "Couldn't stop it."), silent: true, tabId: tabId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task UsageAsync(long chat, CancellationToken cancellationToken)
    {
        var usage = await Desk.GetUsageAsync(cancellationToken).ConfigureAwait(false);
        var lines = RemoteMessages.Usage(usage, _time.GetUtcNow(), _time.LocalTimeZone);
        await SendAsync(chat, string.Join("\n", lines.Select(Escape)), silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The longest <c>/quiet</c> holds: a day.</summary>
    private const int MaxQuietMinutes = 24 * 60;

    private Task QuietAsync(long chat, string? argument, CancellationToken cancellationToken)
    {
        if (argument is { } off && off.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            _quietUntil = null;
            return SendAsync(chat, "Messages are back on.", silent: true, cancellationToken: cancellationToken);
        }
        var minutes = argument is null ? 60
            : int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out var asked) ? Math.Clamp(asked, 1, MaxQuietMinutes)
            : -1;
        if (minutes < 0)
        {
            return SendAsync(chat, "Say how many minutes, as /quiet 30, or /quiet off.", silent: true, cancellationToken: cancellationToken);
        }
        _quietUntil = _time.GetUtcNow() + TimeSpan.FromMinutes(minutes);
        var until = TimeZoneInfo.ConvertTime(_quietUntil.Value, _time.LocalTimeZone).ToString("t", CultureInfo.CurrentCulture);
        return SendAsync(chat, Escape($"Quiet until {until}: no messages but answers to what you do here. /quiet off ends it sooner."), silent: true,
            cancellationToken: cancellationToken);
    }

    private async Task NewTabAsync(long chat, CancellationToken cancellationToken)
    {
        if (!Desk.CanOpenTabs)
        {
            await SendAsync(chat, Escape(RemoteAccess.Refusal(RemoteAction.OpenTab)), silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
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
            await SendAsync(chat, Escape(result.Reason ?? "Couldn't send it."), replyTo: replyTo, silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }
        _engaged.Add(tabId);
        if (await FindTabAsync(tabId, cancellationToken).ConfigureAwait(false) is not { } tab)
        {
            return;
        }
        // Its message shows what the tab is doing, edited in place: the turn's end is a new one.
        var heading = $"Sent to {Bold(tab.Name)}.";
        var id = await SendAsync(chat, LiveHtml(heading, tab), LiveButtons(tab), replyTo, silent: true, tabId: tab.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
        StartLive(chat, id, heading, tab);
    }

    private async Task AskWhichTabAsync(long chat, string text, long replyTo, CancellationToken cancellationToken)
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

    private async Task HandleCallbackAsync(TelegramCallback callback, CancellationToken cancellationToken)
    {
        if (!_pairing.IsPaired(callback.From.Id))
        {
            // A tap still needs its answer, or its spinner turns; it says nothing.
            await PacedAsync(token => _api.AnswerCallbackAsync(callback.Id, cancellationToken: token), cancellationToken).ConfigureAwait(false);
            return;
        }
        var chat = callback.Message?.Chat.Id ?? callback.From.Id;
        var parsed = ParseButtonData(callback.Data);
        if (parsed is not var (run, key) || run != _run || !_buttons.TryGetValue(key, out var button))
        {
            var why = parsed is var (otherRun, _) && otherRun != _run ? "That button is from before Deventry restarted." : "That button has expired.";
            await PacedAsync(token => _api.AnswerCallbackAsync(callback.Id, why, token), cancellationToken).ConfigureAwait(false);
            if (callback.Message is { } stale)
            {
                await PacedAsync(token => _api.EditButtonsAsync(stale.Chat.Id, stale.MessageId, null, token), cancellationToken).ConfigureAwait(false);
            }
            return;
        }
        switch (button)
        {
            case AnswerButton { Option.TakesText: true } answer:
                await Toast(callback, null, cancellationToken).ConfigureAwait(false);
                await AskForWordsAsync(chat, answer, cancellationToken).ConfigureAwait(false);
                break;
            case AnswerButton answer:
                await AnswerAsync(chat, callback, answer.TabId, answer.PromptId, answer.Option, null, cancellationToken).ConfigureAwait(false);
                break;
            case TabButton tab:
                await Toast(callback, null, cancellationToken).ConfigureAwait(false);
                await SendTabCardAsync(chat, tab.TabId, cancellationToken).ConfigureAwait(false);
                break;
            case TargetButton target:
                await Toast(callback, null, cancellationToken).ConfigureAwait(false);
                if (await FindTabAsync(target.TabId, cancellationToken).ConfigureAwait(false) is { } picked)
                {
                    await SetTargetAsync(chat, picked, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await SendAsync(chat, RemoteMessages.TabGone, silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                break;
            case StopButton stop:
                await Toast(callback, null, cancellationToken).ConfigureAwait(false);
                await StopTabAsync(chat, stop.TabId, cancellationToken).ConfigureAwait(false);
                break;
            case RestartButton restart:
                var restarted = await Desk.RestartAsync(restart.TabId, Name, cancellationToken).ConfigureAwait(false);
                await Toast(callback, restarted.Done ? "Starting it again." : restarted.Reason, cancellationToken).ConfigureAwait(false);
                break;
            case ReplyButton reply:
                await Toast(callback, null, cancellationToken).ConfigureAwait(false);
                await SendWholeReplyAsync(chat, reply.TabId, cancellationToken).ConfigureAwait(false);
                break;
            case FilesButton files:
                await Toast(callback, null, cancellationToken).ConfigureAwait(false);
                await SendChangedFilesAsync(chat, files.TabId, cancellationToken).ConfigureAwait(false);
                break;
            case ShowButton show:
                await Toast(callback, null, cancellationToken).ConfigureAwait(false);
                await ShowPromptAsync(chat, show.TabId, show.PromptId, cancellationToken).ConfigureAwait(false);
                break;
            case SendPendingButton pending:
                await Toast(callback, null, cancellationToken).ConfigureAwait(false);
                _buttons.Remove(key);
                if (callback.Message is { } question)
                {
                    await PacedAsync(token => _api.EditButtonsAsync(question.Chat.Id, question.MessageId, null, token), cancellationToken).ConfigureAwait(false);
                }
                await SendToTabAsync(chat, pending.TabId, pending.Text, null, cancellationToken).ConfigureAwait(false);
                break;
            case FolderButton folder:
                var opened = await Desk.OpenTabAsync(folder.Path, Name, cancellationToken).ConfigureAwait(false);
                await Toast(callback, opened.Done ? null : opened.Reason, cancellationToken).ConfigureAwait(false);
                if (opened.Done && opened.TabId is { } openedId && await FindTabAsync(openedId, cancellationToken).ConfigureAwait(false) is { } openedTab)
                {
                    _targets[chat] = openedTab.Id;
                    await SendAsync(chat, $"Opened {Bold(openedTab.Name)}. Your messages go to it now.", silent: true, tabId: openedTab.Id,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                break;
        }
    }

    private Task Toast(TelegramCallback callback, string? text, CancellationToken cancellationToken) =>
        PacedAsync(token => _api.AnswerCallbackAsync(callback.Id, text, token), cancellationToken);

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
        await SendAsync(chat, Escape(ask), buttons, silent: true, tabId: answer.TabId, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task AnswerAsync(long chat, TelegramCallback? callback, string tabId, string promptId, RemoteOption option, string? words, CancellationToken cancellationToken)
    {
        var result = await Desk.AnswerAsync(tabId, promptId, option.Id, words, Name, cancellationToken).ConfigureAwait(false);
        if (callback is not null)
        {
            await Toast(callback, result.Done ? option.Label.TrimEnd('…') : result.Reason, cancellationToken).ConfigureAwait(false);
        }
        else if (!result.Done)
        {
            await SendAsync(chat, Escape(result.Reason ?? "Couldn't answer it."), silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        // Answered here, the prompt's message is edited once the desk says it closed. One that closed already, while
        // this run wasn't watching it, just loses its buttons.
        if (!result.Done && callback?.Message is { } message && !_promptMessages.ContainsKey(PromptKey(tabId, promptId)))
        {
            await PacedAsync(token => _api.EditButtonsAsync(message.Chat.Id, message.MessageId, null, token), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendWholeReplyAsync(long chat, string tabId, CancellationToken cancellationToken)
    {
        if (await Desk.GetLastReplyAsync(tabId, cancellationToken).ConfigureAwait(false) is not { } reply)
        {
            await SendAsync(chat, "There's no reply to show.", silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }
        var html = $"{Bold(reply.TabName)}\n{FromMarkdown(reply.Markdown)}";
        if (FitsMessage(html))
        {
            await SendAsync(chat, html, silent: true, tabId: tabId, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }
        // Longer than a message: a Markdown file.
        await SendFileAsync(chat, $"{FileName(reply.TabName)} reply.md", reply.Markdown, $"{Bold(reply.TabName)}: the whole reply", tabId, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendChangedFilesAsync(long chat, string tabId, CancellationToken cancellationToken)
    {
        var files = await Desk.GetChangedFilesAsync(tabId, cancellationToken).ConfigureAwait(false);
        if (files is null)
        {
            await SendAsync(chat, RemoteMessages.TabGone, silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }
        var tab = await FindTabAsync(tabId, cancellationToken).ConfigureAwait(false);
        await SendAsync(chat, ChangedFilesHtml(tab?.Name ?? "The tab", files), silent: true, tabId: tabId, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task ShowPromptAsync(long chat, string tabId, string promptId, CancellationToken cancellationToken)
    {
        if (await FindTabAsync(tabId, cancellationToken).ConfigureAwait(false) is { Prompt: { } prompt } tab && prompt.Id == promptId)
        {
            await SendPromptAsync([chat], tab, prompt, RemoteDetail.Everything, silent: true, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await SendAsync(chat, Escape(RemoteMessages.AlreadyAnswered(RemotePromptEnd.Withdrawn, null)), silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    // ---- Sending ----------------------------------------------------------------------------------------------------

    private async Task<long> SendAsync(long chat, string html, IReadOnlyList<IReadOnlyList<TelegramButton>>? buttons = null, long? replyTo = null,
        bool silent = false, string? tabId = null, CancellationToken cancellationToken = default)
    {
        var id = await PacedAsync(token => _api.SendMessageAsync(chat, html, buttons, replyTo, silent, token), cancellationToken).ConfigureAwait(false);
        if (tabId is not null)
        {
            Remember(chat, id, tabId);
        }
        return id;
    }

    private async Task SendFileAsync(long chat, string fileName, string markdown, string captionHtml, string? tabId, CancellationToken cancellationToken, bool silent = true)
    {
        var id = await PacedAsync(token => _api.SendDocumentAsync(chat, fileName, System.Text.Encoding.UTF8.GetBytes(markdown), captionHtml, silent: silent,
            cancellationToken: token), cancellationToken).ConfigureAwait(false);
        if (tabId is not null)
        {
            Remember(chat, id, tabId);
        }
    }

    /// <summary>Sends to every paired account. Returns the messages sent, by chat; one that can't go is logged and skipped.</summary>
    private async Task<List<(long Chat, long Message)>> BroadcastAsync(string html, IReadOnlyList<IReadOnlyList<TelegramButton>>? buttons = null, bool silent = false,
        string? tabId = null, CancellationToken cancellationToken = default)
    {
        var sent = new List<(long, long)>();
        foreach (var chat in Chats)
        {
            try
            {
                sent.Add((chat, await SendAsync(chat, html, buttons, null, silent, tabId, cancellationToken).ConfigureAwait(false)));
            }
            catch (TelegramApiException ex)
            {
                _logger.LogWarning("Telegram: couldn't send a message: {Error}", ex.Message);
            }
        }
        return sent;
    }

    /// <summary>The paired accounts' private chats, whose ids are the accounts'.</summary>
    private IEnumerable<long> Chats => _pairing.Accounts.Select(a => a.UserId).Distinct();

    private static IReadOnlyList<IReadOnlyList<TelegramButton>> Rows(IEnumerable<TelegramButton> buttons, int perRow) =>
        [.. buttons.Chunk(perRow).Select(row => (IReadOnlyList<TelegramButton>)row)];

    /// <summary>A file's name from a tab's: what a file system takes.</summary>
    private static string FileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var safe = new string([.. name.Select(c => invalid.Contains(c) ? '-' : c)]).Trim();
        return safe.Length > 0 ? safe : "Deventry";
    }
}
