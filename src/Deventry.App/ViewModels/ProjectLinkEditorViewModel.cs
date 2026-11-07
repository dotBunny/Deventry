using Deventry.Core.ProjectTools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>What the link dialog hands back: the name (empty for none), the address, and its group and icon (null for none).</summary>
public sealed record ProjectLinkFields(string Name, string Url, string? Group = null, string? Icon = null);

/// <summary>
/// The small dialog for one link on Settings' Links page (DESIGN.md §18, "Links"): its name and address, its group and
/// icon in the project's menu, and for a new one, which file it goes in. The address is checked as the project's menu checks it: only https, http and mailto, and
/// only the placeholders it fills in. It hands back what was typed and changes nothing itself.
/// </summary>
public sealed partial class ProjectLinkEditorViewModel : ViewModelBase
{
    private readonly ProjectFileScope? _fixedScope;
    private readonly Func<ProjectFileScope, string?> _fileProblem;
    private readonly Action<ProjectLinkFields, ProjectFileScope> _save;
    private readonly Action _close;

    /// <param name="existing">The link being edited; null for a new one.</param>
    /// <param name="groups">The groups the folder's links have, to offer.</param>
    /// <param name="fileProblem">Why a file can't take links now, such as not being valid JSON; null when it can.</param>
    /// <param name="save">Gets what was typed and chosen, and the file.</param>
    public ProjectLinkEditorViewModel(string folder, ProjectLinkRow? existing, IReadOnlyList<string> groups, Func<ProjectFileScope, string?> fileProblem, Action<ProjectLinkFields, ProjectFileScope> save, Action close)
    {
        Folder = folder;
        _fixedScope = existing?.Scope;
        _fileProblem = fileProblem;
        _save = save;
        _close = close;
        Name = existing?.GivenName ?? "";
        Url = existing?.Url ?? "";
        SaveToShared = existing?.Scope == ProjectFileScope.Shared;
        Groups = groups;
        Group = existing?.Link?.Group ?? "";
        SelectedIcon = IconChoices.First(c => c.Name == existing?.Link?.Icon);
    }

    /// <summary>The section of the project's <b>Links ▸</b> it goes in (<c>"group"</c>); empty for none, at the top (DESIGN.md §18, "Links").</summary>
    [ObservableProperty]
    public partial string Group { get; set; }

    /// <summary>The groups to offer: those the folder's links have.</summary>
    public IReadOnlyList<string> Groups { get; }

    /// <summary>Its icon in the project's menu: none, for a link's, or one of Deventry's by name.</summary>
    public IReadOnlyList<MenuIconChoice> IconChoices { get; } = MenuIconChoice.All(MenuIcons.Link, "A link's");

    [ObservableProperty]
    public partial MenuIconChoice? SelectedIcon { get; set; }

    /// <summary>The tab's folder, where the files are.</summary>
    public string Folder { get; }

    /// <summary>A new link asks which file it goes in; an edited one stays in its file.</summary>
    public bool AsksForFile => _fixedScope is null;

    public string Title => AsksForFile ? "Add a link" : "Edit link";

    /// <summary>What the project's menu shows; empty shows the address.</summary>
    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UrlProblem), nameof(HasUrlProblem))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Url { get; set; }

    /// <summary>Why the address can't be saved; nothing while it's still empty.</summary>
    public string? UrlProblem => Url.Trim().Length == 0 ? null : ProjectLinks.Validate(Url);

    public bool HasUrlProblem => UrlProblem is not null;

    /// <summary>The one-line hint about <c>{branch}</c>, <c>{changelist}</c> and <c>{folderName}</c>.</summary>
    public string PlaceholderHint => ProjectLinks.PlaceholderHint;

    /// <summary>In <c>deventry.json</c>, shared with the project, rather than <c>deventry.local.json</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveToLocal), nameof(FileNote), nameof(FileText), nameof(FileProblem), nameof(HasFileProblem))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool SaveToShared { get; set; }

    public bool SaveToLocal
    {
        get => !SaveToShared;
        set => SaveToShared = !value;
    }

    private ProjectFileScope Scope => _fixedScope ?? (SaveToShared ? ProjectFileScope.Shared : ProjectFileScope.Local);

    public string SharedText => $"Shared with the project ({ProjectFile.SharedName})";

    public string LocalText => $"Just me ({ProjectFile.LocalName})";

    /// <summary>For an edited link: the file it's in.</summary>
    public string FileText => $"In {ProjectFile.FileName(Folder, Scope)}";

    /// <summary>Saving rewrites the file, which drops its comments.</summary>
    public string FileNote => $"Saving rewrites {ProjectFile.FileName(Folder, Scope)} in {Folder}, so comments in it are dropped.";

    /// <summary>Why the chosen file can't be saved, such as "deventry.json isn't valid JSON (…)".</summary>
    public string? FileProblem => _fileProblem(Scope);

    public bool HasFileProblem => FileProblem is not null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        _close();
        _save(new ProjectLinkFields(Name.Trim(), Url.Trim(), string.IsNullOrWhiteSpace(Group) ? null : Group.Trim(), SelectedIcon?.Name), Scope);
    }

    private bool CanSave() => ProjectLinks.Validate(Url) is null && FileProblem is null;

    [RelayCommand]
    private void Cancel() => _close();
}
