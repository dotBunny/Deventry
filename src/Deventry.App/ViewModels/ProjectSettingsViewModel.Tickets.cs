using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Deventry.Core.ProjectTools;
using Deventry.Core.Tickets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>
/// A ticket source on the project's Tickets page (DESIGN.md §18, "Tickets"): one of its files' <c>tickets</c> entries,
/// kept as written so saving keeps fields Deventry doesn't edit, or the one found from the git remote.
/// </summary>
/// <param name="Source">What it reads as; null when it can't be read, when it can only be removed.</param>
/// <param name="Problem">Why it can't be read.</param>
/// <param name="Scope">Its file; null for the one found from git, which no file has.</param>
public sealed record ProjectTicketRow(JsonNode? Raw, TicketSource? Source, string? Problem, ProjectFileScope? Scope, string? ProviderName)
{
    public string Name => Source?.DisplayName ?? "(can't be read)";

    public override string ToString() => Name;

    /// <summary>What it reads, with its provider: <i>GitHub · github.com</i>.</summary>
    public string Detail => Source is { } source ? $"{ProviderName ?? source.Provider} · {source.ServerName}" : Problem ?? "";

    /// <summary>Which file it's in, or that it was found from git.</summary>
    public string FileText => Scope is { } scope ? ProjectSettingsViewModel.FileLabel(scope) : "Found from the git remote";

    public bool IsFromGit => Scope is null;

    public bool CanEdit => Source is not null && Scope is not null;

    public bool HasProblem => Problem is not null;
}

/// <summary>
/// The project's settings → <b>Tickets</b> (DESIGN.md §14, "The project's settings"; §18, "Tickets"): the <c>tickets</c>
/// sources of both files in order, the shared file's first, and the one found from git while neither names any. Each can
/// be added, edited, removed or moved within its file, saved at once; only <c>tickets</c> is rewritten.
/// </summary>
public sealed partial class ProjectSettingsViewModel
{
    private readonly Dictionary<ProjectFileScope, string?> _ticketFileErrors = [];

    public ObservableCollection<ProjectTicketRow> TicketSources { get; } = [];

    public bool HasTicketSources => TicketSources.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditTicketSourceCommand), nameof(RemoveTicketSourceCommand), nameof(MoveTicketSourceUpCommand), nameof(MoveTicketSourceDownCommand))]
    public partial ProjectTicketRow? SelectedTicketSource { get; set; }

    public IReadOnlyList<string> TicketFileErrors => [.. _ticketFileErrors.Values.OfType<string>()];

    public bool HasTicketFileErrors => TicketFileErrors.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTicketSourcesError))]
    public partial string? TicketSourcesError { get; private set; }

    public bool HasTicketSourcesError => TicketSourcesError is not null;

    private IReadOnlyList<ITicketProviderType> TicketTypes => _services.Tickets.Types;

    private string? TicketFileProblem(ProjectFileScope scope) => _ticketFileErrors.GetValueOrDefault(scope);

    private void LoadTicketSources()
    {
        TicketSources.Clear();
        var emptied = false;
        foreach (var scope in (ProjectFileScope[])[ProjectFileScope.Shared, ProjectFileScope.Local])
        {
            try
            {
                var file = ProjectFile.FileName(Folder, scope);
                var entries = ProjectFile.ReadTicketEntries(Folder, scope);
                var tickets = ProjectFile.ReadTickets(Folder, scope);
                emptied |= tickets.HasKey && tickets.Entries.Count == 0;
                var origin = scope == ProjectFileScope.Shared ? TicketSourceOrigin.Shared : TicketSourceOrigin.Local;
                for (var i = 0; i < entries.Count; i++)
                {
                    var source = Core.Tickets.TicketSources.ReadEntry(entries[i], TicketTypes, origin, i, $"{file}: tickets[{i}]", out var problem);
                    TicketSources.Add(new ProjectTicketRow(entries[i], source, problem, scope, NameOf(source)));
                }
                _ticketFileErrors[scope] = null;
            }
            catch (InvalidOperationException ex)
            {
                _ticketFileErrors[scope] = ex.Message;
            }
        }
        var named = TicketSources.Select(r => r.Source).OfType<TicketSource>().ToList();
        if (Core.Tickets.TicketSources.FromGitBeside(Folder, named, emptied, TicketTypes, _services.Tickets.Detection()) is { } found)
        {
            TicketSources.Add(new ProjectTicketRow(null, found, null, null, NameOf(found)));
        }
        SelectedTicketSource = null;
        TicketSourcesChanged();
    }

    private string? NameOf(TicketSource? source) => source is null ? null : _services.Tickets.FindType(source.Provider)?.Name;

    private void TicketSourcesChanged()
    {
        OnPropertyChanged(nameof(HasTicketSources));
        OnPropertyChanged(nameof(TicketFileErrors));
        OnPropertyChanged(nameof(HasTicketFileErrors));
        EditTicketSourceCommand.NotifyCanExecuteChanged();
        RemoveTicketSourceCommand.NotifyCanExecuteChanged();
        MoveTicketSourceUpCommand.NotifyCanExecuteChanged();
        MoveTicketSourceDownCommand.NotifyCanExecuteChanged();
    }

    /// <summary><b>Add…</b>: the source dialog, which asks which file it goes in (just the user's by default).</summary>
    [RelayCommand]
    private void AddTicketSource() =>
        Editor = new TicketSourceEditorViewModel(Folder, TicketTypes, null, ProjectFileScope.Local, TicketFileProblem, Insert, CloseEditor);

    private void Insert(TicketSource source, ProjectFileScope scope)
    {
        // A source of the one found from git's provider takes its place; another's sits beside it.
        if (TicketSources.FirstOrDefault(r => r.IsFromGit) is { Source: { } found } fromGit
            && string.Equals(found.Provider, source.Provider, StringComparison.OrdinalIgnoreCase))
        {
            TicketSources.Remove(fromGit);
        }
        var row = new ProjectTicketRow(Json(source, null), source, null, scope, NameOf(source));
        TicketSources.Insert(scope == ProjectFileScope.Shared ? TicketSources.Count(r => r.Scope == ProjectFileScope.Shared) : TicketSources.Count(r => r.Scope is not null), row);
        if (SaveTicketSources(scope))
        {
            SelectedTicketSource = row;
        }
    }

    private JsonNode Json(TicketSource source, JsonNode? raw) => _services.Tickets.Type(source.Provider).ToJson(source, raw);

    [RelayCommand(CanExecute = nameof(CanEditSelectedTicketSource))]
    private void EditTicketSource()
    {
        if (SelectedTicketSource is not { CanEdit: true, Source: { } existing, Scope: { } scope } row)
        {
            return;
        }
        Editor = new TicketSourceEditorViewModel(Folder, TicketTypes, existing, scope, TicketFileProblem, (source, _) =>
        {
            var index = TicketSources.IndexOf(row);
            if (index < 0)
            {
                return;
            }
            var edited = new ProjectTicketRow(Json(source, source.Provider == existing.Provider ? row.Raw : null), source with { Origin = existing.Origin }, null, scope, NameOf(source));
            TicketSources[index] = edited;
            if (SaveTicketSources(scope))
            {
                SelectedTicketSource = edited;
            }
        }, CloseEditor);
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedTicketSource))]
    private void RemoveTicketSource()
    {
        if (SelectedTicketSource is { Scope: { } scope } row && TicketSources.Remove(row))
        {
            SelectedTicketSource = null;
            SaveTicketSources(scope);
        }
    }

    [RelayCommand(CanExecute = nameof(CanMoveTicketSourceUp))]
    private void MoveTicketSourceUp() => MoveTicketSource(-1);

    [RelayCommand(CanExecute = nameof(CanMoveTicketSourceDown))]
    private void MoveTicketSourceDown() => MoveTicketSource(1);

    private void MoveTicketSource(int by)
    {
        if (SelectedTicketSource is not { Scope: { } scope } row || !CanMoveTicketSource(by))
        {
            return;
        }
        var index = TicketSources.IndexOf(row);
        TicketSources.Move(index, index + by);
        SelectedTicketSource = row;
        SaveTicketSources(scope);
    }

    private bool CanEditSelectedTicketSource() => SelectedTicketSource?.CanEdit == true;

    private bool CanRemoveSelectedTicketSource() => SelectedTicketSource is { Scope: not null };

    private bool CanMoveTicketSourceUp() => CanMoveTicketSource(-1);

    private bool CanMoveTicketSourceDown() => CanMoveTicketSource(1);

    private bool CanMoveTicketSource(int by)
    {
        if (SelectedTicketSource is not { Scope: { } scope } row)
        {
            return false;
        }
        var target = TicketSources.IndexOf(row) + by;
        return target >= 0 && target < TicketSources.Count && TicketSources[target].Scope == scope;
    }

    /// <summary>Writes one file's sources as the list has them; when that fails, the list goes back to what the files hold.</summary>
    private bool SaveTicketSources(ProjectFileScope scope)
    {
        try
        {
            ProjectFile.WriteTickets(Folder, scope, TicketSources.Where(r => r.Scope == scope).Select(r => r.Raw?.DeepClone()));
        }
        catch (Exception ex) when (IsSaveFailure(ex))
        {
            LoadTicketSources();
            TicketSourcesError = SaveFailure(scope, ex);
            return false;
        }
        TicketSourcesError = null;
        TicketSourcesChanged();
        _shell.OnTicketSourcesChanged(Folder);
        return true;
    }
}
