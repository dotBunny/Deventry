using System.Collections.ObjectModel;
using Deventry.App.Services;
using Deventry.Core.Credentials;
using Deventry.Core.Remote;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QRCoder;

namespace Deventry.App.ViewModels.Settings;

/// <summary>An account paired with a remote client, with <b>Unpair…</b>.</summary>
public sealed class PairedAccountRow(RemoteAccount account, string pairedAt, Action unpair)
{
    public long UserId => account.UserId;

    /// <summary>"Matt (@reapazor)".</summary>
    public string Name => account.Display;

    /// <summary>"Paired today at 14:05".</summary>
    public string Detail => $"Paired {pairedAt}";

    public string UnpairName => $"Unpair {account.Display}";

    public InlineConfirmation Unpairing { get; } = new(() =>
    {
        unpair();
        return Task.CompletedTask;
    });
}

/// <summary>
/// One kind of remote client's page in Settings → Remote clients (DESIGN.md §18, "Remote clients"), drawn from what the
/// kind declares (<see cref="IRemoteClientType"/>), so a kind needs no page or view of its own: its switch, what its
/// service can read, its own account and secret, and the accounts paired with it with pairing an account. Telegram's is
/// the first. What every client shares is on <see cref="AllClientsPage"/>. All of it this computer's: a client is run by
/// one machine, and its secret is in this machine's credential store.
/// </summary>
public sealed partial class RemoteClientPage : SettingsPage, IDisposable
{
    private readonly UiTicker _pairingTicker;

    public RemoteClientPage(SettingsContext context, IRemoteClientType type) : base(context, type.Name)
    {
        Type = type;
        var kind = type.Kind;
        Section(kind, $"The {type.AccountName}",
        [
            Setting(EnabledKey, type.SwitchTitle, s => s.RemoteClients.IsEnabled(kind), (s, v) => s.RemoteClients.SetEnabled(kind, v),
                type.SwitchDescription, type.SwitchMore, type.Keywords),
            Info($"state.remoteClients.{kind}.account", Core.Formats.Capitalize(type.AccountName), type.SetupSteps, keywords: [type.SecretName, type.AccountName]),
            Info($"state.remoteClients.{kind}.paired", "Paired with", "Only these accounts are answered; anyone else gets no answer at all.",
                keywords: ["pair", "phone", "qr", "unpair", "account"]),
        ]);
        _pairingTicker = new UiTicker(Services.Time, Services.Dispatcher, TimeSpan.FromSeconds(1), OnPairingTick);
        Remote.Changed += OnRemoteChanged;
        RefreshAccounts();
        _ = Remote.CheckSecretAsync(kind);
    }

    public IRemoteClientType Type { get; }

    public string Kind => Type.Kind;

    /// <summary>Its switch's key in <c>settings.json</c>: <c>remoteClients.clients.telegram.enabled</c>.</summary>
    public string EnabledKey => $"remoteClients.clients.{Kind}.enabled";

    /// <summary>The info rows' keys, for the view.</summary>
    public string AccountKey => $"state.remoteClients.{Kind}.account";

    public string PairedKey => $"state.remoteClients.{Kind}.paired";

    private RemoteClientsService Remote => Services.RemoteClients;

    private RemotePairing Pairing => Remote.PairingOf(Kind);

    public override string IconKey => RemoteClientIcons.For(Kind);

    public override string Description => Type.Description;

    /// <summary><i>Off</i> beside its name in the sidebar while it isn't used; the page can still be picked.</summary>
    public override string? NavTag => IsOn ? null : "Off";

    public void Dispose()
    {
        Remote.Changed -= OnRemoteChanged;
        _pairingTicker.Dispose();
    }

    private void OnRemoteChanged()
    {
        RefreshAccounts();
        OnPropertyChanged(nameof(HasSecret));
        OnPropertyChanged(nameof(IdentityText));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(IsTrouble));
        OnPropertyChanged(nameof(Fix));
        OnPropertyChanged(nameof(HasFix));
        OnPropertyChanged(nameof(CanPair));
        OnPropertyChanged(nameof(SetSecretText));
        if (IsPairing && Pairing.Current is null)
        {
            // Paired, or five wrong codes ended it.
            StopPairing();
        }
    }

    // ---- Its account ------------------------------------------------------------------------------------------------

    /// <summary>The kind's switch: <b>Use a Telegram bot</b>.</summary>
    public bool IsOn
    {
        get => Settings.RemoteClients.IsEnabled(Kind);
        set
        {
            Set(value, v => Settings.RemoteClients.SetEnabled(Kind, v));
            OnPropertyChanged(nameof(NavTag));
            OnPropertyChanged(nameof(StateText));
        }
    }

    public string SwitchTitle => Type.SwitchTitle;

    public string Privacy => Type.Privacy;

    public bool HasSecret => Remote.HasSecret(Kind) == true;

    /// <summary>"@deventry_bot", once its service has said; else what's known.</summary>
    public string IdentityText => Remote.IdentityOf(Kind) is { } name ? Type.ShowIdentity(name)
        : HasSecret ? $"A {Type.AccountName} {Type.Name} hasn't named yet"
        : $"No {Type.AccountName} yet";

    /// <summary>Connected, Connecting…, or why not.</summary>
    public string StateText
    {
        get
        {
            var state = Remote.StateOf(Kind);
            return state.Status switch
            {
                RemoteClientStatus.Connected => "Connected",
                RemoteClientStatus.Connecting => state.Reason is { } why ? $"Connecting… {why}" : "Connecting…",
                RemoteClientStatus.Failed => state.Reason ?? "Stopped.",
                _ => "Off",
            };
        }
    }

    /// <summary>The state is a problem the user should look at: in the caution colors.</summary>
    public bool IsTrouble => Remote.StateOf(Kind) is { Status: RemoteClientStatus.Failed } or { Status: RemoteClientStatus.Connecting, Reason: not null };

    /// <summary>What can be done about why it can't connect, such as Telegram's <b>Use it for Deventry anyway</b>.</summary>
    public RemoteClientFix? Fix => Remote.StateOf(Kind).Fix;

    public bool HasFix => Fix is not null;

    [RelayCommand]
    private Task ApplyFixAsync() => Remote.ApplyFixAsync(Kind);

    /// <summary><b>Set token…</b>, or <b>Change token…</b> once there is one.</summary>
    public string SetSecretText => HasSecret ? $"Change {Type.SecretName}…" : $"Set {Type.SecretName}…";

    public string SaveSecretText => $"Save {Type.SecretName}";

    public string SecretPlaceholder => Type.SecretPlaceholder;

    public string SecretName => $"The {Type.AccountName}'s {Type.SecretName}";

    public string RemoveSecretText =>
        $"Remove the {Type.AccountName}'s {Type.SecretName} from this computer? The {Type.AccountName} stops answering here; it, and its chats, stay in {Type.Name}.";

    [ObservableProperty]
    public partial bool IsSettingSecret { get; set; }

    [ObservableProperty]
    public partial string NewSecret { get; set; } = "";

    [ObservableProperty]
    public partial string? SecretError { get; private set; }

    public bool IsCredentialStoreAvailable => Remote.Credentials.IsAvailable;

    public string? CredentialStoreUnavailableText => Remote.Credentials.IsAvailable ? null
        : $"Deventry keeps the {Type.AccountName}'s {Type.SecretName} only in {Remote.Credentials.Name}, which isn't available here: {Remote.Credentials.UnavailableReason}";

    [RelayCommand]
    private void StartSettingSecret()
    {
        SecretError = null;
        IsSettingSecret = true;
    }

    [RelayCommand]
    private void CancelSecret()
    {
        NewSecret = "";
        SecretError = null;
        IsSettingSecret = false;
    }

    [RelayCommand]
    private async Task SaveSecretAsync()
    {
        try
        {
            await Remote.SetSecretAsync(Kind, NewSecret);
            NewSecret = "";
            IsSettingSecret = false;
            SecretError = null;
        }
        catch (ArgumentException ex)
        {
            SecretError = ex.Message.Split(" (Parameter")[0];
        }
        catch (CredentialStoreException ex)
        {
            SecretError = $"Couldn't keep the {Type.SecretName} in {Remote.Credentials.Name}: {ex.Message}";
        }
        OnRemoteChanged();
    }

    /// <summary><b>Remove…</b>: the secret leaves the credential store, and the client stops.</summary>
    public InlineConfirmation SecretRemoval => field ??= new InlineConfirmation(async () =>
    {
        try
        {
            await Remote.RemoveSecretAsync(Kind);
        }
        catch (CredentialStoreException ex)
        {
            SecretError = $"Couldn't remove the {Type.SecretName} from {Remote.Credentials.Name}: {ex.Message}";
        }
        OnRemoteChanged();
    });

    // ---- Pairing ----------------------------------------------------------------------------------------------------

    public ObservableCollection<PairedAccountRow> Accounts { get; } = [];

    public bool HasAccounts => Accounts.Count > 0;

    private void RefreshAccounts()
    {
        var accounts = Pairing.Accounts;
        if (accounts.Select(a => a.UserId).SequenceEqual(Accounts.Select(a => a.UserId)))
        {
            return;
        }
        Accounts.Clear();
        foreach (var account in accounts)
        {
            var when = Core.Formats.When(account.PairedAt, Services.Time);
            Accounts.Add(new PairedAccountRow(account, when.StartsWith("at ", StringComparison.Ordinal) ? $"today {when}" : when, () => Pairing.Unpair(account.UserId)));
        }
        OnPropertyChanged(nameof(HasAccounts));
    }

    /// <summary>An account can be paired: the client has its secret. Its account's name makes the QR code; without it, the code is sent by hand.</summary>
    public bool CanPair => HasSecret && IsCredentialStoreAvailable;

    public string CanPairTip => $"Set the {Type.SecretName} first";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotPairing))]
    public partial bool IsPairing { get; private set; }

    public bool IsNotPairing => !IsPairing;

    /// <summary>The code as it's shown: <c>7KQ2-M9XD</c>.</summary>
    [ObservableProperty]
    public partial string PairingCode { get; private set; } = "";

    /// <summary>What to send by hand: <c>/start 7KQ2M9XD</c>.</summary>
    [ObservableProperty]
    public partial string PairingCommand { get; private set; } = "";

    /// <summary>What to do with them, as the kind says it.</summary>
    [ObservableProperty]
    public partial string PairingSteps { get; private set; } = "";

    /// <summary>The QR code of the kind's link, such as <c>https://t.me/&lt;bot&gt;?start=&lt;code&gt;</c>, as a PNG; null without one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPairingQr))]
    public partial byte[]? PairingQrPng { get; private set; }

    public bool HasPairingQr => PairingQrPng is not null;

    /// <summary>The kind's link that the QR code holds, to open on this computer instead; null without one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPairingLink))]
    public partial string? PairingLink { get; private set; }

    public bool HasPairingLink => PairingLink is not null;

    /// <summary><b>Open the link</b>: the QR code's link, in the browser or the service's app on this computer.</summary>
    [RelayCommand]
    private Task OpenPairingLinkAsync() => PairingLink is { } link ? Services.Platform.OpenUrlAsync(link) : Task.CompletedTask;

    /// <summary>"Works once, for 4:32 more."</summary>
    [ObservableProperty]
    public partial string PairingTimeLeft { get; private set; } = "";

    /// <summary><b>Pair an account…</b>: a new code, its QR code, and the time it has left.</summary>
    [RelayCommand]
    private void StartPairing()
    {
        var code = Pairing.Start();
        var link = Type.PairingLink(Remote.IdentityOf(Kind), code.Code);
        PairingCode = code.Display;
        PairingCommand = Type.PairingCommand(code.Code);
        PairingQrPng = link is null ? null : Qr(link);
        PairingLink = link;
        PairingSteps = Type.PairingSteps(link is not null);
        IsPairing = true;
        OnPairingTick();
        _pairingTicker.Run(true);
    }

    [RelayCommand]
    private void CancelPairing()
    {
        Pairing.Cancel();
        StopPairing();
    }

    private void StopPairing()
    {
        _pairingTicker.Run(false);
        IsPairing = false;
        PairingQrPng = null;
        PairingLink = null;
        PairingCode = PairingCommand = PairingSteps = PairingTimeLeft = "";
    }

    private void OnPairingTick()
    {
        if (Pairing.Current is not { } code)
        {
            StopPairing();
            return;
        }
        var left = code.ExpiresAt - Services.Time.GetUtcNow();
        PairingTimeLeft = $"Works once, for {(int)left.TotalMinutes}:{left.Seconds:00} more.";
    }

    /// <summary>A QR code as a PNG: dark on light whatever the theme, which every phone's camera reads.</summary>
    internal static byte[] Qr(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(8);
    }

    protected override void OnReset()
    {
        OnPropertyChanged(string.Empty);
    }
}
