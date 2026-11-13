using Deventry.Core.Tickets;

namespace Deventry.Core.Settings;

/// <summary>What <b>Work on it</b> opens by default (DESIGN.md §18, "Tickets").</summary>
public enum TicketWorkIn
{
    /// <summary>A new tab in the project's folder.</summary>
    Tab,

    /// <summary>A new tab in a worktree named for the ticket.</summary>
    Worktree,
}

/// <summary>
/// Settings → Tickets (DESIGN.md §18, "Tickets"), under <c>tickets</c>. All of it this computer's, never synced: a
/// server's secret is in this machine's credential store, and its servers are its own.
/// </summary>
public sealed class TicketSettings
{
    /// <summary><b>Work on a ticket in</b>.</summary>
    public TicketWorkIn WorkIn { get; set; } = TicketWorkIn.Tab;

    /// <summary><b>Name new tabs after their ticket</b>.</summary>
    public bool NameTabs { get; set; } = true;

    /// <summary><b>Include the ticket's comments</b>.</summary>
    public bool IncludeComments { get; set; } = true;

    /// <summary>Each provider's own settings, by its kind (<c>github</c>).</summary>
    public Dictionary<string, TicketProviderSettings> Providers { get; set; } = new(StringComparer.Ordinal);

    /// <summary>A provider's settings as they are; null while it has none, which reads as its defaults.</summary>
    public TicketProviderSettings? Find(string kind) => Providers.GetValueOrDefault(kind);

    /// <summary>A provider's settings, made when it has none yet.</summary>
    public TicketProviderSettings For(string kind)
    {
        if (!Providers.TryGetValue(kind, out var provider))
        {
            Providers[kind] = provider = new TicketProviderSettings();
        }
        return provider;
    }
}

/// <summary>One provider's settings: its servers, and its switches. Its secrets are in the credential store, never here.</summary>
public sealed class TicketProviderSettings
{
    /// <summary>
    /// Its servers: one it has without being added (github.com) is here once its sign-in is chosen; the rest are added.
    /// </summary>
    public List<TicketServerSettings> Servers { get; set; } = [];

    /// <summary><b>Find a project's repository from its git remote</b>, for a provider that does.</summary>
    public bool FindFromGit { get; set; } = true;

    /// <summary><b>Let Deventry change tickets</b>.</summary>
    public bool AllowChanges { get; set; }

    /// <summary>The settings of <paramref name="server"/>, however it's written; null when it has none.</summary>
    public TicketServerSettings? Server(string server) => Servers.FirstOrDefault(s => TicketServers.Same(s.Server, server));
}

/// <summary>One server of a provider's, and how Deventry signs in to it.</summary>
public sealed class TicketServerSettings
{
    /// <summary>As <see cref="TicketServers.Normalize"/> writes it.</summary>
    public string Server { get; set; } = "";

    /// <summary><b>Sign in with</b>.</summary>
    public TicketSignIn SignIn { get; set; } = TicketSignIn.None;
}
