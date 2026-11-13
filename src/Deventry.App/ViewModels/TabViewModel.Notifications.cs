using Deventry.App.Conversation;
using Deventry.App.Services;
using Deventry.Core.Protocol;
using Deventry.Core.Remote;
using Deventry.Core.Sessions;

namespace Deventry.App.ViewModels;

/// <summary>
/// This tab's OS notifications (DESIGN.md §10). The notification service decides whether each one is sent. The remote
/// clients hear of the same things, in the same words (DESIGN.md §18, "Remote clients").
/// </summary>
public sealed partial class TabViewModel
{
    partial void OnStatusChanged(TabStatus value)
    {
        _shell.OnTabStatusChanged();
        Remote.OnTabChanged(this);
    }

    /// <summary>
    /// A turn ended while the user may not be looking: the first line of the reply, after where Claude's task list stands
    /// when the turn changed it (DESIGN.md §5, "Tasks").
    /// </summary>
    private void NotifyTurnFinished(ResultMessage result)
    {
        var body = RemoteMessages.TurnLine(_conversation.TurnTasksSummary?.Text, result.Result, Agent);
        _services.Notifications.Notify(NotificationKind.TurnFinished, DisplayName, body, Id);
        _shell.Announce($"{DisplayName}: Claude finished.");
        TellRemoteTurnFinished(result);
    }

    /// <summary>A permission prompt, question or plan is waiting (DESIGN.md §7).</summary>
    private void NotifyNeedsInput(PermissionRequest request)
    {
        var prompt = Items.OfType<PromptItem>().LastOrDefault(p => ReferenceEquals(p.Request, request));
        var remote = prompt is null ? null : ToRemotePrompt(prompt);
        var body = prompt is null ? "Claude is waiting for you." : PromptText(prompt);
        _services.Notifications.Notify(NotificationKind.NeedsInput, DisplayName, body, Id);
        _shell.Announce($"{DisplayName}: {body}");
        if (remote is not null)
        {
            Remote.OnPromptWaiting(this, remote);
        }
    }

    /// <summary>What a waiting prompt asks, in a line: its notification's text, and a sub-thread's in its thread's strip.</summary>
    private string PromptText(PromptItem prompt) =>
        ToRemotePrompt(prompt) is { } remote ? RemoteMessages.PromptLine(remote, Agent) : "Claude is waiting for you.";

    /// <summary>Claude Code stopped with an error, or couldn't start.</summary>
    private void NotifyProcessError(string body)
    {
        _services.Notifications.Notify(NotificationKind.ProcessError, DisplayName, body, Id);
        _shell.Announce($"{DisplayName}: {body}");
        Remote.OnTabFailed(this, body, needsSignIn: false);
    }

    /// <summary>"Notify me when a check-in is sent" (DESIGN.md §5).</summary>
    private void NotifyCheckIn() =>
        _services.Notifications.Notify(NotificationKind.CheckIn, DisplayName,
            "Claude has been at this a while, so Deventry asked how it's going.", Id, checkIns: CheckInSettings);
}
