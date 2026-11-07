using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.App.ViewModels.Settings;
using Deventry.App.Views;
using Deventry.Core.ProjectTools;
using Deventry.Core.Settings;

namespace Deventry.App.UiTests;

/// <summary>
/// A project's settings in their own window, rendered (DESIGN.md §14, "The project's settings"): its heading and pages,
/// the Links page with its dialog, and Settings without them.
/// </summary>
public class ProjectSettingsUiTests
{
    private static ProjectSettingsWindow ShowProjectSettings(TabTestHarness h, TabViewModel tab, string page, out ProjectSettingsWindowViewModel settings, double height = 660)
    {
        settings = new ProjectSettingsWindowViewModel(h.Services, h.Shell.ProjectSettingsFor(tab.Folder)!, page);
        var window = new ProjectSettingsWindow { DataContext = settings, Width = 860, Height = height };
        window.Show();
        UiText.Settle(window);
        return window;
    }

    private static async Task<TabViewModel> OpenTabAsync(TabTestHarness h, bool project)
    {
        var tab = await h.OpenTabAsync();
        if (project)
        {
            await Waiting.UntilAsync(() => tab.ProjectTools.Project is not null, "the project", poll: () => Avalonia.Threading.Dispatcher.UIThread.RunJobs());
        }
        Assert.Equal(project, tab.ProjectTools.Project is not null);
        return tab;
    }

    /// <summary>
    /// The window is the project's (DESIGN.md §14, "The project's settings"): titled and headed by its name after its
    /// group's color, with its folder under it, then its pages, each with its icon, in either style.
    /// </summary>
    [AvaloniaFact]
    public async Task The_project_settings_window_is_headed_by_the_project_and_lists_its_pages_in_either_style()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        UnrealFixture.Write(h.Root, h.WorkFolder);
        var tab = await OpenTabAsync(h, project: true);
        var window = ShowProjectSettings(h, tab, ProjectSettingsWindowViewModel.LinksPage, out var settings);

        Assert.Equal("NightOwl · Project settings", window.Title);
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "PageList");
        var heading = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ProjectHeading");
        Assert.Equal("NightOwl", heading.Text);
        var header = heading.FindAncestorOfType<StackPanel>()!.FindAncestorOfType<StackPanel>()!;
        Assert.Equal(h.WorkFolder, ToolTip.GetTip(header) as string);
        Assert.Contains(header.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == h.WorkFolder && t.IsEffectivelyVisible);
        var dot = header.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single();
        Assert.True(dot.IsEffectivelyVisible);
        Assert.Equal(h.Shell.Groups.Single(g => g.Tabs.Contains(tab)).Color, Assert.IsAssignableFrom<ISolidColorBrush>(dot.Fill).Color);
        Assert.Equal(ProjectSettingsWindowViewModel.LinksPage, settings.SelectedNavEntry!.Title);

        list.SelectedItem = settings.NavEntries.Single(e => e.Title == ProjectSettingsWindowViewModel.ToolsPage);
        UiText.Settle(window);
        Assert.IsType<ProjectToolChoicesPage>(settings.CurrentPage);
        var tools = window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "ToolsPage");
        var toolsText = UiText.Describe(tools, (h.Root, "{root}"));
        Assert.Contains("\"Engine folder\"", toolsText, StringComparison.Ordinal);
        Assert.Contains("[choice] \"Development\"", toolsText, StringComparison.Ordinal);

        // The Claude style, dark: drawn with the app's own tokens, which the style repaints.
        var app = (App)Application.Current!;
        var before = app.RequestedThemeVariant;
        try
        {
            app.Colors.Apply(AppStyle.Claude);
            app.RequestedThemeVariant = ThemeVariant.Dark;
            UiText.Settle(window);
            var folder = header.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == h.WorkFolder);
            Assert.Equal(((ISolidColorBrush)window.FindResource(ThemeVariant.Dark, "MutedTextBrush")!).Color, Assert.IsAssignableFrom<ISolidColorBrush>(folder.Foreground).Color);
        }
        finally
        {
            app.Colors.Apply(AppStyle.Standard);
            app.RequestedThemeVariant = before;
            UiText.Settle(window);
        }

        var shown = UiText.Describe(header, (h.Root, "{root}")) + Environment.NewLine + UiText.Describe(list);
        window.Close();
        await Verify(shown);
    }

    /// <summary>
    /// Each kind of project has an icon of its own among the app's (DESIGN.md §18, "Project types"), for its page in
    /// Project tools and the sidebar's project row, and so do All tools and a folder without a project.
    /// </summary>
    [AvaloniaFact]
    public async Task Each_project_type_has_an_icon_of_its_own_in_the_apps_resources()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        using var settings = new SettingsViewModel(h.Services, null);
        List<string> keys = [.. settings.ProjectTypes.Select(p => p.IconKey), ProjectTypeIcons.Folder, ProjectTypeIcons.Other, settings.AllTools.IconKey];

        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.All(keys, key => Assert.True(Application.Current!.TryFindResource(key, out var icon) && icon is Geometry, key));
    }

    /// <summary>
    /// A project type switched off says <i>Off</i> after its name in the sidebar (DESIGN.md §14), which a screen reader
    /// reads as its status; its page can still be picked.
    /// </summary>
    [AvaloniaFact]
    public async Task A_project_type_switched_off_has_Off_after_its_name_and_can_still_be_picked()
    {
        await using var h = new TabTestHarness(s => s.ProjectTools.Type(Core.ProjectTools.Godot.GodotProvider.KindId).Enabled = false, dispatcher: new AvaloniaUiDispatcher());
        using var settings = new SettingsViewModel(h.Services, null);
        var window = new SettingsWindow { DataContext = settings, Width = 900, Height = 1000 };
        window.Show();
        UiText.Settle(window);
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "SidebarList");
        ListBoxItem Item(string title) => list.GetVisualDescendants().OfType<ListBoxItem>().Single(i => AutomationProperties.GetName(i) == title);

        var godot = Item("Godot");
        var tag = godot.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("settingchip"));
        Assert.True(tag.IsEffectivelyVisible);
        Assert.Equal("Off", tag.GetVisualDescendants().OfType<TextBlock>().Single().Text);
        Assert.Equal("Off", AutomationProperties.GetItemStatus(godot));
        Assert.True(godot.Focusable);
        var title = godot.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Godot" && t.IsEffectivelyVisible);
        Assert.True(tag.TranslatePoint(default, godot)!.Value.X > title.TranslatePoint(new Point(title.Bounds.Width, 0), godot)!.Value.X);
        Assert.False(Item("Unity").GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("settingchip")).IsEffectivelyVisible);

        list.SelectedItem = settings.NavEntries.Single(e => e.Title == "Godot");
        UiText.Settle(window);
        Assert.Same(settings.ProjectType(Core.ProjectTools.Godot.GodotProvider.KindId), settings.CurrentPage);
        window.Close();
    }

    /// <summary>Settings' sidebar has its categories alone: a project's pages are in its own window.</summary>
    [AvaloniaFact]
    public async Task The_settings_sidebar_has_no_project_group()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        await OpenTabAsync(h, project: false);
        using var settings = new SettingsViewModel(h.Services, null, host: h.Shell);
        var window = new SettingsWindow { DataContext = settings, Width = 900, Height = 700 };
        window.Show();
        UiText.Settle(window);

        Assert.DoesNotContain(window.GetVisualDescendants().OfType<ListBox>(), l => l.Name == "ProjectList");
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "SidebarList");
        // The list draws only the rows in view, and reuses them as it scrolls: the headings drawn at the top and at the end.
        var headings = new HashSet<string>(StringComparer.Ordinal);
        void Drawn() => headings.UnionWith(list.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("navheading") && t.IsEffectivelyVisible).Select(t => t.Text ?? ""));
        Drawn();
        list.ScrollIntoView(settings.NavEntries[^1]);
        UiText.Settle(window);
        Drawn();
        Assert.Equal(new[] { "Deventry", "Claude", "Your work", SettingsSidebar.ProjectTools, SettingsSidebar.SourceControl }.Order(StringComparer.Ordinal), headings.Order(StringComparer.Ordinal));
        window.Close();
    }

    [AvaloniaFact]
    public async Task The_Links_page_lists_both_files_links_and_its_dialog_checks_the_address()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        File.WriteAllText(Path.Combine(h.WorkFolder, ProjectFile.SharedName), """
            {
              "actions": [ { "name": "Run tests", "command": "make test" } ],
              "links": [
                { "name": "Board", "url": "https://example.atlassian.net/jira/software/projects/ABC/boards/1" },
                { "name": "Pull request", "url": "https://github.com/org/repo/compare/{branch}?expand=1" },
              ],
            }
            """);
        File.WriteAllText(Path.Combine(h.WorkFolder, ProjectFile.LocalName), """
            { "links": [ { "name": "Notes", "url": "file:///home/me/notes.txt" }, { "url": "mailto:me@example.com" } ] }
            """);
        var tab = await OpenTabAsync(h, project: false);
        var window = ShowProjectSettings(h, tab, ProjectSettingsWindowViewModel.LinksPage, out var settings);
        var page = window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "LinksPage");
        Assert.True(page.IsEffectivelyVisible);
        var shown = UiText.Describe(page, (h.Root, "{root}"));

        settings.Project.AddLinkCommand.Execute(null);
        UiText.Settle(window);
        var dialog = window.GetVisualDescendants().OfType<ProjectLinkEditorView>().Single();
        Assert.True(dialog.IsEffectivelyVisible);
        var address = dialog.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Address");
        address.Text = "javascript:alert(1)";
        UiText.Settle(window);
        var save = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Save");
        Assert.False(save.IsEffectivelyEnabled);
        var dialogText = UiText.Describe(dialog, (h.Root, "{root}"));
        Assert.Contains("\"\"javascript:\" links don't open from Deventry; only https, http and mailto do.\"", dialogText, StringComparison.Ordinal);

        address.Text = "https://example.com/{folderName}";
        UiText.Settle(window);
        Assert.True(save.IsEffectivelyEnabled);
        save.Command!.Execute(null);
        UiText.Settle(window);
        Assert.False(settings.Project.HasEditor);
        Assert.Equal("https://example.com/{folderName}", settings.Project.Links[^1].Url);

        window.Close();
        await Verify($"{shown}\n--- Add a link, with an address that won't open ---\n{dialogText}");
    }

    [AvaloniaFact]
    public async Task The_action_dialog_asks_what_must_exist_and_scrolls_to_Save_in_a_window_at_its_opening_size()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await OpenTabAsync(h, project: false);
        var window = ShowProjectSettings(h, tab, ProjectSettingsWindowViewModel.ActionsPage, out var settings);
        window.Width = 820;
        window.Height = 650;

        // A new action: the dialog at its tallest, with its group and icon.
        settings.Project.AddProjectActionCommand.Execute(null);
        UiText.Settle(window);
        var dialog = window.GetVisualDescendants().OfType<ProjectActionEditorView>().Single();
        Assert.True(dialog.IsEffectivelyVisible);
        // Nothing is cut off: its title is in the window at the top, and Save once scrolled to the end.
        var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "EditorScroller");
        var title = dialog.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Add an action");
        Assert.InRange(title.TranslatePoint(default, window)!.Value.Y, 0, window.ClientSize.Height);
        scroller.ScrollToEnd();
        UiText.Settle(window);
        var save = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Save");
        Assert.InRange(save.TranslatePoint(new Point(0, save.Bounds.Height), window)!.Value.Y, 0, window.ClientSize.Height);
        var ifExists = dialog.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Only show when this exists");
        Assert.Equal("Always shown", ifExists.PlaceholderText);

        h.Platform.FileToPick = Path.Combine(h.WorkFolder, "Build", "Game.exe");
        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Browse…").Command!.Execute(null);
        UiText.Settle(window);
        Assert.Equal("Build/Game.exe", ifExists.Text);

        window.Close();
    }
}
