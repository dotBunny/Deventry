using System.Collections.ObjectModel;
using Deventry.App.Services;
using Deventry.Core.Credentials;
using Deventry.Core.Tickets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels.Settings;

/// <summary>A way to sign in to a server, as <b>Sign in with</b> lists it.</summary>
public sealed record TicketSignInChoice(TicketSignIn Value, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// One server on a ticket provider's page (DESIGN.md §18, "Tickets", "Signing in"): how Deventry signs in to it, its
/// token, who it's signed in as or why not, and for one that was added, <b>Remove…</b>.
/// </summary>
public sealed partial class TicketServerRow : ObservableObject
{
    private readonly TicketsService _tickets;
    private readonly ITicketProviderType _type;
    private CancellationTokenSource? _signingIn;

    public TicketServerRow(TicketsService tickets, ITicketProviderType type, string server)
    {
        _tickets = tickets;
        _type = type;
        Server = server;
        SignInChoices = [.. type.SignIns.Select(s => new TicketSignInChoice(s, Label(s)))];
    }

    public string Server { get; }

    /// <summary>Its address as it's shown: <c>github.com</c>.</summary>
    public string Name => TicketServers.Key(Server);

    /// <summary>The provider's own server, which isn't removed: github.com.</summary>
    public bool CanRemove => _type.DefaultServer is not { } first || !TicketServers.Same(first, Server);

    public IReadOnlyList<TicketSignInChoice> SignInChoices { get; }

    /// <summary><b>Sign in with</b> shows when there's more than one way.</summary>
    public bool HasSignInChoice => SignInChoices.Count > 1;

    private TicketSignIn SignIn => _tickets.SignInOf(_type.Kind, Server);

    public TicketSignInChoice? SelectedSignIn
    {
        get => SignInChoices.FirstOrDefault(c => c.Value == SignIn);
        set
        {
            if (value is not null && value.Value != SignIn)
            {
                _tickets.SetSignIn(_type.Kind, Server, value.Value);
                Refresh();
            }
        }
    }

    /// <summary>The token's buttons show: it signs in with a token.</summary>
    public bool UsesToken => SignIn == TicketSignIn.Token;

    /// <summary>Sign in with GitHub's button shows.</summary>
    public bool UsesDevice => SignIn == TicketSignIn.Device;

    public bool HasToken => _tickets.HasToken(_type.Kind, Server) == true;

    public string SetTokenText => HasToken ? "Change token…" : "Set token…";

    public string TokenPlaceholder => _type.TokenPlaceholder;

    public string TokenSteps => _type.TokenSteps;

    public string TokenName => $"The token for {Name}";

    /// <summary>Who it's signed in as, or why not, or what it does without signing in.</summary>
    public string StateText
    {
        get
        {
            var state = _tickets.StateOf(_type.Kind, Server);
            if (state.Problem is { } problem)
            {
                return problem;
            }
            if (state.Account is { } account)
            {
                return $"Signed in as {account.Login}{(account.Name is { } name ? $" ({name})" : "")}";
            }
            return SignIn switch
            {
                TicketSignIn.None => $"Not signed in: public repositories only, at {_type.Name}'s 60 requests an hour for this computer.",
                TicketSignIn.Token when !HasToken => _type.DefaultServer is null ? "No token: Deventry is the server's guest." : "No token yet.",
                _ => state.Checked ? "Not signed in." : "Not checked yet.",
            };
        }
    }

    /// <summary>The state is a problem the user should look at: in the caution colors.</summary>
    public bool IsTrouble => _tickets.StateOf(_type.Kind, Server).Problem is not null;

    [ObservableProperty]
    public partial bool IsSettingToken { get; set; }

    [ObservableProperty]
    public partial string NewToken { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => Error is not null;

    [ObservableProperty]
    public partial bool IsChecking { get; private set; }

    [RelayCommand]
    private void StartSettingToken()
    {
        Error = null;
        IsSettingToken = true;
    }

    [RelayCommand]
    private void CancelToken()
    {
        NewToken = "";
        Error = null;
        IsSettingToken = false;
    }

    /// <summary><b>Save token</b>: checked with the server first, then kept in the credential store.</summary>
    [RelayCommand]
    private async Task SaveTokenAsync()
    {
        IsChecking = true;
        Error = null;
        try
        {
            await _tickets.SetTokenAsync(_type.Kind, Server, NewToken);
            NewToken = "";
            IsSettingToken = false;
        }
        catch (ArgumentException ex)
        {
            Error = ex.Message.Split(" (Parameter")[0];
        }
        catch (TicketException ex)
        {
            Error = $"It wasn't kept: {ex.Message}";
        }
        catch (CredentialStoreException ex)
        {
            Error = $"Couldn't keep the token in {_tickets.Credentials.Name}: {ex.Message}";
        }
        finally
        {
            IsChecking = false;
        }
        Refresh();
    }

    /// <summary><b>Remove…</b> on the token.</summary>
    public InlineConfirmation TokenRemoval => field ??= new InlineConfirmation(async () =>
    {
        try
        {
            await _tickets.RemoveTokenAsync(_type.Kind, Server);
        }
        catch (CredentialStoreException ex)
        {
            Error = $"Couldn't remove the token from {_tickets.Credentials.Name}: {ex.Message}";
        }
        Refresh();
    });

    /// <summary><b>Remove…</b> on an added server: it goes, with its token.</summary>
    public InlineConfirmation ServerRemoval => field ??= new InlineConfirmation(async () =>
    {
        try
        {
            await _tickets.RemoveServerAsync(_type.Kind, Server);
        }
        catch (CredentialStoreException ex)
        {
            Error = $"Couldn't remove the token from {_tickets.Credentials.Name}: {ex.Message}";
        }
    });

    public string RemoveServerText => $"Remove {Name}? Its token leaves this computer, and projects on it have no tickets until it's added again.";

    /// <summary><b>Check</b>: asks the server who its sign-in is.</summary>
    [RelayCommand]
    private async Task CheckAsync()
    {
        IsChecking = true;
        await _tickets.CheckAsync(_type.Kind, Server);
        IsChecking = false;
        Refresh();
    }

    // ---- Sign in with GitHub ------------------------------------------------------------------------------------------

    /// <summary>The code to enter at GitHub while signing in: <c>WDJB-MJHT</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSigningIn))]
    public partial string? DeviceCode { get; private set; }

    public bool IsSigningIn => DeviceCode is not null;

    private string? _verificationUri;

    /// <summary><b>Sign in with GitHub</b>: a code to enter at GitHub, which the page shows until it's entered.</summary>
    [RelayCommand]
    private async Task SignInWithGitHubAsync()
    {
        Error = null;
        _signingIn?.Cancel();
        _signingIn = new CancellationTokenSource();
        try
        {
            var (code, done) = await _tickets.SignInWithGitHubAsync(Server, _signingIn.Token);
            DeviceCode = code.UserCode;
            _verificationUri = code.VerificationUri;
            await done;
        }
        catch (TicketException ex)
        {
            Error = ex.Message;
        }
        catch (OperationCanceledException)
        {
        }
        DeviceCode = null;
        Refresh();
    }

    /// <summary><b>Copy and open GitHub</b>: the code on the clipboard, and GitHub's page to enter it in the browser.</summary>
    internal Func<string, string, Task>? CopyAndOpen { get; set; }

    [RelayCommand]
    private Task CopyCodeAndOpenAsync() =>
        DeviceCode is { } code && _verificationUri is { } uri && CopyAndOpen is { } open ? open(code, uri) : Task.CompletedTask;

    [RelayCommand]
    private void CancelSignIn()
    {
        _signingIn?.Cancel();
        DeviceCode = null;
    }

    internal void Refresh() => OnPropertyChanged(string.Empty);

    internal void Stop() => _signingIn?.Cancel();

    private string Label(TicketSignIn signIn) => signIn switch
    {
        TicketSignIn.None => _type.DefaultServer is null ? "Nothing (as a guest)" : "Nothing (public repositories only)",
        TicketSignIn.Token => "A token",
        TicketSignIn.GitHubCli => "The GitHub CLI (gh)",
        TicketSignIn.GitCredentialManager => "Git Credential Manager",
        TicketSignIn.Device => "Sign in with GitHub",
        _ => signIn.ToString(),
    };
}

/// <summary>
/// One ticket provider's page in Settings → Tickets (DESIGN.md §18, "Tickets"), drawn from what the provider declares
/// (<see cref="ITicketProviderType"/>), so a provider needs no page or view of its own: its servers and how Deventry signs
/// in to each, finding a project's source from git when it does, and letting Deventry change tickets. All of it this
/// computer's; tokens are in its credential store.
/// </summary>
public sealed partial class TicketProviderPage : SettingsPage, IDisposable
{
    public TicketProviderPage(SettingsContext context, ITicketProviderType type) : base(context, type.Name)
    {
        Type = type;
        var kind = type.Kind;
        Section($"{kind}.servers", "Servers",
        [
            Info($"tickets.providers.{kind}.servers", type.DefaultServer is null ? $"{type.Name} servers" : $"{type.Name} and {type.ServerWord}s",
                type.TokenSteps, keywords: [.. type.Keywords, "sign in", "token", "server", "account"]),
        ]);
        if (type.FindsFromGit)
        {
            Section($"{kind}.projects", "Projects",
            [
                Setting($"tickets.providers.{kind}.findFromGit", "Find a project's repository from its git remote",
                    s => s.Tickets.Find(kind)?.FindFromGit ?? true, (s, v) => s.Tickets.For(kind).FindFromGit = v,
                    "When the project's deventry.json names no ticket sources.", keywords: ["remote", "origin", "detect"]),
            ]);
        }
        Section($"{kind}.changes", "Changing tickets",
        [
            Setting($"tickets.providers.{kind}.allowChanges", "Let Deventry change tickets",
                s => s.Tickets.Find(kind)?.AllowChanges ?? false, (s, v) => s.Tickets.For(kind).AllowChanges = v,
                "Comment, close or change a ticket's state, and assign it to you, from the Tickets tab, on a click.",
                more: "Only with the token's own rights: one that may only read gets the tracker's refusal.", keywords: ["write", "comment", "close", "assign"]),
        ]);
        Tickets.Changed += OnTicketsChanged;
        RefreshServers();
    }

    public ITicketProviderType Type { get; }

    public string Kind => Type.Kind;

    private TicketsService Tickets => Services.Tickets;

    public override string IconKey => TicketProviderIcons.For(Kind);

    public override string Description => Type.Description;

    public string ServersKey => $"tickets.providers.{Kind}.servers";

    public string FindFromGitKey => $"tickets.providers.{Kind}.findFromGit";

    public string AllowChangesKey => $"tickets.providers.{Kind}.allowChanges";

    public string ProjectsSection => $"{Kind}.projects";

    public bool FindsFromGit => Type.FindsFromGit;

    public void Dispose()
    {
        Tickets.Changed -= OnTicketsChanged;
        foreach (var row in Servers)
        {
            row.Stop();
        }
    }

    private void OnTicketsChanged() => Services.Dispatcher.Post(RefreshServers);

    public ObservableCollection<TicketServerRow> Servers { get; } = [];

    private void RefreshServers()
    {
        var servers = Tickets.ServersOf(Kind);
        if (!servers.SequenceEqual(Servers.Select(s => s.Server)))
        {
            foreach (var gone in Servers.Where(r => !servers.Contains(r.Server)).ToArray())
            {
                gone.Stop();
                Servers.Remove(gone);
            }
            for (var i = 0; i < servers.Count; i++)
            {
                if (i >= Servers.Count || Servers[i].Server != servers[i])
                {
                    Servers.Insert(i, new TicketServerRow(Tickets, Type, servers[i])
                    {
                        CopyAndOpen = async (code, uri) =>
                        {
                            await Services.Platform.SetClipboardTextAsync(code);
                            await Services.Platform.OpenUrlAsync(uri);
                        },
                    });
                }
            }
        }
        foreach (var row in Servers)
        {
            row.Refresh();
        }
        OnPropertyChanged(nameof(HasNoServers));
        // Whether each has a token, once the rows are there: reading one says the service changed, which comes back here.
        foreach (var row in Servers.ToArray())
        {
            _ = Tickets.ReadTokenAsync(Kind, row.Server);
        }
    }

    public bool HasNoServers => Servers.Count == 0;

    public bool IsCredentialStoreAvailable => Tickets.Credentials.IsAvailable;

    public string? CredentialStoreUnavailableText => IsCredentialStoreAvailable ? null
        : $"Deventry keeps tokens only in {Tickets.Credentials.Name}, which isn't available here: {Tickets.Credentials.UnavailableReason}";

    /// <summary><b>Add a server…</b>: its address, typed under the list.</summary>
    [ObservableProperty]
    public partial bool IsAddingServer { get; set; }

    [ObservableProperty]
    public partial string NewServer { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasServerError))]
    public partial string? ServerError { get; private set; }

    public bool HasServerError => ServerError is not null;

    public string AddServerText => $"Add a {Type.ServerWord}…";

    public string ServerPlaceholder => Type.ServerPlaceholder;

    public string NoServersText => $"No {Type.ServerWord}s yet. Add one to read its projects' tickets.";

    [RelayCommand]
    private void StartAddingServer()
    {
        ServerError = null;
        IsAddingServer = true;
    }

    [RelayCommand]
    private void CancelAddingServer()
    {
        NewServer = "";
        ServerError = null;
        IsAddingServer = false;
    }

    [RelayCommand]
    private void AddServer()
    {
        try
        {
            Tickets.AddServer(Kind, NewServer);
            NewServer = "";
            IsAddingServer = false;
            ServerError = null;
        }
        catch (ArgumentException ex)
        {
            ServerError = ex.Message.Split(" (Parameter")[0];
        }
    }

    public bool FindFromGit
    {
        get => Settings.Tickets.Find(Kind)?.FindFromGit ?? true;
        set => Set(value, v => Settings.Tickets.For(Kind).FindFromGit = v);
    }

    public bool AllowChanges
    {
        get => Settings.Tickets.Find(Kind)?.AllowChanges ?? false;
        set => Set(value, v => Settings.Tickets.For(Kind).AllowChanges = v);
    }

    public override void OnShown()
    {
        foreach (var row in Servers)
        {
            if (!Tickets.StateOf(Kind, row.Server).Checked)
            {
                _ = Tickets.CheckAsync(Kind, row.Server);
            }
        }
    }

    protected override void OnReset() => OnPropertyChanged(string.Empty);
}
