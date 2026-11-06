namespace Deventry.App.ViewModels;

/// <summary>
/// Unloading idle tabs (DESIGN.md §9, "Unloading idle tabs"): with Settings → General's <b>Unload idle tabs</b> on, a tab
/// left idle that long has its claude stopped, and starts it again with its next message.
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>Looks for idle tabs once a minute; the setting is read each time, so changing it needs nothing else.</summary>
    private UiTicker? _idleUnloadTicker;

    private UiTicker IdleUnloadTicker => _idleUnloadTicker ??= new(_services.Time, _services.Dispatcher, TimeSpan.FromMinutes(1), UnloadIdleTabs);

    private void UnloadIdleTabs()
    {
        if (_services.Settings.Sessions is not { UnloadIdleTabs: true } sessions)
        {
            return;
        }
        var idleFor = TimeSpan.FromMinutes(Math.Max(1, sessions.UnloadIdleTabsAfterMinutes));
        foreach (var tab in AllTabs.Where(t => t.CanUnload(idleFor)).ToArray())
        {
            _ = tab.UnloadAsync();
        }
    }
}
