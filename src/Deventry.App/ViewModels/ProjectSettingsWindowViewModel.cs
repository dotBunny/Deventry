using Deventry.App.Services;
using Deventry.App.ViewModels.Settings;
using Deventry.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Deventry.App.ViewModels;

/// <summary>
/// A project's settings, in a window of their own (DESIGN.md §14, "The project's settings"): a sidebar of its pages,
/// <b>Links</b>, <b>Actions</b>, <b>Solution</b>, <b>Tools</b> and <b>Group</b>, and the page shown, for one folder. It opens from
/// <b>Project settings…</b> in a group's or a tab's menu, and from the project menu's <b>Add an action…</b> and <b>Add a
/// link…</b>, with a new one started. Each change is saved as it's made. Dispose it when the window closes.
/// </summary>
public sealed partial class ProjectSettingsWindowViewModel : ViewModelBase, IDisposable
{
    public const string LinksPage = "Links";

    public const string ActionsPage = "Actions";

    public const string SolutionPage = "Solution";

    public const string ToolsPage = "Tools";

    public const string GroupPage = "Group";

    /// <summary>The pages, in the sidebar's order.</summary>
    public static readonly IReadOnlyList<string> PageNames = [LinksPage, ActionsPage, SolutionPage, ToolsPage, GroupPage];

    private readonly AppServices _services;
    private readonly SettingsContext _context;

    public ProjectSettingsWindowViewModel(AppServices services, ProjectSettingsViewModel project, string page = LinksPage)
    {
        _services = services;
        _context = new SettingsContext(services);
        Project = project;
        Pages = [new ProjectLinksPage(_context, project), new ProjectActionsPage(_context, project), new ProjectSolutionPage(_context, project), new ProjectToolChoicesPage(_context, project), new ProjectGroupPage(_context, project)];
        foreach (var each in Pages)
        {
            each.RefreshItems();
        }
        NavEntries = [.. Pages.Select(p => SettingsNavEntry.Page(p.Title, p.IconKey))];
        _context.SettingsSaved += OnSettingsChanged;
        _context.AllPagesChanged += OnSettingsChanged;
        Open(page);
    }

    public void Dispose()
    {
        _context.SettingsSaved -= OnSettingsChanged;
        _context.AllPagesChanged -= OnSettingsChanged;
        Project.Dispose();
        _context.Dispose();
    }

    /// <summary>What the pages edit: the folder's project files and this computer's choices for its project.</summary>
    public ProjectSettingsViewModel Project { get; }

    public IReadOnlyList<ProjectPage> Pages { get; }

    /// <summary>The sidebar's list: a row for each page, with its icon.</summary>
    public IReadOnlyList<SettingsNavEntry> NavEntries { get; }

    /// <summary>The name of the page shown, one of <see cref="PageNames"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage), nameof(SelectedNavEntry))]
    public partial string SelectedPage { get; set; } = LinksPage;

    public ProjectPage? CurrentPage => Pages.FirstOrDefault(p => p.Title == SelectedPage);

    partial void OnSelectedPageChanged(string value) => CurrentPage?.OnShown();

    /// <summary>The sidebar's selection: the shown page's row.</summary>
    public SettingsNavEntry? SelectedNavEntry
    {
        get => NavEntries.FirstOrDefault(e => e.Category == SelectedPage);
        set
        {
            if (value is not null)
            {
                SelectedPage = value.Category;
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    /// <summary>The window takes the Claude style's shapes (DESIGN.md §3, "Visual style"), as the main window does.</summary>
    public bool IsClaudeStyle => _services.Settings.Appearance.Style == AppStyle.Claude;

    private void OnSettingsChanged() => OnPropertyChanged(nameof(IsClaudeStyle));

    /// <summary>Shows <paramref name="page"/>: the window was opened, or asked for again, at it.</summary>
    public void Open(string page)
    {
        if (PageNames.Contains(page))
        {
            SelectedPage = page;
        }
    }
}
