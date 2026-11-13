using Deventry.Core.Remote.Chat;

namespace Deventry.Core.Remote.Telegram;

/// <summary>Telegram's HTML (<see cref="TelegramFormat"/>), as the chat bot writes its messages.</summary>
internal sealed class TelegramHtml : ChatFormat
{
    public static TelegramHtml Instance { get; } = new();

    public override int MessageLimit => TelegramBotApi.MessageLimit;

    public override string Escape(string text) => TelegramFormat.Escape(text);

    public override string Bold(string text) => TelegramFormat.Bold(text);

    public override string Italic(string text) => TelegramFormat.Italic(text);

    public override string Code(string text) => TelegramFormat.Code(text);

    public override string CodeBlock(string text, string? language = null) => TelegramFormat.CodeBlock(text, language);

    public override string Quote(string text) => $"<blockquote>{TelegramFormat.Escape(text)}</blockquote>";

    public override string FromMarkdown(string markdown) => TelegramFormat.FromMarkdown(markdown);
}
