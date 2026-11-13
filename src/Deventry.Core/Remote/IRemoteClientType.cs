using Microsoft.Extensions.Logging;

namespace Deventry.Core.Remote;

/// <summary>What a client is made with: its secret, the accounts paired with it, and Deventry's own services.</summary>
/// <param name="Http">For its requests: <c>AppServices.Http</c>, which tests replace.</param>
/// <param name="Secret">Its secret from the credential store, such as a bot's token.</param>
/// <param name="AnnounceBack">It said Deventry was closing last time: it says, silently, that it's back.</param>
public sealed record RemoteClientContext(HttpClient Http, TimeProvider Time, string Secret, string UserAgent, RemotePairing Pairing, ILogger Logger, bool AnnounceBack);

/// <summary>
/// A kind of remote client (DESIGN.md §18, "Remote clients"), as a project type is a kind of project: what Settings
/// says about it and how a phone pairs with it, and how to make one. Deventry lists the kinds it knows, and Settings →
/// Remote clients has a page for each, after <b>All clients</b>, drawn from what the kind declares; the service starts
/// one of each that's turned on. Telegram's is <c>TelegramClientType</c>.
/// </summary>
public interface IRemoteClientType
{
    /// <summary>A stable id, such as <c>telegram</c>: its settings' key, its accounts' client, and its secret's name.</summary>
    string Kind { get; }

    /// <summary>"Telegram": its page in Settings, and what cards answered from it say.</summary>
    string Name { get; }

    /// <summary>One line under its page's title.</summary>
    string Description { get; }

    /// <summary>Its page's switch: "Use a Telegram bot".</summary>
    string SwitchTitle { get; }

    /// <summary>The line under the switch.</summary>
    string SwitchDescription { get; }

    /// <summary>What a client stands for, in a sentence after the switch's line: it isn't the Claude app, say.</summary>
    string? SwitchMore { get; }

    /// <summary>What it shares with its service, which the page says in the caution colors.</summary>
    string Privacy { get; }

    /// <summary>Other words search finds its page by.</summary>
    IReadOnlyList<string> Keywords { get; }

    // ---- Its secret ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Where its secret is in the credential store, before the data folder's id that Deventry puts after it:
    /// <c>telegram-bot-token</c>.
    /// </summary>
    string CredentialKey { get; }

    /// <summary>What the OS shows for it in the credential store.</summary>
    string CredentialLabel { get; }

    /// <summary>What its secret is called, mid-sentence: "token", for <b>Set token…</b>.</summary>
    string SecretName { get; }

    /// <summary>The secret's box's placeholder: <c>123456789:AAH…</c>.</summary>
    string SecretPlaceholder { get; }

    /// <summary>How to get one, under the account's row: <i>"Make one with @BotFather…"</i>.</summary>
    string SetupSteps { get; }

    /// <summary>Why <paramref name="secret"/> can't be one, in words to show; null when it can.</summary>
    string? CheckSecret(string secret);

    // ---- Its account and pairing --------------------------------------------------------------------------------

    /// <summary>What its own account is, mid-sentence: "bot", for <b>The bot</b>.</summary>
    string AccountName { get; }

    /// <summary>What its own account is called in Settings, once its service has said: "@deventry_bot".</summary>
    string ShowIdentity(string identity);

    /// <summary>What a phone opens to pair, as a QR code: the bot's link with the code; null without its name.</summary>
    string? PairingLink(string? identity, string code);

    /// <summary>What to send by hand to pair: <c>/start 7KQ2M9XD</c>.</summary>
    string PairingCommand(string code);

    /// <summary>What to do with the QR code or the command, as Settings says it while pairing.</summary>
    string PairingSteps(bool hasLink);

    /// <summary>Makes a client of this kind. It starts when Deventry starts it.</summary>
    IRemoteClient Create(RemoteClientContext context);
}
