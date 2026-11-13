using Deventry.Core.Remote.Discord;
using Deventry.Core.Remote.Telegram;

namespace Deventry.App.ViewModels;

/// <summary>Each kind of remote client's icon in Settings' sidebar (DESIGN.md §18, "Remote clients"): Deventry's own drawings.</summary>
public static class RemoteClientIcons
{
    /// <summary>All clients', and a kind's without an icon of its own: a phone.</summary>
    public const string Any = "IconSettingsRemoteClients";

    public static string For(string kind) => kind switch
    {
        TelegramClient.ClientKind => "IconSettingsTelegram",
        DiscordClient.ClientKind => "IconSettingsDiscord",
        _ => Any,
    };
}
