namespace Deventry.Core.Remote;

/// <summary>What remote clients may do (Settings → Remote clients → <b>What they can do</b>), one level for every client.</summary>
public enum RemoteAccessLevel
{
    /// <summary>The tabs, the replies, changed files and usage, and messages when things happen.</summary>
    Watch,

    /// <summary>And answer prompts: allow or deny, answer a question, approve a plan or keep planning.</summary>
    Answer,

    /// <summary>And send a tab a message, stop it, and start a stopped tab again. The default.</summary>
    Full,
}

/// <summary>Something a client asks the desk to do that changes a tab.</summary>
public enum RemoteAction
{
    Answer,
    Send,
    Stop,
    Restart,
    OpenTab,
}

/// <summary>
/// Whether remote clients may do something, as Settings → Remote clients says (DESIGN.md §18, "What remote clients can
/// do"). What's only at the desk, at any level, isn't offered at all: saving an <b>Always allow</b> rule, changing a
/// tab's mode, model, account or settings, rewinding, closing a tab, and an MCP server's form.
/// </summary>
public static class RemoteAccess
{
    public static bool Allows(RemoteAccessLevel level, bool openTabs, RemoteAction action) => action switch
    {
        RemoteAction.Answer => level >= RemoteAccessLevel.Answer,
        RemoteAction.Send or RemoteAction.Stop or RemoteAction.Restart => level >= RemoteAccessLevel.Full,
        RemoteAction.OpenTab => openTabs,
        _ => false,
    };

    /// <summary>Why a client may not, in words to show it.</summary>
    public static string Refusal(RemoteAction action) => action switch
    {
        RemoteAction.OpenTab => "Opening tabs from here is turned off. Settings → Remote clients → Open new tabs turns it on.",
        RemoteAction.Answer => "Remote clients can only watch here. Settings → Remote clients lets them answer prompts.",
        _ => "Remote clients can't send messages or stop Claude here. Settings → Remote clients lets them.",
    };

    /// <summary>What each level lets a client do, for Settings.</summary>
    public static string Describe(RemoteAccessLevel level) => level switch
    {
        RemoteAccessLevel.Watch => "Watch",
        RemoteAccessLevel.Answer => "Watch and answer prompts",
        _ => "Watch, answer, send messages and stop Claude",
    };
}
