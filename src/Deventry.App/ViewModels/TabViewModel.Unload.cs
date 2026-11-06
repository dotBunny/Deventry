using Deventry.App.Conversation;

namespace Deventry.App.ViewModels;

/// <summary>
/// Unloading an idle tab (DESIGN.md §9, "Unloading idle tabs"): its claude stops to give its memory back, and the tab stays
/// as it is, its conversation still there to read. The next message starts it again on the same session.
/// </summary>
public sealed partial class TabViewModel
{
    /// <summary>When <see cref="Status"/> last changed: an idle tab has been idle since then.</summary>
    private DateTimeOffset _statusSince;

    /// <summary>Its claude is running, and nothing in it would notice it stopping: no turn, prompt, task or connection.</summary>
    internal bool CanUnload(TimeSpan idleFor) =>
        _session is not null && _starting is null && !_closing.IsCancellationRequested
        && Status is TabStatus.Idle or TabStatus.Unread
        && _services.Time.GetUtcNow() - _statusSince >= idleFor
        && RunningTaskCount == 0 && !HasActiveAgents && !ProcessMonitor.HasBusyProcesses
        && !RemoteControl.IsConnected && !_restartForAccount
        && !Items.OfType<UserMessageItem>().Any(m => m.IsQueued);

    /// <summary>Stops its claude, leaving the processes Claude started, and waits for the next message.</summary>
    internal async Task UnloadAsync()
    {
        await StopSessionAsync(interruptTurn: false);
        if (!_closing.IsCancellationRequested && _session is null && _starting is null)
        {
            Status = TabStatus.NotStarted;
        }
    }
}
