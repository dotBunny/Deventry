namespace Deventry.Core.Remote.Telegram;

/// <summary>
/// Telegram, as a kind of remote client (DESIGN.md §18, "Remote clients", "Telegram"): a bot the user makes with
/// @BotFather, its token kept in the credential store, and a phone paired by opening the bot with <c>/start</c> and a
/// code.
/// </summary>
public sealed class TelegramClientType : IRemoteClientType
{
    public string Kind => TelegramClient.ClientKind;

    public string Name => TelegramClient.ClientName;

    public string Description => "A Telegram bot of your own, for your tabs from your phone.";

    public string SwitchTitle => "Use a Telegram bot";

    public string SwitchDescription =>
        "Deventry talks to the bot itself, so reading tabs and answering prompts cost no tokens. Only a message you send a tab does, as typing it here would.";

    public string? SwitchMore => "Not the Claude app's Remote Control, which is Claude Code's own link to one tab at a time.";

    public string Privacy =>
        "Telegram can read bot chats: they aren't end-to-end encrypted. It sees what the bot sends: your tabs' and projects' names, Claude's replies, and the commands and files Claude asks about. All clients → What they say can keep the messages to the tab's name and what happened.";

    public IReadOnlyList<string> Keywords { get; } = ["telegram", "bot", "phone", "remote", "pair", "mobile"];

    public string CredentialKey => "telegram-bot-token";

    public string CredentialLabel => "Deventry: Telegram bot";

    public string SecretName => "token";

    public string SecretPlaceholder => "123456789:AAH…";

    public string SetupSteps => "Make one with @BotFather in Telegram: send it /newbot, pick a name, and paste the token it gives you here.";

    public string? CheckSecret(string secret) => TelegramBotApi.LooksLikeToken(secret)
        ? null
        : "That isn't a bot's token. @BotFather gives it as numbers, a colon, then letters and digits.";

    public string AccountName => "bot";

    public string ShowIdentity(string identity) => $"@{identity}";

    public string? PairingLink(string? identity, string code) => identity is { Length: > 0 } bot ? $"https://t.me/{bot}?start={code}" : null;

    public string PairingCommand(string code) => $"/start {code}";

    public string PairingSteps(bool hasLink) => hasLink
        ? "Scan the code with your phone's camera to open the bot in Telegram, then tap Start. Or send the bot this:"
        : "Send the bot this, in Telegram:";

    public IRemoteClient Create(RemoteClientContext context) =>
        new TelegramClient(new TelegramBotApi(context.Http, context.Time, context.Secret, context.UserAgent), context.Pairing, context.Time, context.Logger)
        {
            AnnounceBack = context.AnnounceBack,
        };
}
