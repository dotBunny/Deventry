using System.Windows.Input;
using Deventry.Core.ProjectTools;

namespace Deventry.App.ViewModels;

/// <summary>What an entry of the project's menu is.</summary>
public enum ProjectMenuKind
{
    /// <summary>A section's title, such as "Build" or "Editor configuration".</summary>
    Header,
    /// <summary>A project action, such as Launch editor.</summary>
    Action,
    /// <summary>One of a set of choices, such as DebugGame: a radio item.</summary>
    Option,
    Separator,
    /// <summary>Something about the menu itself: Show in Explorer, Show output…, Refresh, and the project's fix.</summary>
    Command,
    /// <summary>A link from the folder's project files, opened in the browser.</summary>
    Link,
    /// <summary><b>Actions ▸</b> or <b>Links ▸</b>: the folder's own, in a menu of their own (<see cref="ProjectMenuEntry.Items"/>).</summary>
    Submenu,
}

/// <summary>One entry of the menu of the project's icon on its group's row (DESIGN.md §18, "Project tools").</summary>
public sealed class ProjectMenuEntry
{
    public required ProjectMenuKind Kind { get; init; }

    public string Label { get; init; } = "";

    public string? Tip { get; init; }

    public bool IsEnabled { get; init; } = true;

    public bool IsChecked { get; init; }

    public ICommand? Command { get; init; }

    public object? Parameter { get; init; }

    /// <summary>One of <see cref="MenuIcons"/>, drawn before the label; null for a choice, which has its radio mark there.</summary>
    public string? Icon { get; init; }

    /// <summary>A submenu's entries, in sections as the menu's are.</summary>
    public IReadOnlyList<ProjectMenuEntry> Items { get; init; } = [];

    public bool IsSeparator => Kind == ProjectMenuKind.Separator;

    public bool IsHeader => Kind == ProjectMenuKind.Header;

    public bool IsOption => Kind == ProjectMenuKind.Option;

    /// <summary>A button in the project's menu that runs something: an action, a choice, a command or a link.</summary>
    public bool IsButton => Kind is ProjectMenuKind.Action or ProjectMenuKind.Option or ProjectMenuKind.Command or ProjectMenuKind.Link;

    public bool IsLink => Kind == ProjectMenuKind.Link;

    public bool IsSubmenu => Kind == ProjectMenuKind.Submenu;

    /// <summary>A choice's radio mark, in the icon's place.</summary>
    public string Mark => Kind == ProjectMenuKind.Option ? IsChecked ? "●" : "○" : "";

    public bool HasMark => Kind == ProjectMenuKind.Option;

    public bool HasIcon => Icon is not null;

    /// <summary>The geometry the view draws for <see cref="Icon"/>.</summary>
    public string IconKey => MenuIconKeys.For(Icon);

    public override string ToString() => Label;
}

/// <summary>The commands the project's menu entries run, each with the entry's <see cref="ProjectMenuEntry.Parameter"/>.</summary>
/// <param name="RunAction">An action, with the <see cref="ProjectAction"/>.</param>
/// <param name="ChooseProject">One of several projects in the folder, with the <see cref="ProjectCandidate"/>.</param>
/// <param name="ChooseOption">A per-project choice, with the <see cref="ProjectChoiceOption"/>.</param>
/// <param name="Fix">The project's fix, such as <b>Choose engine folder…</b>.</param>
/// <param name="OpenLink">A link, with the <see cref="ResolvedLink"/>.</param>
/// <param name="ShowOutput"><b>Show output…</b>: the newest run's log, in the main area.</param>
/// <param name="Refresh"><b>Refresh</b>: look for the project and its files again.</param>
/// <param name="ShowFolder"><b>Show in Explorer</b> (Finder, the file manager): the project's folder.</param>
internal sealed record ProjectMenuCommands(
    ICommand RunAction, ICommand ChooseProject, ICommand ChooseOption, ICommand Fix, ICommand OpenLink,
    ICommand ShowOutput, ICommand Refresh, ICommand ShowFolder);

/// <summary>
/// Builds the menu of the project's icon on its group's row (DESIGN.md §18, "Project tools"), in sections under small
/// titles as the sidebar's other menus are: the projects to choose from when the folder has several; the project type's
/// actions by what they do (Editor, with the project files' solution and the project's fix, Build, Clean up); its
/// choice; then Folder, with the folder's own actions and links in submenus, <b>Actions ▸</b> and <b>Links ▸</b>, and
/// what the menu itself offers. In each submenu, those with no group come first, then each group under its name. Lines
/// fall only between sections that are there.
/// </summary>
internal static class ProjectMenu
{
    /// <summary>The submenu of the folder's own actions.</summary>
    public const string ActionsTitle = "Actions";

    /// <summary>The submenu of the folder's links.</summary>
    public const string LinksTitle = "Links";

    /// <summary>The section with the folder's own actions and links, and what the menu itself offers.</summary>
    public const string FolderTitle = "Folder";

    /// <param name="project">The project in use; null when none was recognized.</param>
    /// <param name="candidates">Every project found for the folder: with more than one, the menu offers a choice.</param>
    /// <param name="solution">Opening the project files' <c>solution</c>, which goes with the editor; null for none.</param>
    /// <param name="customActions">The folder's own actions, from its project files.</param>
    /// <param name="links">The links of the folder's project files, filled in for the tab.</param>
    /// <param name="hasTools">The tab has project tools, so the menu offers <b>Show output…</b>.</param>
    /// <param name="hasRuns">A job has run in the tab, so <b>Show output…</b> has a log to show.</param>
    /// <param name="forJob">How an action shows as things are: while a job runs, other jobs wait.</param>
    /// <param name="folder">The folder <b>Show in Explorer</b> opens: the project's, else the tab's.</param>
    public static IReadOnlyList<ProjectMenuEntry> Build(
        ProjectInfo? project, IReadOnlyList<ProjectCandidate> candidates, ProjectAction? solution, IReadOnlyList<ProjectAction> customActions,
        IReadOnlyList<ResolvedLink> links, bool hasTools, bool hasRuns, Func<ProjectAction, ProjectAction> forJob, string folder, ProjectMenuCommands commands)
    {
        ProjectMenuEntry ActionEntry(ProjectAction action) => new()
        {
            Kind = ProjectMenuKind.Action,
            Label = action.Label,
            Tip = action.Tip,
            IsEnabled = action.IsEnabled,
            Command = commands.RunAction,
            Parameter = action,
            Icon = action.MenuIcon,
        };
        ProjectMenuEntry LinkEntry(ResolvedLink link) => new()
        {
            Kind = ProjectMenuKind.Link,
            Label = link.Name,
            Tip = link.Tip,
            IsEnabled = link.IsEnabled,
            Command = commands.OpenLink,
            Parameter = link,
            Icon = link.Icon ?? MenuIcons.Link,
        };

        var sections = new Sections();
        if (candidates.Count > 1)
        {
            sections.Add("Projects in this folder").AddRange(candidates.Select(candidate => new ProjectMenuEntry
            {
                Kind = ProjectMenuKind.Option,
                Label = candidate.Name,
                Tip = candidate.Path,
                IsChecked = project is { } current && current.ProjectPath == candidate.Path,
                Command = commands.ChooseProject,
                Parameter = candidate,
            }));
        }
        // The type's own sections in their order, whichever of its actions come first.
        foreach (var group in ProjectActionGroups.All.Where(g => project?.Actions.Any(a => a.Group == g) == true))
        {
            sections.Named(group);
        }
        foreach (var action in project?.Actions ?? [])
        {
            sections.Named(action.Group ?? project!.KindName).Add(ActionEntry(forJob(action)));
        }
        // The project files' solution is opened as the type's own is, so it goes with the editor, in a folder without a
        // project type too.
        if (solution is not null)
        {
            sections.Named(ProjectActionGroups.Editor).Add(ActionEntry(forJob(solution)));
        }
        if (project?.Fix is { } fix)
        {
            sections.Named(ProjectActionGroups.Editor).Add(new ProjectMenuEntry
            {
                Kind = ProjectMenuKind.Command,
                Label = fix.Label,
                Tip = fix.Title,
                Command = commands.Fix,
                Icon = fix.PickFolder ? MenuIcons.Folder : MenuIcons.File,
            });
        }
        if (project?.Choice is { } choice)
        {
            sections.Add(choice.Label).AddRange(choice.Options.Select(option => new ProjectMenuEntry
            {
                Kind = ProjectMenuKind.Option,
                Label = option.Label,
                IsChecked = option.Value == choice.Selected,
                Command = commands.ChooseOption,
                Parameter = option,
            }));
        }

        var menu = sections.Add(FolderTitle);
        if (customActions.Count > 0)
        {
            menu.Add(new ProjectMenuEntry
            {
                Kind = ProjectMenuKind.Submenu,
                Label = ActionsTitle,
                Tip = "Your own actions, from the project's deventry.json and deventry.local.json",
                Icon = MenuIcons.Terminal,
                Items = Grouped(customActions, a => a.Group, a => ActionEntry(forJob(a))),
            });
        }
        if (links.Count > 0)
        {
            menu.Add(new ProjectMenuEntry
            {
                Kind = ProjectMenuKind.Submenu,
                Label = LinksTitle,
                Tip = "Your own links, from the project's deventry.json and deventry.local.json",
                Icon = MenuIcons.Link,
                Items = Grouped(links, l => l.Group, LinkEntry),
            });
        }
        menu.Add(new ProjectMenuEntry
        {
            Kind = ProjectMenuKind.Command,
            Label = ShowFolderLabel(ToolOSExtensions.Current),
            Tip = folder,
            Command = commands.ShowFolder,
            Icon = MenuIcons.Folder,
        });
        // Without project tools there's no job to have output; until one has run, there's none yet.
        if (hasTools)
        {
            menu.Add(new ProjectMenuEntry
            {
                Kind = ProjectMenuKind.Command,
                Label = "Show output…",
                Tip = hasRuns ? "The newest action's log" : "No action has run in this tab yet",
                IsEnabled = hasRuns,
                Command = commands.ShowOutput,
                Icon = MenuIcons.Log,
            });
        }
        menu.Add(new ProjectMenuEntry
        {
            Kind = ProjectMenuKind.Command,
            Label = "Refresh",
            Tip = "Look for the project and its files again",
            Command = commands.Refresh,
            Icon = MenuIcons.Refresh,
        });
        return sections.Entries();
    }

    /// <summary>
    /// A submenu's entries: those with no group first, untitled, then each group under its name, in the order it first
    /// comes. A group is found whatever its case, and keeps the name it first came with.
    /// </summary>
    private static IReadOnlyList<ProjectMenuEntry> Grouped<T>(IReadOnlyList<T> items, Func<T, string?> group, Func<T, ProjectMenuEntry> entry)
    {
        var sections = new Sections();
        var ungrouped = sections.Add(null);
        foreach (var item in items)
        {
            (group(item) is { } name ? sections.Named(name) : ungrouped).Add(entry(item));
        }
        return sections.Entries();
    }

    /// <summary>
    /// What a menu shows and what its entries do, as text: the same for two menus whose entries look and act alike, as
    /// the menu made again after its look at the files found nothing new is.
    /// </summary>
    public static string Shape(IReadOnlyList<ProjectMenuEntry> entries)
    {
        var text = new System.Text.StringBuilder();
        Append(entries);
        return text.ToString();

        void Append(IReadOnlyList<ProjectMenuEntry> list)
        {
            foreach (var entry in list)
            {
                text.Append(entry.Kind).Append('\u001f').Append(entry.Label).Append('\u001f').Append(entry.Tip).Append('\u001f')
                    .Append(entry.IsEnabled).Append(entry.IsChecked).Append('\u001f').Append(entry.Icon).Append('\u001f')
                    .Append(entry.Parameter switch
                    {
                        ProjectAction action => $"{action.Id}\u001f{action.Kind}\u001f{action.CommandText}\u001f{action.OpenPath}\u001f{action.ThenOnSuccess?.CommandText}",
                        ResolvedLink link => $"{link.Url}\u001f{link.Problem}",
                        ProjectCandidate candidate => candidate.Path,
                        ProjectChoiceOption option => option.Value,
                        _ => "",
                    })
                    .Append('\u001e');
                if (entry.Items.Count > 0)
                {
                    text.Append('[');
                    Append(entry.Items);
                    text.Append(']');
                }
            }
        }
    }

    /// <summary>The entry that opens the project's folder, in each OS's name for its file manager.</summary>
    public static string ShowFolderLabel(ToolOS os) => os switch
    {
        ToolOS.Windows => "Show in Explorer",
        ToolOS.MacOS => "Show in Finder",
        _ => "Show in file manager",
    };

    /// <summary>
    /// A menu's sections in order. The project type's are found by name, ignoring case, as are a submenu's groups; the
    /// projects to choose from, the choice and Folder are sections of their own.
    /// </summary>
    private sealed class Sections
    {
        private readonly List<(string? Title, bool Named, List<ProjectMenuEntry> Entries)> _sections = [];

        /// <summary>The section called <paramref name="title"/>: the one there is, or a new one at the end.</summary>
        public List<ProjectMenuEntry> Named(string title)
        {
            foreach (var section in _sections)
            {
                if (section.Named && string.Equals(section.Title, title, StringComparison.OrdinalIgnoreCase))
                {
                    return section.Entries;
                }
            }
            return Add(title, named: true);
        }

        /// <summary>A section nothing else joins, at the end; with no title, it has none over it.</summary>
        public List<ProjectMenuEntry> Add(string? title) => Add(title, named: false);

        private List<ProjectMenuEntry> Add(string? title, bool named)
        {
            var entries = new List<ProjectMenuEntry>();
            _sections.Add((title, named, entries));
            return entries;
        }

        /// <summary>Each section that has entries, under its title, with a line before every one but the first.</summary>
        public IReadOnlyList<ProjectMenuEntry> Entries()
        {
            var all = new List<ProjectMenuEntry>();
            foreach (var (title, _, entries) in _sections.Where(s => s.Entries.Count > 0))
            {
                if (all.Count > 0)
                {
                    all.Add(new ProjectMenuEntry { Kind = ProjectMenuKind.Separator });
                }
                if (title is not null)
                {
                    all.Add(new ProjectMenuEntry { Kind = ProjectMenuKind.Header, Label = title });
                }
                all.AddRange(entries);
            }
            return all;
        }
    }
}
