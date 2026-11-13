namespace Deventry.Core.Remote;

/// <summary>What a prompt asks about (DESIGN.md §18, "Remote clients").</summary>
public enum RemotePromptKind
{
    /// <summary>A command to run.</summary>
    Command,

    /// <summary>A file to create or edit.</summary>
    FileChange,

    /// <summary>Any other tool.</summary>
    Tool,

    /// <summary>A question for the user, with options to pick or their own words.</summary>
    Question,

    /// <summary>A plan to approve, or to keep planning.</summary>
    Plan,

    /// <summary>A thread's message to one of its sub-threads, to send or not (DESIGN.md §18, "Threads").</summary>
    ThreadMessage,

    /// <summary>A card only the desk can answer: an MCP server's form, folder trust, a sign-in.</summary>
    AtDesk,
}

/// <summary>One answer to a prompt: one of its card's buttons.</summary>
/// <param name="Id">What the desk knows it by, within its prompt.</param>
/// <param name="Label">The card's own label, such as "Allow" or "Approve, accept edits".</param>
/// <param name="IsDefault">The card's safe "yes", which Ctrl+Enter picks at the desk.</param>
/// <param name="TakesText">It takes the user's words: <b>Deny with a message…</b>, a question's own answer, <b>Keep planning…</b>.</param>
/// <param name="NeedsText">Without words it can't be answered (a question's own words); the others go without them.</param>
public sealed record RemoteOption(string Id, string Label, bool IsDefault = false, bool TakesText = false, bool NeedsText = false);

/// <summary>
/// A prompt waiting in a tab, the same for every agent (DESIGN.md §18, "Remote clients"): Claude Code's permission,
/// question and plan cards and a thread's approvals map to it, and the agents to come map theirs.
/// </summary>
public sealed record RemotePrompt
{
    /// <summary>The ids an answer gives for each kind's options.</summary>
    public static class OptionIds
    {
        public const string Allow = "allow";
        public const string Deny = "deny";
        public const string DenyWithMessage = "deny-message";
        public const string ApproveAuto = "approve-auto";
        public const string ApproveAcceptEdits = "approve-edits";
        public const string ApproveAskBeforeEdits = "approve-ask";
        public const string KeepPlanning = "keep-planning";
        public const string Send = "send";
        public const string DontSend = "dont-send";
        public const string OwnWords = "own-words";

        /// <summary>A question's option, by where it is in its list.</summary>
        public static string Choice(int index) => $"choice-{index}";

        /// <summary>The place in its question's list of a <see cref="Choice"/>; null for any other id.</summary>
        public static int? ChoiceIndex(string id) =>
            id.StartsWith("choice-", StringComparison.Ordinal) && int.TryParse(id.AsSpan("choice-".Length), out var index) && index >= 0 ? index : null;
    }

    /// <summary>The prompt's id within its tab. A question with several parts gives each part its own.</summary>
    public required string Id { get; init; }

    public required RemotePromptKind Kind { get; init; }

    /// <summary>What's waiting, in the card's words: "Allow this command?", "Claude has a question".</summary>
    public required string Title { get; init; }

    /// <summary>What it's about: the command, the file, the question. Null when the title says it all.</summary>
    public string? About { get; init; }

    /// <summary>The card's answers, in its order. None for a card only the desk can answer.</summary>
    public IReadOnlyList<RemoteOption> Options { get; init; } = [];

    /// <summary>A plan's text, as Markdown.</summary>
    public string? Plan { get; init; }

    /// <summary>The subagent asking, when it isn't the main agent.</summary>
    public string? Asker { get; init; }

    /// <summary>For a thread's message: the sub-thread it would go to.</summary>
    public string? Recipient { get; init; }

    /// <summary>For a question in parts: which part this is, and how many there are; 0 for one alone.</summary>
    public int Part { get; init; }

    public int Parts { get; init; }

    /// <summary>Only the desk can answer it: <i>"Answer this at the desk."</i></summary>
    public bool AtDeskOnly => Options.Count == 0;

    public RemoteOption? Option(string id) => Options.FirstOrDefault(o => o.Id == id);
}
