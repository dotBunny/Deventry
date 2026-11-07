using System.Globalization;
using System.Text.Json.Nodes;
using Deventry.App.Conversation;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Settings;

namespace Deventry.App.Tests;

/// <summary>The prompt cache's chip in the composer's bar (DESIGN.md §6, "Prompt cache").</summary>
public class PromptCacheTests
{
    // The harness's clock starts on Monday 28 September 2026 at 12:00 UTC, and its time zone is UTC.

    private static readonly TimeSpan Tick = TabViewModel.PromptCacheTickInterval;

    /// <summary>A turn's init and a main-agent reply with its usage, which refreshes the cache; the result follows.</summary>
    private static void EmitReply(TabTestHarness h, long oneHour = 0, long fiveMinutes = 0, string? apiKeySource = "none")
    {
        h.Transport.Emit(new JsonObject
        {
            ["type"] = "system", ["subtype"] = "init", ["session_id"] = "s1", ["model"] = "claude-opus-5-5", ["permissionMode"] = "default",
            ["apiKeySource"] = apiKeySource,
        });
        h.Transport.Emit(new JsonObject
        {
            ["type"] = "assistant",
            ["parent_tool_use_id"] = null,
            ["message"] = new JsonObject
            {
                ["id"] = $"msg_{Guid.NewGuid():N}",
                ["model"] = "claude-opus-5-5",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "Done." }),
                ["usage"] = Usage(oneHour, fiveMinutes),
            },
        });
    }

    private static void EmitResult(TabTestHarness h, long oneHour = 0, long fiveMinutes = 0) => h.Transport.Emit(new JsonObject
    {
        ["type"] = "result", ["subtype"] = "success", ["is_error"] = false, ["result"] = "Done.", ["session_id"] = "s1",
        ["usage"] = Usage(oneHour, fiveMinutes),
    });

    private static JsonObject Usage(long oneHour, long fiveMinutes) => new()
    {
        ["input_tokens"] = 12, ["output_tokens"] = 40, ["cache_read_input_tokens"] = 9_000, ["cache_creation_input_tokens"] = oneHour + fiveMinutes,
        ["cache_creation"] = new JsonObject { ["ephemeral_5m_input_tokens"] = fiveMinutes, ["ephemeral_1h_input_tokens"] = oneHour },
    };

    private static async Task SendAsync(TabViewModel tab)
    {
        tab.ComposerText = "go";
        await tab.SendCommand.ExecuteAsync(null);
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Working, "the turn");
    }

    private static async Task FinishTurnAsync(TabTestHarness h, TabViewModel tab, long oneHour = 0, long fiveMinutes = 0, string? apiKeySource = "none")
    {
        EmitReply(h, oneHour, fiveMinutes, apiKeySource);
        EmitResult(h, oneHour, fiveMinutes);
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle && tab.IsSettled, "the end of the turn");
    }

    [Fact]
    public async Task After_a_turn_the_chip_counts_down_turns_to_caution_in_the_last_three_minutes_and_expires()
    {
        using var culture = new CultureScope(CultureInfo.InvariantCulture);
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        // No turn yet, so no cache.
        Assert.False(tab.IsPromptCacheShown);

        await SendAsync(tab);
        h.Time.Advance(TimeSpan.FromMinutes(1));
        EmitReply(h, oneHour: 20_000);
        await TabTestHarness.Eventually(() => tab.Items.OfType<AssistantTextItem>().Any(), "the reply");
        // While the turn runs its requests keep the cache fresh, and the working line shows instead.
        Assert.False(tab.IsPromptCacheShown);
        EmitResult(h, oneHour: 20_000);
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle && tab.IsPromptCacheShown, "the chip");

        Assert.Equal("Cache 60m", tab.PromptCacheText);
        Assert.False(tab.IsPromptCacheEnding);
        Assert.False(tab.IsPromptCacheExpired);
        // Claude Code's sign-in is a subscription here: what Claude reads counts toward the limits.
        Assert.Equal(
            "The prompt cache keeps this conversation for a while after each request. A message sent in that time reads it from the cache, which counts for much less toward your usage limits.\n"
            + "1-hour cache, seen in the last turn.\n"
            + "Sent before 13:01, your next message reads this conversation from the cache. After that, Claude reads all of it again, and all of it counts toward your usage limits.",
            tab.PromptCacheTip);

        // It changes at most every 15 seconds.
        h.Time.Advance(Tick - TimeSpan.FromSeconds(1));
        Assert.Equal("Cache 60m", tab.PromptCacheText);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("Cache 59m", tab.PromptCacheText);

        // Three minutes left isn't the last three yet.
        h.Time.Advance(TimeSpan.FromMinutes(57) - Tick);
        Assert.Equal("Cache 3m", tab.PromptCacheText);
        Assert.False(tab.IsPromptCacheEnding);
        h.Time.Advance(Tick);
        Assert.Equal("Cache 2m", tab.PromptCacheText);
        Assert.True(tab.IsPromptCacheEnding);

        h.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal("Cache <1m", tab.PromptCacheText);
        Assert.True(tab.IsPromptCacheEnding);

        h.Time.Advance(TimeSpan.FromSeconds(45));
        Assert.Equal("Cache expired", tab.PromptCacheText);
        Assert.True(tab.IsPromptCacheExpired);
        Assert.False(tab.IsPromptCacheEnding);
        Assert.True(tab.IsPromptCacheShown);
        Assert.EndsWith("It expired at 13:01, so your next message has Claude read all of this conversation again, and all of it counts toward your usage limits.", tab.PromptCacheTip, StringComparison.Ordinal);

        // The next turn hides it while it runs, and starts it again.
        await SendAsync(tab);
        Assert.False(tab.IsPromptCacheShown);
        h.Time.Advance(TimeSpan.FromSeconds(20));
        await FinishTurnAsync(h, tab);
        Assert.True(tab.IsPromptCacheShown);
        Assert.Equal("Cache 60m", tab.PromptCacheText);
        Assert.False(tab.IsPromptCacheExpired);
    }

    [Fact]
    public async Task A_tab_in_the_background_doesnt_tick_and_catches_up_when_selected()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        await SendAsync(tab);
        await FinishTurnAsync(h, tab, oneHour: 5_000);
        Assert.Equal("Cache 60m", tab.PromptCacheText);

        tab.IsSelected = false;
        h.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal("Cache 60m", tab.PromptCacheText);

        tab.IsSelected = true;
        Assert.Equal("Cache 55m", tab.PromptCacheText);
        h.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal("Cache 54m", tab.PromptCacheText);
    }

    [Fact]
    public async Task Without_writes_it_goes_by_how_the_tab_signs_in()
    {
        using var culture = new CultureScope(CultureInfo.InvariantCulture);
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();

        // A session on an API key gets Claude Code's five minutes, at the full price.
        await SendAsync(tab);
        await FinishTurnAsync(h, tab, apiKeySource: "ANTHROPIC_API_KEY");

        Assert.Equal("Cache 5m", tab.PromptCacheText);
        Assert.Equal(
            "The prompt cache keeps this conversation for a while after each request. A message sent in that time reads it from the cache, at a fraction of the price.\n"
            + "5-minute cache, as Claude Code uses with an API key.\n"
            + "Sent before 12:05, your next message reads this conversation from the cache. After that, Claude reads all of it again at the full price.",
            tab.PromptCacheTip);

        // A claude.ai sign-in usually gets an hour.
        await SendAsync(tab);
        await FinishTurnAsync(h, tab, apiKeySource: "none");
        Assert.Equal("Cache 60m", tab.PromptCacheText);
        Assert.Contains("\n1-hour cache, as Claude Code usually gives subscriptions.\n", tab.PromptCacheTip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Claude_Codes_own_configuration_decides_first()
    {
        using var culture = new CultureScope(CultureInfo.InvariantCulture);
        await using var h = new TabTestHarness();
        // Before the tab opens: its start reads Claude Code's configuration in the background, and a read from before the
        // file was written could finish after the ones below. It's the start's own read that's waited for.
        var settings = Path.Combine(h.WorkFolder, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        File.WriteAllText(settings, """{ "promptCacheTtl": "5m" }""");
        var tab = await h.OpenTabAsync();
        await TabTestHarness.Eventually(() => tab.PromptCacheLifetime?.Source == Core.Sessions.PromptCacheTtlSource.Setting, "the start's read of the setting");

        // The setting outranks what the turn wrote.
        await SendAsync(tab);
        await FinishTurnAsync(h, tab, oneHour: 8_000);
        Assert.Equal("Cache 5m", tab.PromptCacheText);
        Assert.Contains("\n5-minute cache, set by promptCacheTtl in Claude Code's settings.\n", tab.PromptCacheTip, StringComparison.Ordinal);
        h.Time.Advance(Tick * 9);
        Assert.Equal("Cache 2m", tab.PromptCacheText);
        Assert.True(tab.IsPromptCacheEnding);

        // A variable of the environment claude starts with outranks the setting.
        h.ClaudeUserEnvironment["CLAUDE_CODE_PROMPT_CACHE_TTL"] = "1h";
        await tab.LoadPromptCacheConfigAsync();
        Assert.Equal("Cache 57m", tab.PromptCacheText);
        Assert.Contains("\n1-hour cache, set by CLAUDE_CODE_PROMPT_CACHE_TTL.\n", tab.PromptCacheTip, StringComparison.Ordinal);

        // With prompt caching off there's nothing to count down.
        h.ClaudeUserEnvironment["DISABLE_PROMPT_CACHING"] = "1";
        await tab.LoadPromptCacheConfigAsync();
        Assert.False(tab.IsPromptCacheShown);
    }

    [Fact]
    public async Task Clearing_the_conversation_forgets_the_cache_and_the_setting_hides_it()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        await SendAsync(tab);
        await FinishTurnAsync(h, tab);
        Assert.True(tab.IsPromptCacheShown);

        h.Services.Settings.Appearance.ShowPromptCacheExpiry = false;
        tab.OnSettingsChanged();
        Assert.False(tab.IsPromptCacheShown);
        h.Services.Settings.Appearance.ShowPromptCacheExpiry = true;
        tab.OnSettingsChanged();
        Assert.True(tab.IsPromptCacheShown);

        h.Transport.Emit("""{"type":"conversation_reset","trigger":"clear"}""");
        await TabTestHarness.Eventually(() => !tab.IsPromptCacheShown, "the cache forgotten");

        // A turn with no request, as a local command such as /cost has, doesn't bring it back.
        await SendAsync(tab);
        EmitResult(h);
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle && tab.IsSettled, "the end of the turn");
        Assert.False(tab.IsPromptCacheShown);
    }

    [Fact]
    public async Task A_compacted_conversation_counts_from_the_first_request_after_it()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        await SendAsync(tab);
        await FinishTurnAsync(h, tab, oneHour: 100);

        // Compacting, as /compact does: the conversation the cache holds isn't the one sent next.
        await SendAsync(tab);
        h.Transport.Emit("""{"type":"system","subtype":"compact_boundary","compact_metadata":{"trigger":"manual","pre_tokens":1000}}""");
        EmitResult(h);
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle && tab.IsSettled, "the end of the turn");

        Assert.False(tab.IsPromptCacheShown);
    }

    [Fact]
    public async Task A_restored_tab_counts_from_the_last_request_in_its_transcript()
    {
        using var culture = new CultureScope(CultureInfo.InvariantCulture);
        await using var h = new TabTestHarness();
        var reply = Wire.Message(new JsonObject { ["type"] = "text", ["text"] = "A missing `using`." });
        reply["usage"] = Usage(oneHour: 30_000, fiveMinutes: 0);
        h.WriteTranscript("s1",
            Wire.Entry("user", "2026-09-28T11:39:00Z", new JsonObject { ["role"] = "user", ["content"] = "Why is the build red?" }),
            Wire.Entry("assistant", "2026-09-28T11:40:00Z", reply));
        h.Services.State.Tabs = [new TabState { Folder = h.WorkFolder, IsPinned = true, SessionId = "s1" }];
        h.Shell.Restore(null);
        h.Start();
        var tab = h.Shell.AllTabs.Single();

        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle && tab.IsPromptCacheShown, "the restored conversation");

        Assert.Equal("Cache 40m", tab.PromptCacheText);
        Assert.Contains("\n1-hour cache, seen in the last turn.\nSent before 12:40,", tab.PromptCacheTip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tab_restored_after_the_cache_expired_says_so()
    {
        using var culture = new CultureScope(CultureInfo.InvariantCulture);
        await using var h = new TabTestHarness();
        h.WriteTranscript("s1",
            Wire.Entry("user", "2026-09-27T09:30:00Z", new JsonObject { ["role"] = "user", ["content"] = "Why is the build red?" }),
            Wire.Entry("assistant", "2026-09-27T09:31:00Z", Wire.Message(new JsonObject { ["type"] = "text", ["text"] = "A missing `using`." })));
        h.Services.State.Tabs = [new TabState { Folder = h.WorkFolder, IsPinned = true, SessionId = "s1" }];
        h.Shell.Restore(null);
        h.Start();
        var tab = h.Shell.AllTabs.Single();

        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle && tab.IsPromptCacheShown, "the restored conversation");

        Assert.Equal("Cache expired", tab.PromptCacheText);
        Assert.True(tab.IsPromptCacheExpired);
        Assert.EndsWith("It expired at Sun 10:31, so your next message has Claude read all of this conversation again, and all of it counts toward your usage limits.",
            tab.PromptCacheTip, StringComparison.Ordinal);
    }
}
