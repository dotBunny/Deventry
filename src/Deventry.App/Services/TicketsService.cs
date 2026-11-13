using Deventry.Core.Credentials;
using Deventry.Core.Settings;
using Deventry.Core.Tickets;
using Deventry.Core.Tickets.GitHub;
using Deventry.Core.Tickets.YouTrack;

namespace Deventry.App.Services;

/// <summary>What Settings shows about a server: who it's signed in as, or why not; null fields until it's been checked.</summary>
public sealed record TicketServerState(TicketAccount? Account, string? Problem, bool Checked)
{
    public static TicketServerState Unchecked { get; } = new(null, null, false);
}

/// <summary>
/// The ticket providers (DESIGN.md §18, "Tickets"): the kinds Deventry knows (<see cref="AllTypes"/>), each one's
/// servers and how Deventry signs in to them, their tokens in the OS credential store, a tracker for each server, and a
/// folder's sources. A token from the GitHub CLI or Git Credential Manager is kept in memory only, for this run.
/// </summary>
public sealed class TicketsService
{
    /// <summary>The providers Deventry knows, in the order Settings lists them.</summary>
    public static IReadOnlyList<ITicketProviderType> AllTypes { get; } = [new GitHubTicketsType(), new YouTrackTicketsType()];

    /// <summary>How many comments a ticket's read with.</summary>
    public const int MaxComments = 100;

    private readonly AppServices _services;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, string?> _tokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string? Token, ITicketTracker Tracker)> _trackers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TicketServerState> _states = new(StringComparer.OrdinalIgnoreCase);

    public TicketsService(AppServices services, ICredentialStore credentials)
    {
        _services = services;
        Credentials = credentials;
        Sources = new GitHubCredentialSources(services.Launcher, services.Time, (spec, ct) => services.UserEnvironment.ApplyAsync(spec, ct), () => services.Git.Executable);
        DeviceFlow = () => GitHubDeviceFlow.DeventryClientId is { } clientId ? new GitHubDeviceFlow(services.Http, services.Time, clientId, services.UserAgent) : null;
    }

    /// <summary>The providers; tests may give others.</summary>
    public IReadOnlyList<ITicketProviderType> Types { get; internal set; } = AllTypes;

    internal ICredentialStore Credentials { get; set; }

    /// <summary>Where GitHub's tokens from other programs come from; tests give fake processes.</summary>
    internal GitHubCredentialSources Sources { get; set; }

    /// <summary>Sign in with GitHub's flow; null until Deventry has its app's client id.</summary>
    internal Func<GitHubDeviceFlow?> DeviceFlow { get; set; } = () => null;

    /// <summary>A server's sign-in, token or who it's signed in as changed: Settings and the Tickets tabs look again.</summary>
    public event Action? Changed;

    private TicketSettings Settings => _services.Settings.Tickets;

    public ITicketProviderType Type(string kind) => Types.First(t => string.Equals(t.Kind, kind, StringComparison.OrdinalIgnoreCase));

    public ITicketProviderType? FindType(string kind) => Types.FirstOrDefault(t => string.Equals(t.Kind, kind, StringComparison.OrdinalIgnoreCase));

    private static string Key(string kind, string server) => $"{kind}:{TicketServers.Key(server)}";

    // ---- Servers -------------------------------------------------------------------------------------------------------

    /// <summary>A provider's servers: the one it has without being added, then those added, in their order.</summary>
    public IReadOnlyList<string> ServersOf(string kind)
    {
        var type = Type(kind);
        var added = Settings.Providers.TryGetValue(type.Kind, out var provider) ? provider.Servers.Select(s => s.Server) : [];
        return type.DefaultServer is { } first ? [first, .. added.Where(s => !TicketServers.Same(s, first))] : [.. added];
    }

    /// <summary>How Deventry signs in to <paramref name="server"/>: what Settings says, else the provider's first way.</summary>
    public TicketSignIn SignInOf(string kind, string server)
    {
        var type = Type(kind);
        var chosen = Settings.Providers.TryGetValue(type.Kind, out var provider) ? provider.Server(server)?.SignIn : null;
        return chosen is { } signIn && type.SignIns.Contains(signIn) ? signIn : type.SignIns[0];
    }

    /// <summary><b>Sign in with</b> for a server: it's asked again with the new way.</summary>
    public void SetSignIn(string kind, string server, TicketSignIn signIn)
    {
        var provider = Settings.For(kind);
        if (provider.Server(server) is { } known)
        {
            known.SignIn = signIn;
        }
        else
        {
            provider.Servers.Add(new TicketServerSettings { Server = server, SignIn = signIn });
        }
        _services.SaveSettings();
        Forget(kind, server);
        _ = CheckAsync(kind, server);
    }

    /// <summary>
    /// A server a project's source names, which Settings lists from then on, so its sign-in and token can be set there,
    /// as if it had been added: a YouTrack server's, or a GitHub Enterprise server's. One listed already is left as it is.
    /// </summary>
    public void Remember(string kind, string server)
    {
        if (FindType(kind) is not { } type || ServersOf(type.Kind).Any(s => TicketServers.Same(s, server)))
        {
            return;
        }
        Settings.For(type.Kind).Servers.Add(new TicketServerSettings
        {
            Server = server,
            SignIn = type.SignIns.Contains(TicketSignIn.Token) ? TicketSignIn.Token : type.SignIns[0],
        });
        _services.SaveSettings();
        Changed?.Invoke();
    }

    /// <summary><b>Add a server…</b>: its address as it's written. Throws why it can't be one.</summary>
    /// <exception cref="ArgumentException">It isn't a server's address, or it's there already.</exception>
    public string AddServer(string kind, string text)
    {
        var type = Type(kind);
        var server = TicketServers.Normalize(text, type.ServerHasScheme)
            ?? throw new ArgumentException($"That isn't a server's address, such as {type.ServerPlaceholder}.", nameof(text));
        if (ServersOf(kind).Any(s => TicketServers.Same(s, server)))
        {
            throw new ArgumentException($"{TicketServers.Key(server)} is there already.", nameof(text));
        }
        Settings.For(kind).Servers.Add(new TicketServerSettings { Server = server, SignIn = type.SignIns.Contains(TicketSignIn.Token) ? TicketSignIn.Token : type.SignIns[0] });
        _services.SaveSettings();
        Changed?.Invoke();
        return server;
    }

    /// <summary><b>Remove…</b> on an added server: it goes, with its token.</summary>
    /// <exception cref="CredentialStoreException">Its token couldn't be removed.</exception>
    public async Task RemoveServerAsync(string kind, string server)
    {
        if (HasToken(kind, server) != false && Credentials.IsAvailable)
        {
            await Credentials.DeleteAsync(TicketSecrets.Key(kind, server)).ConfigureAwait(true);
        }
        Settings.For(kind).Servers.RemoveAll(s => TicketServers.Same(s.Server, server));
        _services.SaveSettings();
        Forget(kind, server);
        lock (_lock)
        {
            _states.Remove(Key(kind, server));
        }
        Changed?.Invoke();
    }

    /// <summary>The servers a provider finds a project's source from git on, for <see cref="SourcesFor"/>.</summary>
    public TicketSources.Detection Detection() => new(
        Types.ToDictionary(t => t.Kind, t => (IReadOnlyCollection<string>)ServersOf(t.Kind), StringComparer.OrdinalIgnoreCase),
        kind => Settings.Find(kind)?.FindFromGit ?? true);

    /// <summary><paramref name="folder"/>'s ticket sources, read from its files and git's: off the UI thread.</summary>
    public ProjectTicketSources SourcesFor(string folder) => TicketSources.Read(folder, Types, Detection());

    /// <summary><b>Let Deventry change tickets</b> is on for the provider.</summary>
    public bool AllowsChanges(string kind) => Settings.Find(kind)?.AllowChanges ?? false;

    // ---- Tokens ------------------------------------------------------------------------------------------------------

    /// <summary>The server has a token in the credential store; null until it's been read.</summary>
    public bool? HasToken(string kind, string server)
    {
        lock (_lock)
        {
            return _tokens.TryGetValue("stored:" + Key(kind, server), out var token) ? token is not null : null;
        }
    }

    /// <summary>
    /// <b>Set token…</b>: checked with the server first, then kept in the credential store. Throws why it wasn't kept.
    /// </summary>
    /// <exception cref="TicketException">The server refused it, or couldn't be reached.</exception>
    /// <exception cref="CredentialStoreException">It couldn't be kept.</exception>
    public async Task<TicketAccount?> SetTokenAsync(string kind, string server, string token)
    {
        var type = Type(kind);
        var trimmed = token.Trim();
        if (trimmed.Length == 0 || trimmed.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("A token is one word, with no spaces.", nameof(token));
        }
        var account = await type.Connect(Context(server, trimmed)).WhoAmIAsync().ConfigureAwait(true);
        await Credentials.WriteAsync(TicketSecrets.Key(kind, server), TicketSecrets.Label(type.Name, server), trimmed).ConfigureAwait(true);
        lock (_lock)
        {
            _tokens["stored:" + Key(kind, server)] = trimmed;
            _states[Key(kind, server)] = new TicketServerState(account, null, true);
        }
        Forget(kind, server, keepStored: true);
        Changed?.Invoke();
        return account;
    }

    /// <summary><b>Remove…</b> on a token: it leaves the credential store.</summary>
    /// <exception cref="CredentialStoreException">It couldn't be removed.</exception>
    public async Task RemoveTokenAsync(string kind, string server)
    {
        await Credentials.DeleteAsync(TicketSecrets.Key(kind, server)).ConfigureAwait(true);
        lock (_lock)
        {
            _tokens["stored:" + Key(kind, server)] = null;
            _states[Key(kind, server)] = new TicketServerState(null, null, true);
        }
        Forget(kind, server, keepStored: true);
        Changed?.Invoke();
    }

    /// <summary>The token a server's requests go with, as its sign-in gets it; null to ask without one.</summary>
    /// <exception cref="TicketException">The way it signs in couldn't give one: gh not signed in, no credential in git.</exception>
    public async Task<string?> TokenAsync(string kind, string server, CancellationToken cancellationToken = default)
    {
        var signIn = SignInOf(kind, server);
        if (signIn == TicketSignIn.None)
        {
            return null;
        }
        if (signIn is TicketSignIn.Token or TicketSignIn.Device)
        {
            return await StoredTokenAsync(kind, server, cancellationToken).ConfigureAwait(false);
        }
        var key = $"{signIn}:{Key(kind, server)}";
        lock (_lock)
        {
            if (_tokens.TryGetValue(key, out var kept) && kept is not null)
            {
                return kept;
            }
        }
        var token = signIn == TicketSignIn.GitHubCli
            ? await Sources.FromGitHubCliAsync(TicketServers.Key(server), cancellationToken).ConfigureAwait(false)
            : await Sources.FromGitCredentialManagerAsync(TicketServers.Key(server), cancellationToken).ConfigureAwait(false);
        lock (_lock)
        {
            _tokens[key] = token;
        }
        return token;
    }

    private async Task<string?> StoredTokenAsync(string kind, string server, CancellationToken cancellationToken)
    {
        var key = "stored:" + Key(kind, server);
        lock (_lock)
        {
            if (_tokens.TryGetValue(key, out var kept))
            {
                return kept;
            }
        }
        string? token = null;
        if (Credentials.IsAvailable)
        {
            try
            {
                token = await Credentials.ReadAsync(TicketSecrets.Key(kind, server), cancellationToken).ConfigureAwait(false);
            }
            catch (CredentialStoreException ex)
            {
                throw new TicketException(TicketErrorKind.Refused, $"The token for {TicketServers.Key(server)} couldn't be read from {Credentials.Name}: {ex.Message}", inner: ex);
            }
        }
        lock (_lock)
        {
            _tokens[key] = token;
        }
        return token;
    }

    /// <summary>Reads whether a server has its token, for Settings; says it changed only when it wasn't known.</summary>
    public async Task ReadTokenAsync(string kind, string server)
    {
        if (HasToken(kind, server) is not null)
        {
            return;
        }
        try
        {
            await StoredTokenAsync(kind, server, CancellationToken.None).ConfigureAwait(true);
        }
        catch (TicketException)
        {
            // Settings says it has none; using it says why.
            return;
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// A request was refused: a token from another program is asked for again next time, as it may have changed, and
    /// the tracker is made again.
    /// </summary>
    public void TokenRefused(string kind, string server) => Forget(kind, server, keepStored: true);

    private void Forget(string kind, string server, bool keepStored = false)
    {
        lock (_lock)
        {
            foreach (var key in _tokens.Keys.Where(k => k.EndsWith(":" + Key(kind, server), StringComparison.OrdinalIgnoreCase)
                && (!keepStored || !k.StartsWith("stored:", StringComparison.Ordinal))).ToArray())
            {
                _tokens.Remove(key);
            }
            _trackers.Remove(Key(kind, server));
        }
    }

    // ---- Trackers ----------------------------------------------------------------------------------------------------

    /// <summary>The tracker for a source's server, signed in as Settings says; the same one while its token is, so it keeps its <c>ETag</c>s.</summary>
    /// <exception cref="TicketException">Its sign-in couldn't give a token.</exception>
    public async Task<ITicketTracker> TrackerAsync(string kind, string server, CancellationToken cancellationToken = default)
    {
        var token = await TokenAsync(kind, server, cancellationToken).ConfigureAwait(false);
        lock (_lock)
        {
            if (_trackers.TryGetValue(Key(kind, server), out var kept) && kept.Token == token)
            {
                return kept.Tracker;
            }
            var tracker = Type(kind).Connect(Context(server, token));
            _trackers[Key(kind, server)] = (token, tracker);
            return tracker;
        }
    }

    private TicketTrackerContext Context(string server, string? token) => new(_services.Http, _services.Time, server, token, _services.UserAgent);

    // ---- Who it's signed in as ----------------------------------------------------------------------------------------

    /// <summary>Who a server is signed in as, or why not, as last checked.</summary>
    public TicketServerState StateOf(string kind, string server)
    {
        lock (_lock)
        {
            return _states.TryGetValue(Key(kind, server), out var state) ? state : TicketServerState.Unchecked;
        }
    }

    /// <summary>Asks the server who its sign-in is, for Settings and <b>Mine</b>.</summary>
    public async Task<TicketServerState> CheckAsync(string kind, string server)
    {
        TicketServerState state;
        try
        {
            var tracker = await TrackerAsync(kind, server).ConfigureAwait(true);
            state = new TicketServerState(await tracker.WhoAmIAsync().ConfigureAwait(true), null, true);
        }
        catch (TicketException ex)
        {
            if (ex.Kind == TicketErrorKind.Refused)
            {
                TokenRefused(kind, server);
            }
            state = new TicketServerState(null, ex.Message, true);
        }
        lock (_lock)
        {
            _states[Key(kind, server)] = state;
        }
        Changed?.Invoke();
        return state;
    }

    // ---- Sign in with GitHub --------------------------------------------------------------------------------------------

    /// <summary>Sign in with GitHub is offered: Deventry has its app.</summary>
    public bool CanSignInWithGitHub => DeviceFlow() is not null;

    /// <summary>Starts Sign in with GitHub: the code to enter, and the wait for its token, which is then kept as a token is.</summary>
    /// <exception cref="TicketException">GitHub didn't give a code.</exception>
    public async Task<(GitHubDeviceCode Code, Task<TicketAccount?> Done)> SignInWithGitHubAsync(string server, CancellationToken cancellationToken)
    {
        var flow = DeviceFlow() ?? throw new TicketException(TicketErrorKind.Other, "Sign in with GitHub isn't offered by this Deventry.");
        var code = await flow.StartAsync(cancellationToken).ConfigureAwait(true);
        return (code, WaitAsync());

        async Task<TicketAccount?> WaitAsync()
        {
            var token = await flow.WaitForTokenAsync(code, cancellationToken).ConfigureAwait(true);
            var account = await SetTokenAsync(GitHubTicketsType.KindId, server, token).ConfigureAwait(true);
            SetSignIn(GitHubTicketsType.KindId, server, TicketSignIn.Device);
            return account;
        }
    }
}
