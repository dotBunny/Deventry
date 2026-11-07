using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;

namespace Deventry.App.Tests;

/// <summary>Find a tab (DESIGN.md §4, "Find a tab"): finding open tabs by pieces of text, and going to one.</summary>
public class TabSearchTests
{
    /// <summary>
    /// Three tabs: <i>Fix login bug</i> and <i>Orbit notes</i> in the <c>work</c> folder's group, and <i>Refactor auth</i>
    /// in the <c>orbit</c> folder's.
    /// </summary>
    private static async Task<(TabViewModel Login, TabViewModel Notes, TabViewModel Auth)> OpenThreeAsync(TabTestHarness h)
    {
        var login = await h.OpenTabAsync();
        login.State.UserName = "Fix login bug";
        var notes = await h.OpenTabAsync();
        notes.State.UserName = "Orbit notes";
        var orbit = Path.Combine(Path.GetDirectoryName(h.WorkFolder)!, "orbit");
        Directory.CreateDirectory(orbit);
        await h.Shell.OpenFolderAsync(orbit);
        var auth = h.Shell.SelectedTab!;
        await TabTestHarness.Eventually(() => auth.Status == TabStatus.Idle && auth.IsSettled, "the orbit tab to start");
        auth.State.UserName = "Refactor auth";
        return (login, notes, auth);
    }

    private static TabSearchViewModel Open(TabTestHarness h)
    {
        h.OnUiThread(() => h.Shell.OpenTabSearchCommand.Execute(null));
        return h.Shell.TabSearch!;
    }

    private static string[] Names(TabSearchViewModel search) => [.. search.Results.Select(r => r.Name)];

    [Fact]
    public async Task With_nothing_typed_it_lists_every_open_tab_in_sidebar_order_with_its_project()
    {
        await using var h = new TabTestHarness();
        await OpenThreeAsync(h);

        var search = Open(h);

        Assert.True(h.Shell.IsTabSearchOpen);
        Assert.Equal(h.Shell.AllTabs.Select(t => t.DisplayName), Names(search));
        Assert.Equal(["work", "work", "orbit"], search.Results.Select(r => r.Project));
        Assert.Equal(search.Results[0], search.Selected);
    }

    [Fact]
    public async Task Each_word_typed_must_be_somewhere_ignoring_case_and_name_matches_come_before_project_ones()
    {
        await using var h = new TabTestHarness();
        await OpenThreeAsync(h);
        var search = Open(h);

        // Pieces of the name, in any order and case.
        search.Query = "LOGIN fix";
        Assert.Equal(["Fix login bug"], Names(search));

        // In one tab's name and in another's project: the name first.
        search.Query = "orb";
        Assert.Equal(["Orbit notes", "Refactor auth"], Names(search));

        // One word in the name, the other in the project.
        search.Query = "auth orbit";
        Assert.Equal(["Refactor auth"], Names(search));

        // The folder's path finds every tab under it, after any whose name or project has the words.
        search.Query = Path.GetFileName(Path.GetDirectoryName(h.WorkFolder)!);
        Assert.Equal(h.Shell.AllTabs.Select(t => t.DisplayName), Names(search));

        search.Query = "login zebra";
        Assert.False(search.HasResults);
        Assert.True(search.HasNoMatches);
        Assert.Null(search.Selected);
    }

    [Fact]
    public void The_words_typed_are_split_on_spaces()
    {
        Assert.Equal(["a", "b"], TabSearchViewModel.Words("  a   b "));
        Assert.Empty(TabSearchViewModel.Words("   "));
    }

    [Fact]
    public async Task Picking_a_tab_selects_it_opens_its_collapsed_group_and_picks_its_row_out_for_a_moment()
    {
        await using var h = new TabTestHarness();
        var (login, _, auth) = await OpenThreeAsync(h);
        var work = h.Shell.Groups.Single(g => g.Tabs.Contains(login));
        h.OnUiThread(() => h.Shell.ToggleGroupCollapsedCommand.Execute(work));
        Assert.True(work.IsCollapsed);
        Assert.Same(auth, h.Shell.SelectedTab);
        var revealed = new List<TabViewModel>();
        h.Shell.TabRevealed += revealed.Add;

        var search = Open(h);
        search.Query = "login";
        h.OnUiThread(() => search.PickCommand.Execute(null));

        Assert.False(h.Shell.IsTabSearchOpen);
        Assert.Same(login, h.Shell.SelectedTab);
        Assert.False(work.IsCollapsed);
        Assert.True(login.IsDrawingAttention);
        Assert.Equal([login], revealed);

        // Another tab found takes the highlight.
        search = Open(h);
        search.Query = "refactor";
        h.OnUiThread(() => search.PickCommand.Execute(null));
        Assert.Same(auth, h.Shell.SelectedTab);
        Assert.False(login.IsDrawingAttention);
        Assert.True(auth.IsDrawingAttention);

        // It fades after a couple of seconds.
        h.Time.Advance(ShellViewModel.AttentionFor);
        await TabTestHarness.Eventually(() => !auth.IsDrawingAttention, "the highlight to end");
    }

    [Fact]
    public async Task Escape_closes_it_without_going_anywhere_and_the_palette_offers_it()
    {
        await using var h = new TabTestHarness();
        var (_, _, auth) = await OpenThreeAsync(h);

        var search = Open(h);
        search.MoveDownCommand.Execute(null);
        search.CloseCommand.Execute(null);

        Assert.False(h.Shell.IsTabSearchOpen);
        Assert.Same(auth, h.Shell.SelectedTab);
        Assert.All(h.Shell.AllTabs, t => Assert.False(t.IsDrawingAttention));

        var command = h.Shell.PaletteEntries().Single(e => e.Label == "Find a tab…");
        await InlineDispatcher.RunAsync(command.Run);
        Assert.True(h.Shell.IsTabSearchOpen);
    }
}
