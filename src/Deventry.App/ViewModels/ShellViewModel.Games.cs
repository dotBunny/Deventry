using Deventry.App.Services;
using Deventry.App.ViewModels.ToolWindows;

namespace Deventry.App.ViewModels;

/// <summary>
/// Games with Joulene (DESIGN.md §18): the Games tool window, its icon on the strip while games are on, follows the one
/// game that's open or lists them, and the line over the board says when Claude needs the user. A game that runs by the
/// clock hears whether its window shows.
/// </summary>
public sealed partial class ShellViewModel
{
    private void WatchGames()
    {
        _services.Games.Announcer = Announce;
        _services.Games.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(GameService.IsEnabled) or null or "")
            {
                OnGamesEnabledChanged();
            }
        };
        var window = ToolWindows.For(ToolWindowKinds.Games);
        ToolWindows.ShowingChanged += () => _services.Games.SetWindowShowing(window.IsShowing);
        _services.Games.SetWindowShowing(window.IsShowing);
    }

    /// <summary>
    /// The line over the board while tabs wait on the user: the other tabs first, with <b>Go there</b>; the one in front
    /// needs nothing to go to.
    /// </summary>
    private void UpdateGameCall()
    {
        var waiting = AllTabs.Where(t => t.NeedsInput).ToList();
        var others = waiting.Where(t => !ReferenceEquals(t, SelectedTab)).ToList();
        if (others is [var only])
        {
            _services.Games.SetCall($"Claude needs you in {only.DisplayName}", () => SelectTab(only.Id));
        }
        else if (others.Count > 1)
        {
            _services.Games.SetCall($"Claude needs you in {others.Count} tabs", SelectNextNeedingInput);
        }
        else
        {
            _services.Games.SetCall(waiting.Count > 0 ? "Claude is waiting for you in this tab" : null, null);
        }
    }
}
