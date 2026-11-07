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

    /// <summary>
    /// An unload under way, from looking for the processes Claude left running to its claude having stopped: a start
    /// waits for it.
    /// </summary>
    private Task? _unloading;

    /// <summary>Its claude is running, and nothing in it would notice it stopping: no turn, prompt, task or connection.</summary>
    internal bool CanUnload(TimeSpan idleFor) => _unloading is null && IsIdleFor(idleFor);

    private bool IsIdleFor(TimeSpan idleFor) =>
        _session is not null && _starting is null && !_closing.IsCancellationRequested
        && Status is TabStatus.Idle or TabStatus.Unread
        && _services.Time.GetUtcNow() - _statusSince >= idleFor
        && RunningTaskCount == 0 && !HasActiveAgents && !ProcessMonitor.HasBusyProcesses
        && !RemoteControl.IsConnected && !_restartForAccount
        && !Items.OfType<UserMessageItem>().Any(m => m.IsQueued);

    /// <summary>Stops its claude, if it still can, and waits for the next message.</summary>
    internal async Task UnloadAsync(TimeSpan idleFor)
    {
        if (!CanUnload(idleFor))
        {
            return;
        }
        var unloading = new TaskCompletionSource();
        _unloading = unloading.Task;
        try
        {
            // Processes Claude left running, a dev server say, are the tab's while its claude runs: listed as it closes and
            // stopped with it (DESIGN.md §4, "Cleanup"). Once claude is gone they wouldn't be, so the tab stays as it is.
            // Its MCP servers don't count: Claude Code stops them as it exits. Listing them scans every process: not on the
            // UI thread.
            var mcpServers = await McpServers.LaunchesAsync();
            var running = await Task.Run(() => ProcessMonitor.RunningChildProcesses(mcpServers));
            // A message may have come meanwhile.
            if (running.Count > 0 || !IsIdleFor(idleFor))
            {
                return;
            }
            await StopSessionAsync(interruptTurn: false);
            if (!_closing.IsCancellationRequested && _session is null && _starting is null)
            {
                Status = TabStatus.NotStarted;
            }
        }
        finally
        {
            _unloading = null;
            unloading.SetResult();
        }
    }
}
