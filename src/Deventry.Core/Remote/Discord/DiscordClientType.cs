namespace Deventry.Core.Remote.Discord;

/// <summary>
/// Discord, as a kind of remote client (DESIGN.md §18, "Remote clients", "Discord"): an app the user makes in Discord's
/// Developer Portal and adds to their own account, in no server, its bot's token kept in the credential store, and
/// paired by sending it <c>/pair</c> and a code from any chat.
/// </summary>
public sealed class DiscordClientType : IRemoteClientType
{
    public string Kind => DiscordClient.ClientKind;

    public string Name => DiscordClient.ClientName;

    public string Description => "A Discord bot of your own, on your account and in no server, for your tabs from your phone.";

    public string SwitchTitle => "Use a Discord bot";

    public string SwitchDescription =>
        "Deventry talks to the bot itself, so reading tabs and answering prompts cost no tokens. Only a message you send a tab does, as typing it here would.";

    public string? SwitchMore => "Not the Claude app's Remote Control, which is Claude Code's own link to one tab at a time.";

    public string Privacy =>
        "Discord can read direct messages: they aren't end-to-end encrypted. It sees what the bot sends: your tabs' and projects' names, Claude's replies, and the commands and files Claude asks about. All clients → What they say can keep the messages to the tab's name and what happened.";

    public IReadOnlyList<string> Keywords { get; } = ["discord", "bot", "app", "phone", "remote", "pair", "mobile"];

    public string CredentialKey => "discord-bot-token";

    public string CredentialLabel => "Deventry: Discord bot";

    public string SecretName => "token";

    public string SecretPlaceholder => "MTIzNDU2Nzg5…";

    public string SetupSteps =>
        "Make an application in Discord's Developer Portal. Under Installation, tick User Install and untick Guild Install; under Bot, choose Reset Token and paste the token here.";

    public string? CheckSecret(string secret) => DiscordApi.LooksLikeToken(secret)
        ? null
        : "That isn't a bot's token. The Developer Portal gives it under Bot, as three parts with dots between them.";

    public string AccountName => "bot";

    public string ShowIdentity(string identity) => DiscordClient.ParseIdentity(identity).Name;

    /// <summary>The link that adds the app to the account that opens it, without the code, which <c>/pair</c> sends.</summary>
    public string? PairingLink(string? identity, string code) => identity is not null && DiscordClient.ParseIdentity(identity).ApplicationId is { } id
        ? $"https://discord.com/oauth2/authorize?client_id={id}&integration_type=1&scope=applications.commands"
        : null;

    public string PairingCommand(string code) => $"/pair {code}";

    public string PairingSteps(bool hasLink) => hasLink
        ? "Scan the code with your phone's camera, or open its link, to add the bot to your Discord account: choose Add to My Apps. Then send it this in any chat, and write to it once in its direct messages, as its answer asks:"
        : "Once the bot is on your Discord account, send it this in any chat, and write to it once in its direct messages, as its answer asks:";

    public IRemoteClient Create(RemoteClientContext context) =>
        new DiscordClient(new DiscordApi(context.Http, context.Time, context.Secret, context.UserAgent), () => new DiscordWebSocket(context.UserAgent), context.Secret,
            context.Pairing, context.Time, context.Logger)
        {
            AnnounceBack = context.AnnounceBack,
        };
}
