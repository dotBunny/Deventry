using Deventry.Core.Remote;

namespace Deventry.Core.Settings;

/// <summary>
/// Settings → Remote clients (DESIGN.md §18, "Remote clients"), under <c>remoteClients</c>. All of it this computer's,
/// never synced: a bot is polled by one machine, and its token is in this machine's credential store.
/// </summary>
public sealed class RemoteClientSettings
{
    /// <summary>Each kind of client's own settings, by its kind (<c>telegram</c>); a kind with none here is off.</summary>
    public Dictionary<string, RemoteClientTypeSettings> Clients { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The client of <paramref name="kind"/> is on: off unless turned on.</summary>
    public bool IsEnabled(string kind) => Clients.TryGetValue(kind, out var client) && client.Enabled;

    /// <summary>Turns the client of <paramref name="kind"/> on or off.</summary>
    public void SetEnabled(string kind, bool on)
    {
        if (!Clients.TryGetValue(kind, out var client))
        {
            Clients[kind] = client = new RemoteClientTypeSettings();
        }
        client.Enabled = on;
    }

    /// <summary>Any client is on.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool AnyEnabled => Clients.Values.Any(c => c.Enabled);

    /// <summary><b>What they can do</b>.</summary>
    public RemoteAccessLevel Access { get; set; } = RemoteAccessLevel.Full;

    /// <summary><b>Open new tabs</b>: <c>/new</c>, in a favorite or recent folder.</summary>
    public bool OpenTabs { get; set; }

    /// <summary>A tab needs permission or an answer.</summary>
    public bool NeedsInput { get; set; } = true;

    /// <summary>A tab finished its turn.</summary>
    public bool TurnFinished { get; set; } = true;

    /// <summary>A tab stopped with an error.</summary>
    public bool ProcessErrors { get; set; } = true;

    /// <summary>Usage alerts and accounts' spend warnings.</summary>
    public bool UsageAlerts { get; set; } = true;

    /// <summary>A project action finished or failed.</summary>
    public bool ProjectActions { get; set; }

    /// <summary><b>When</b>: while the user is away, or always.</summary>
    public RemoteSendWhen When { get; set; } = RemoteSendWhen.Away;

    /// <summary><b>Count me away after</b>: minutes without the keyboard or mouse, 1 to 60.</summary>
    public int AwayAfterMinutes { get; set; } = RemoteAway.DefaultMinutes;

    /// <summary><b>What they say</b>.</summary>
    public RemoteDetail Detail { get; set; } = RemoteDetail.Everything;

    /// <summary>Settings → General → Keeping the computer awake → <b>Also while a remote client is on</b>.</summary>
    public bool KeepAwake { get; set; } = true;
}

/// <summary>One kind of client's settings: its switch, such as <b>Use a Telegram bot</b>. Its secret is in the credential store, never here.</summary>
public sealed class RemoteClientTypeSettings
{
    public bool Enabled { get; set; }
}

/// <summary>What the remote clients keep in <c>state.json</c>: the paired accounts and the bot's name.</summary>
public sealed class RemoteClientsState
{
    public List<RemoteAccount> Accounts { get; set; } = [];

    /// <summary>Each client's own account's name, by its kind: the Telegram bot's <c>@username</c>, without the @.</summary>
    public Dictionary<string, string> Identities { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The bot said Deventry was closing: it says, silently, that it's back when it starts again.</summary>
    public bool SaidClosing { get; set; }
}
