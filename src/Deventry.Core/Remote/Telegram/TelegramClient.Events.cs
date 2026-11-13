using Microsoft.Extensions.Logging;
using static Deventry.Core.Remote.Telegram.TelegramFormat;

namespace Deventry.Core.Remote.Telegram;

/// <summary>What happens at the desk, as the bot's messages (DESIGN.md §18, "What the bot sends").</summary>
public sealed partial class TelegramClient
{
    /// <summary>The least time between two edits of a live status message.</summary>
    public static readonly TimeSpan LiveEditInterval = TimeSpan.FromSeconds(5);

    /// <summary>How often a live status message's time moves on while nothing else changes.</summary>
    public static readonly TimeSpan LiveRefreshInterval = TimeSpan.FromSeconds(30);

    /// <summary>The tabs the user sent a message to from here: their prompts and their turn's end always come.</summary>
    private readonly HashSet<string> _engaged = new(StringComparer.Ordinal);

    /// <summary>The messages each waiting prompt was sent in, by tab and prompt, with their text, to edit once it closes.</summary>
    private readonly Dictionary<string, List<(long Chat, long Message, string Html)>> _promptMessages = new(StringComparer.Ordinal);

    /// <summary>Each tab's live status message: the last one sent.</summary>
    private readonly Dictionary<string, Live> _live = new(StringComparer.Ordinal);

    private sealed class Live(long chat, long message, string heading, RemoteTab tab)
    {
        public long Chat { get; } = chat;

        public long Message { get; } = message;

        /// <summary>Its first line, as HTML: "Sent to <b>Grappling hook</b>.", or the tab's name.</summary>
        public string Heading { get; } = heading;

        public RemoteTab Tab { get; set; } = tab;

        public DateTimeOffset LastEdit { get; set; }

        public string LastHtml { get; set; } = "";
    }

    private static string PromptKey(string tabId, string promptId) => $"{tabId}\n{promptId}";

    private void OnDeskEvent(RemoteEvent raised) => Post(token => HandleEventAsync(raised, token));

    /// <summary>
    /// Whether a message goes for an event, and how: as the desk says, held by <c>/quiet</c>; but a tab the user sent a
    /// message to from here always answers, silently while quiet.
    /// </summary>
    private RemoteNotice Effective(RemoteNotice notice, string? tabId) =>
        tabId is not null && _engaged.Contains(tabId) ? IsQuiet ? RemoteNotice.Silent : RemoteNotice.Sound
        : IsQuiet ? RemoteNotice.None
        : notice;

    private async Task HandleEventAsync(RemoteEvent raised, CancellationToken cancellationToken)
    {
        switch (raised)
        {
            case RemoteTabChanged changed:
                OnTabChanged(changed.Tab);
                break;
            case RemoteTabClosed closed:
                await OnTabClosedAsync(closed, cancellationToken).ConfigureAwait(false);
                break;
            case RemotePromptWaiting waiting:
                await OnPromptWaitingAsync(waiting, cancellationToken).ConfigureAwait(false);
                break;
            case RemotePromptClosed closed:
                await OnPromptClosedAsync(closed, cancellationToken).ConfigureAwait(false);
                break;
            case RemoteTurnFinished turn:
                await OnTurnFinishedAsync(turn, cancellationToken).ConfigureAwait(false);
                break;
            case RemoteTabFailed failed:
                await OnTabFailedAsync(failed, cancellationToken).ConfigureAwait(false);
                break;
            case RemoteUsageAlert alert when Effective(alert.Notice, null) is var notice and not RemoteNotice.None:
                await BroadcastAsync($"{Bold(alert.Title)}\n{Escape(alert.Body)}", silent: notice == RemoteNotice.Silent, cancellationToken: cancellationToken).ConfigureAwait(false);
                break;
            case RemoteProjectAction action when Effective(action.Notice, null) is var notice and not RemoteNotice.None:
                await BroadcastAsync($"{Bold(action.Title)}\n{Escape(action.Body)}", silent: notice == RemoteNotice.Silent, tabId: action.TabId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                break;
            case RemoteDeskClosing closing:
                // Always said: silently at the desk, where the user is closing it.
                await BroadcastAsync(Escape(RemoteMessages.Closing(Desk.MachineName)), silent: closing.Notice != RemoteNotice.Sound,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task OnTabClosedAsync(RemoteTabClosed closed, CancellationToken cancellationToken)
    {
        _engaged.Remove(closed.TabId);
        foreach (var chat in _targets.Where(t => t.Value == closed.TabId).Select(t => t.Key).ToArray())
        {
            _targets.Remove(chat);
        }
        if (_live.Remove(closed.TabId, out var live))
        {
            _timers.Remove($"live:{closed.TabId}", out var timer);
            timer?.Dispose();
            await EditAsync(live.Chat, live.Message, $"{live.Heading}\n{Italic("The tab closed.")}", null, cancellationToken).ConfigureAwait(false);
        }
    }

    // ---- Prompts ----------------------------------------------------------------------------------------------------

    private async Task OnPromptWaitingAsync(RemotePromptWaiting waiting, CancellationToken cancellationToken)
    {
        var key = PromptKey(waiting.Tab.Id, waiting.Prompt.Id);
        // Sent before, it isn't again when the user goes away.
        if (_promptMessages.ContainsKey(key) || Effective(waiting.Notice, waiting.Tab.Id) is not (var notice and not RemoteNotice.None))
        {
            return;
        }
        await SendPromptAsync([.. Chats], waiting.Tab, waiting.Prompt, Desk.Detail, notice == RemoteNotice.Silent, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends a waiting prompt to <paramref name="chats"/>, with its buttons, and keeps its messages to edit once it closes.</summary>
    private async Task SendPromptAsync(IReadOnlyList<long> chats, RemoteTab tab, RemotePrompt prompt, RemoteDetail detail, bool silent, CancellationToken cancellationToken)
    {
        var key = PromptKey(tab.Id, prompt.Id);
        var html = detail == RemoteDetail.JustWhatHappened ? Escape(RemoteMessages.NeedsYou(tab.Name)) : PromptHtml(tab, prompt);
        var buttons = detail == RemoteDetail.JustWhatHappened
            ? Rows([MakeButton("Show", new ShowButton(tab.Id, prompt.Id))], 1)
            : PromptButtons(tab, prompt);
        var sent = _promptMessages.TryGetValue(key, out var kept) ? kept : _promptMessages[key] = [];
        foreach (var chat in chats)
        {
            try
            {
                // A plan comes as a file first, so its buttons are last in the chat.
                if (detail == RemoteDetail.Everything && prompt is { Kind: RemotePromptKind.Plan, Plan: { Length: > 0 } plan })
                {
                    await SendFileAsync(chat, $"{FileName(tab.Name)} plan.md", plan, $"{Bold(tab.Name)}: the plan", tab.Id, cancellationToken).ConfigureAwait(false);
                }
                var id = await SendAsync(chat, html, buttons, null, silent, tab.Id, cancellationToken).ConfigureAwait(false);
                sent.Add((chat, id, html));
            }
            catch (TelegramApiException ex)
            {
                _logger.LogWarning("Telegram: couldn't send a prompt: {Error}", ex.Message);
            }
        }
    }

    private async Task OnPromptClosedAsync(RemotePromptClosed closed, CancellationToken cancellationToken)
    {
        if (!_promptMessages.Remove(PromptKey(closed.TabId, closed.PromptId), out var messages))
        {
            return;
        }
        var line = Italic(RemoteMessages.Closed(closed.How, closed.Outcome, closed.Client, closed.At, _time.LocalTimeZone));
        foreach (var (chat, message, html) in messages)
        {
            await EditAsync(chat, message, $"{html}\n\n{line}", null, cancellationToken).ConfigureAwait(false);
        }
    }

    // ---- Turns and errors -------------------------------------------------------------------------------------------

    private async Task OnTurnFinishedAsync(RemoteTurnFinished turn, CancellationToken cancellationToken)
    {
        var notice = Effective(turn.Notice, turn.Tab.Id);
        _engaged.Remove(turn.Tab.Id);
        await FinishLiveAsync(turn.Tab, turn.Took, cancellationToken).ConfigureAwait(false);
        if (notice == RemoteNotice.None)
        {
            return;
        }
        var detail = Desk.Detail;
        var html = detail == RemoteDetail.JustWhatHappened ? Escape(RemoteMessages.Finished(turn.Tab.Name)) : TurnHtml(turn);
        var buttons = Rows([MakeButton("Whole reply", new ReplyButton(turn.Tab.Id)), MakeButton("Changed files", new FilesButton(turn.Tab.Id))], 2);
        await BroadcastAsync(html, buttons, notice == RemoteNotice.Silent, turn.Tab.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task OnTabFailedAsync(RemoteTabFailed failed, CancellationToken cancellationToken)
    {
        var notice = Effective(failed.Notice, failed.Tab.Id);
        _engaged.Remove(failed.Tab.Id);
        await FinishLiveAsync(failed.Tab, null, cancellationToken).ConfigureAwait(false);
        if (notice == RemoteNotice.None)
        {
            return;
        }
        var html = Desk.Detail == RemoteDetail.JustWhatHappened
            ? Escape(RemoteMessages.Stopped(failed.Tab.Name))
            : $"{Header(failed.Tab)}\n{Escape(failed.NeedsSignIn ? RemoteMessages.SignInAtDesk : failed.Message)}";
        var buttons = !failed.NeedsSignIn && failed.Tab.CanRestart && RemoteAccess.Allows(Desk.Access, Desk.CanOpenTabs, RemoteAction.Restart)
            ? Rows([MakeButton("Start it again", new RestartButton(failed.Tab.Id))], 1)
            : null;
        await BroadcastAsync(html, buttons, notice == RemoteNotice.Silent, failed.Tab.Id, cancellationToken).ConfigureAwait(false);
    }

    // ---- Live status ------------------------------------------------------------------------------------------------

    /// <summary>A tab's message that shows what it's doing, edited in place at most every 5 seconds, with <b>Stop</b>.</summary>
    private void StartLive(long chat, long message, string heading, RemoteTab tab)
    {
        var live = new Live(chat, message, heading, tab) { LastEdit = _time.GetUtcNow(), LastHtml = LiveHtml(heading, tab) };
        // The tab's earlier one stays as it last read: the new one carries on.
        _live[tab.Id] = live;
        ScheduleLive(tab.Id, LiveRefreshInterval);
    }

    private void OnTabChanged(RemoteTab tab)
    {
        if (_live.TryGetValue(tab.Id, out var live))
        {
            live.Tab = tab;
            ScheduleLive(tab.Id, live.LastEdit + LiveEditInterval - _time.GetUtcNow());
        }
    }

    private void ScheduleLive(string tabId, TimeSpan delay) => After($"live:{tabId}", delay, token => EditLiveAsync(tabId, token));

    private async Task EditLiveAsync(string tabId, CancellationToken cancellationToken)
    {
        if (!_live.TryGetValue(tabId, out var live))
        {
            return;
        }
        var html = LiveHtml(live.Heading, live.Tab);
        if (html != live.LastHtml)
        {
            live.LastHtml = html;
            live.LastEdit = _time.GetUtcNow();
            await EditAsync(live.Chat, live.Message, html, LiveButtons(live.Tab), cancellationToken).ConfigureAwait(false);
        }
        // While it works, its time moves on.
        if (live.Tab.Status is RemoteTabStatus.Working or RemoteTabStatus.NeedsInput or RemoteTabStatus.Starting && _live.ContainsKey(tabId))
        {
            ScheduleLive(tabId, LiveRefreshInterval);
        }
    }

    /// <summary>The turn ended: the live message says so, without <b>Stop</b>, and is edited no more.</summary>
    private async Task FinishLiveAsync(RemoteTab tab, TimeSpan? took, CancellationToken cancellationToken)
    {
        if (!_live.Remove(tab.Id, out var live))
        {
            return;
        }
        _timers.Remove($"live:{tab.Id}", out var timer);
        timer?.Dispose();
        var status = tab.Status == RemoteTabStatus.Error ? RemoteMessages.Status(RemoteTabStatus.Error)
            : took is { } time ? $"Finished in {Formats.Duration(time)}"
            : "Finished";
        await EditAsync(live.Chat, live.Message, $"{live.Heading}\n{Escape(status)}", null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A tab picked from <c>/tabs</c>: what it's doing, live, with its buttons; and its prompt, when one waits.</summary>
    private async Task SendTabCardAsync(long chat, string tabId, CancellationToken cancellationToken)
    {
        if (await FindTabAsync(tabId, cancellationToken).ConfigureAwait(false) is not { } tab)
        {
            await SendAsync(chat, RemoteMessages.TabGone, silent: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }
        var heading = Header(tab);
        var id = await SendAsync(chat, LiveHtml(heading, tab), CardButtons(tab), null, silent: true, tab.Id, cancellationToken).ConfigureAwait(false);
        StartLive(chat, id, heading, tab);
        if (tab.Prompt is { } prompt && !_promptMessages.ContainsKey(PromptKey(tab.Id, prompt.Id)))
        {
            await SendPromptAsync([chat], tab, prompt, Desk.Detail, silent: true, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EditAsync(long chat, long message, string html, IReadOnlyList<IReadOnlyList<TelegramButton>>? buttons, CancellationToken cancellationToken)
    {
        try
        {
            await PacedAsync(token => _api.EditMessageTextAsync(chat, message, html, buttons, token), cancellationToken).ConfigureAwait(false);
        }
        catch (TelegramApiException ex) when (ex.ErrorCode == 400)
        {
            // The message was deleted in the chat, or is too old to edit: nothing more to say there.
        }
    }
}
