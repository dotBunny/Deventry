using Deventry.Core.Remote;

namespace Deventry.Core.Tests.Remote;

/// <summary>A desk with tabs the test sets, which keeps what clients ask of it and answers as the test says.</summary>
internal sealed class FakeRemoteDesk : IRemoteDesk
{
    private readonly Lock _lock = new();

    public string MachineName { get; set; } = "DESK";

    public RemoteAccessLevel Access { get; set; } = RemoteAccessLevel.Full;

    public bool CanOpenTabs { get; set; }

    public RemoteDetail Detail { get; set; } = RemoteDetail.Everything;

    public DateTimeOffset? QuietUntil { get; set; }

    public void SetQuiet(DateTimeOffset? until) => QuietUntil = until;

    public List<RemoteTab> Tabs { get; set; } = [];

    public RemoteReply? LastReply { get; set; }

    public IReadOnlyList<RemoteFileChange> ChangedFiles { get; set; } = [];

    public RemoteUsage Usage { get; set; } = RemoteUsage.None;

    public IReadOnlyList<RemoteFolder> Folders { get; set; } = [];

    /// <summary>What an answer, a message or a stop returns; done by default.</summary>
    public RemoteResult Result { get; set; } = RemoteResult.Ok;

    public List<string> Calls { get; } = [];

    public IReadOnlyList<string> CallsCopy()
    {
        lock (_lock)
        {
            return [.. Calls];
        }
    }

    public event Action<RemoteEvent>? EventRaised;

    public void Raise(RemoteEvent raised) => EventRaised?.Invoke(raised);

    public bool HasListener => EventRaised is not null;

    private Task<RemoteResult> Record(string call)
    {
        lock (_lock)
        {
            Calls.Add(call);
        }
        return Task.FromResult(Result);
    }

    public Task<IReadOnlyList<RemoteTab>> GetTabsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RemoteTab>>([.. Tabs]);

    public Task<RemoteResult> SendAsync(string tabId, string text, string client, CancellationToken cancellationToken = default) => Record($"send {tabId} {text} ({client})");

    public Task<RemoteResult> StopAsync(string tabId, string client, CancellationToken cancellationToken = default) => Record($"stop {tabId}");

    public Task<RemoteResult> AnswerAsync(string tabId, string promptId, string optionId, string? text, string client, CancellationToken cancellationToken = default) =>
        Record(text is null ? $"answer {tabId} {promptId} {optionId}" : $"answer {tabId} {promptId} {optionId}: {text}");

    public Task<RemoteResult> RestartAsync(string tabId, string client, CancellationToken cancellationToken = default) => Record($"restart {tabId}");

    public Task<RemoteReply?> GetLastReplyAsync(string tabId, CancellationToken cancellationToken = default) => Task.FromResult(LastReply);

    public Task<IReadOnlyList<RemoteFileChange>?> GetChangedFilesAsync(string tabId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RemoteFileChange>?>(ChangedFiles);

    public Task<RemoteUsage> GetUsageAsync(CancellationToken cancellationToken = default) => Task.FromResult(Usage);

    public Task<IReadOnlyList<RemoteFolder>> GetFoldersAsync(CancellationToken cancellationToken = default) => Task.FromResult(Folders);

    public async Task<RemoteResult> OpenTabAsync(string folder, string client, CancellationToken cancellationToken = default)
    {
        await Record($"open {folder}");
        return Result.Done ? Result with { TabId = Tabs.FirstOrDefault()?.Id } : Result;
    }

    public static RemoteTab Tab(string id, string name, RemoteTabStatus status = RemoteTabStatus.Idle, RemotePrompt? prompt = null) => new()
    {
        Id = id,
        Name = name,
        Project = "Game",
        Branch = "main",
        Status = status,
        Prompt = prompt,
    };

    public static RemotePrompt Command(string id = "p1", string command = "npm test") => new()
    {
        Id = id,
        Kind = RemotePromptKind.Command,
        Title = "Allow this command?",
        About = command,
        Options =
        [
            new RemoteOption(RemotePrompt.OptionIds.Allow, "Allow", IsDefault: true),
            new RemoteOption(RemotePrompt.OptionIds.Deny, "Deny"),
            new RemoteOption(RemotePrompt.OptionIds.DenyWithMessage, "Deny with a message…", TakesText: true),
        ],
    };
}
