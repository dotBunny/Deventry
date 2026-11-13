namespace Deventry.Core.Remote.Chat;

/// <summary>Someone in a chat service: a user, or a bot.</summary>
/// <param name="Id">Their id in the service, which doesn't change.</param>
/// <param name="Name">Their name, as the service shows it.</param>
/// <param name="Username">Their username, without an @; null when they have none.</param>
public sealed record ChatUser(long Id, bool IsBot, string Name, string? Username);

/// <summary>A message a chat bot received, as much of it as Deventry reads.</summary>
/// <param name="Chat">The chat it's in, which the bot answers in.</param>
/// <param name="MessageId">Its id in the chat; 0 for a command a service delivers without a message, as Discord's slash commands.</param>
/// <param name="ReplyTo">The bot's message it replies to, with the service's reply.</param>
/// <param name="HasMedia">It carries a photo, a file, a voice message or the like, which the bot doesn't take yet.</param>
/// <param name="IsPrivate">It's in a chat between the bot and one account: only these count.</param>
public sealed record ChatMessage(long Chat, long MessageId, ChatUser? From, DateTimeOffset Date, string? Text, long? ReplyTo, bool HasMedia, bool IsPrivate);

/// <summary>A tap on one of the bot's buttons.</summary>
/// <param name="Id">The service's id for the tap, to answer it.</param>
/// <param name="Chat">The chat of the message the button is on; null when the service no longer has it.</param>
/// <param name="MessageId">The message the button is on; null when the service no longer has it.</param>
/// <param name="Data">What the button carries: its run and key (<see cref="ChatFormat.ButtonData"/>).</param>
/// <param name="Token">What else answering it takes: a Discord interaction's token.</param>
public sealed record ChatTap(string Id, ChatUser From, long? Chat, long? MessageId, string? Data, string? Token = null);

/// <summary>A button under a message, whose data comes back when it's tapped.</summary>
public sealed record ChatButton(string Text, string Data);

/// <summary>A command in the service's <b>/</b> menu.</summary>
/// <param name="Command">Its name, without the /: <c>tabs</c>.</param>
/// <param name="Argument">The name of what it takes after it, for a service that asks for it by name (Discord's options): <c>tab</c>.</param>
/// <param name="ArgumentDescription">What that is.</param>
public sealed record ChatCommand(string Command, string Description, string? Argument = null, string? ArgumentDescription = null);

/// <summary>
/// A chat service refused a request, or couldn't be reached. The message is safe to show and log: it never holds a
/// secret.
/// </summary>
public abstract class ChatApiException(string message, Exception? inner) : Exception(message, inner)
{
    /// <summary>The service asked the bot to slow down.</summary>
    public abstract bool IsTooManyRequests { get; }

    /// <summary>How long it asked to wait, with <see cref="IsTooManyRequests"/>.</summary>
    public abstract TimeSpan? RetryAfter { get; }

    /// <summary>An edit of a message that was deleted, or can't be edited any more: there's nothing more to say there.</summary>
    public abstract bool IsMessageGone { get; }
}
