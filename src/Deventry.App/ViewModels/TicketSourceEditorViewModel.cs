using Deventry.Core.ProjectTools;
using Deventry.Core.Tickets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>
/// The small dialog for one ticket source on the project settings' Tickets page (DESIGN.md §14, "The project's settings"):
/// its provider, what it reads (a GitHub repository and server; a YouTrack server, project and query), its name, and for a
/// new one which file it goes in. It hands back the source and changes nothing itself.
/// </summary>
public sealed partial class TicketSourceEditorViewModel : ViewModelBase
{
    private readonly ProjectFileScope? _fixedScope;
    private readonly Func<ProjectFileScope, string?> _fileProblem;
    private readonly Action<TicketSource, ProjectFileScope> _save;
    private readonly Action _close;

    /// <param name="existing">The source being edited; null for a new one.</param>
    /// <param name="fileProblem">Why a file can't take sources now, such as not being valid JSON; null when it can.</param>
    public TicketSourceEditorViewModel(string folder, IReadOnlyList<ITicketProviderType> types, TicketSource? existing, ProjectFileScope? scope,
        Func<ProjectFileScope, string?> fileProblem, Action<TicketSource, ProjectFileScope> save, Action close)
    {
        Folder = folder;
        Types = types;
        _fixedScope = existing is null ? null : scope;
        _fileProblem = fileProblem;
        _save = save;
        _close = close;
        SelectedType = types.FirstOrDefault(t => t.Kind == existing?.Provider) ?? types[0];
        Project = existing?.Project ?? "";
        Server = existing is null ? "" : SelectedType.DefaultServer is { } first && TicketServers.Same(existing.Server, first) ? "" : existing.Server;
        Query = existing?.Query ?? "";
        Name = existing?.Name ?? "";
        SaveToShared = scope == ProjectFileScope.Shared;
    }

    public string Folder { get; }

    public IReadOnlyList<ITicketProviderType> Types { get; }

    public bool AsksForFile => _fixedScope is null;

    public string Title => AsksForFile ? "Add a ticket source" : "Edit ticket source";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectWord), nameof(ProjectPlaceholder), nameof(HasQuery), nameof(ServerTitle), nameof(ServerPlaceholder),
        nameof(ProjectProblem), nameof(HasProjectProblem), nameof(ServerProblem), nameof(HasServerProblem))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial ITicketProviderType SelectedType { get; set; }

    public string ProjectWord => SelectedType.ProjectWord;

    public string ProjectPlaceholder => SelectedType.ProjectPlaceholder;

    public bool HasQuery => SelectedType.HasQuery;

    /// <summary>GitHub's server is optional (github.com without one); YouTrack's isn't.</summary>
    public string ServerTitle => SelectedType.DefaultServer is { } first ? $"Server (empty for {first})" : "Server";

    public string ServerPlaceholder => SelectedType.DefaultServer is null ? SelectedType.ServerPlaceholder : SelectedType.ServerPlaceholder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectProblem), nameof(HasProjectProblem))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Project { get; set; }

    /// <summary>Why the project can't be saved; nothing while it's still empty.</summary>
    public string? ProjectProblem => Project.Trim().Length == 0 ? null : SelectedType.CheckProject(Project.Trim());

    public bool HasProjectProblem => ProjectProblem is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerProblem), nameof(HasServerProblem))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Server { get; set; }

    private string? NormalizedServer => Server.Trim().Length == 0 ? SelectedType.DefaultServer : TicketServers.Normalize(Server, SelectedType.ServerHasScheme);

    /// <summary>Why the server can't be saved.</summary>
    public string? ServerProblem => Server.Trim().Length > 0 && NormalizedServer is null ? $"That isn't a server's address, such as {SelectedType.ServerPlaceholder}." : null;

    public bool HasServerProblem => ServerProblem is not null;

    [ObservableProperty]
    public partial string Query { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; }

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

    public string FileText => $"In {ProjectFile.FileName(Folder, Scope)}";

    public string FileNote => $"Saving rewrites {ProjectFile.FileName(Folder, Scope)} in {Folder}, so comments in it are dropped.";

    public string? FileProblem => _fileProblem(Scope);

    public bool HasFileProblem => FileProblem is not null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (NormalizedServer is not { } server)
        {
            return;
        }
        _close();
        var origin = Scope == ProjectFileScope.Shared ? TicketSourceOrigin.Shared : TicketSourceOrigin.Local;
        _save(new TicketSource(SelectedType.Kind, server, Project.Trim(), HasQuery && Query.Trim().Length > 0 ? Query.Trim() : null,
            Name.Trim().Length > 0 ? Name.Trim() : null, origin), Scope);
    }

    private bool CanSave() => Project.Trim().Length > 0 && SelectedType.CheckProject(Project.Trim()) is null && NormalizedServer is not null && FileProblem is null;

    [RelayCommand]
    private void Cancel() => _close();
}
