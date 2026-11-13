using Deventry.Core.Remote;

namespace Deventry.App.Tests.Support;

/// <summary>A kind of remote client besides Telegram, such as a Discord bot would be, for what more than one does.</summary>
internal sealed class FakeRemoteClientType(string kind, string name) : IRemoteClientType
{
    public string Kind => kind;

    public string Name => name;

    public string Description => $"A {name} bot.";

    public string SwitchTitle => $"Use a {name} bot";

    public string SwitchDescription => "It talks to the bot itself.";

    public string? SwitchMore => null;

    public string Privacy => $"{name} can read its chats.";

    public IReadOnlyList<string> Keywords => [kind];

    public string CredentialKey => $"{kind}-bot-token";

    public string CredentialLabel => $"Deventry: {name} bot";

    public string SecretName => "token";

    public string SecretPlaceholder => "token";

    public string SetupSteps => "Make one.";

    public string? CheckSecret(string secret) => null;

    public string AccountName => "bot";

    public string ShowIdentity(string identity) => identity;

    public string? PairingLink(string? identity, string code) => null;

    public string PairingCommand(string code) => $"!pair {code}";

    public string PairingSteps(bool hasLink) => "Send the bot this:";

    public IRemoteClient Create(RemoteClientContext context) => throw new InvalidOperationException("Tests give the service their own clients.");
}
