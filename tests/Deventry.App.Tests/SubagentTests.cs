using System.Text.Json.Nodes;
using Deventry.App.Conversation;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Settings;
using Deventry.Core.Subagents;

namespace Deventry.App.Tests;

/// <summary>
/// Subagents (DESIGN.md §5, "Subagents"): Settings → Appearance → Show subagents' work puts each one's work under its
/// tab in the sidebar, read-only, and Settings → Claude Code → Subagents discourages or refuses them. The lines Claude
/// Code sends are <see cref="Wire"/>'s, as for the agent map.
/// </summary>
public class SubagentTests
{
    // ---- Their work under the tab ---------------------------------------------------------------------------------

    [Fact]
    public async Task Each_subagent_has_a_row_under_its_tab_nested_under_the_one_that_started_it()
    {
        // The default.
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();

        h.Transport.Emit(Wire.Init());
        h.Transport.Emit(Wire.Agent("a1", "Explore the auth code", "Find it", "Explore"));
        h.Transport.Emit(Wire.Agent("a2", "Check the tests", "Run them"));
        h.Transport.Emit(Wire.Agent("a3", "Look deeper", "Read the middleware", "Explore", parent: "a2"));
        // A sibling started after the nested one still comes after it, under the main agent.
        h.Transport.Emit(Wire.Agent("a4", "Write the fix", "Fix it"));

        await TabTestHarness.Eventually(() => tab.SubagentRows.Count == 4, "the rows");
        Assert.Equal(["a1", "a2", "a3", "a4"], tab.SubagentRows.Select(r => r.Node.ToolUseId));
        Assert.Equal([0d, 0d, 12d, 0d], tab.SubagentRows.Select(r => r.Indent));
        Assert.True(tab.HasSubagentRows);
        var explore = tab.SubagentRows[0];
        Assert.Equal("Explore subagent: Explore the auth code\nRunning", explore.Tip);
        Assert.Equal("Explore · Starting…", explore.Node.Subtitle);
        Assert.False(explore.CanDismiss);
        Assert.Equal("Show its work", explore.Node.ShowText);
    }

    [Fact]
    public async Task In_the_conversation_subagents_have_no_rows()
    {
        await using var h = new TabTestHarness(InConversation);
        var tab = await h.OpenTabAsync();

        h.Transport.Emit(Wire.Init());
        h.Transport.Emit(Wire.Agent("a1", "Explore", "Find it", "Explore"));

        await TabTestHarness.Eventually(() => tab.HasAgents, "the subagent");
        Assert.Empty(tab.SubagentRows);
        Assert.False(tab.SubagentsInSidebar);
        Assert.Equal("Show in conversation", tab.Agents.Find("a1")!.ShowText);
        // Its card opens its cards in the conversation, as it always has.
        var card = tab.Items.OfType<SubagentItem>().Single();
        tab.OpenSubagentCommand.Execute(card);
        Assert.True(card.IsExpanded);
        Assert.False(tab.IsViewingAgent);
    }

    [Fact]
    public async Task A_row_shows_its_work_in_the_tab_and_the_tab_row_brings_the_conversation_back()
    {
        await using var h = new TabTestHarness(InSidebar);
        var tab = await h.OpenTabAsync();
        h.Transport.Emit(Wire.Init());
        h.Transport.Emit(Wire.Agent("a1", "Explore the auth code", "Find it", "Explore"));
        h.Transport.Emit(Wire.Tool("a1", "g1", "Grep", new JsonObject { ["pattern"] = "token", ["path"] = "src/" }));
        h.Transport.Emit(Wire.Text("a1", "Tokens are checked in the middleware."));
        await TabTestHarness.Eventually(() => tab.SubagentRows.Count == 1 && tab.Agents.Find("a1")!.Item!.Items.Count == 2, "the subagent's work");
        var row = tab.SubagentRows.Single();

        row.OpenCommand.Execute(null);

        Assert.True(tab.IsViewingAgent);
        Assert.Same(row.Node, tab.ViewedAgent);
        Assert.Same(row.Node.Item!.Items, tab.ShownItems);
        Assert.True(row.IsShowing);
        Assert.Contains("read-only", tab.ViewedAgentNote, StringComparison.Ordinal);
        // Find looks through what's shown: the subagent's own reply, not the conversation's card.
        tab.Find.IsOpen = true;
        tab.Find.Query = "middleware";
        Assert.IsType<AssistantTextItem>(tab.Find.Current);
        Assert.Same(tab.Find.Current, tab.TopLevelItemOf(tab.Find.Current!));

        h.Shell.SelectCommand.Execute(tab);

        Assert.False(tab.IsViewingAgent);
        Assert.Same(tab.Items, tab.ShownItems);
        Assert.False(row.IsShowing);
        Assert.IsType<SubagentItem>(tab.Find.Current);
    }

    [Fact]
    public async Task The_card_and_the_Agents_page_open_its_work()
    {
        await using var h = new TabTestHarness(InSidebar);
        var tab = await h.OpenTabAsync();
        h.Transport.Emit(Wire.Init());
        h.Transport.Emit(Wire.Agent("a1", "Explore", "Find it", "Explore"));
        await TabTestHarness.Eventually(() => tab.HasAgents, "the subagent");
        var node = tab.Agents.Find("a1")!;

        tab.OpenSubagentCommand.Execute(node.Item);
        Assert.Same(node, tab.ViewedAgent);
        Assert.False(node.Item!.IsExpanded);

        tab.ShowConversationCommand.Execute(null);
        Assert.False(tab.IsViewingAgent);

        tab.ShowAgentCommand.Execute(node);
        Assert.Same(node, tab.ViewedAgent);
        Assert.Same(node, tab.SelectedAgent);
    }

    [Fact]
    public async Task A_card_inside_a_subagents_work_shows_there_when_the_tab_scrolls_to_it()
    {
        await using var h = new TabTestHarness(InSidebar);
        var tab = await h.OpenTabAsync();
        h.Transport.Emit(Wire.Init());
        h.Transport.Emit(Wire.Agent("a1", "Explore", "Find it", "Explore"));
        h.Transport.Emit(Wire.Agent("a2", "Deeper", "Go on", "Explore", parent: "a1"));
        h.Transport.Emit(Wire.Tool("a2", "b1", "Bash", new JsonObject { ["command"] = "npm test" }));
        await TabTestHarness.Eventually(() => tab.Agents.Find("a2")?.ToolCalls == 1, "the nested call");
        var call = tab.Agents.Find("a2")!.Item!.Items.ToolCalls().Single();

        // The process monitor's Show in conversation, for a process the nested subagent started.
        tab.ScrollTo(call);

        Assert.Same(tab.Agents.Find("a2"), tab.ViewedAgent);
        tab.ScrollTo(tab.Items.OfType<SubagentItem>().Single());
        Assert.False(tab.IsViewingAgent);
    }

    [Fact]
    public async Task Finished_rows_from_an_earlier_turn_are_marked_and_can_be_dismissed()
    {
        await using var h = new TabTestHarness(InSidebar);
        var tab = await h.OpenTabAsync();
        h.Transport.Emit(Wire.Init());
        h.Transport.Emit(Wire.Agent("a1", "Done one", "p1"));
        h.Transport.Emit(Wire.Agent("a2", "Background one", "p2", background: true));
        h.Transport.Emit(Wire.Result("a1", "All done."));
        h.Transport.Emit(Wire.Result("a2", "Async agent launched successfully.", toolUseResult: new JsonObject { ["status"] = "async_launched", ["agentId"] = "task-b" }));
        h.Transport.Emit(Wire.TurnResult());
        await TabTestHarness.Eventually(() => tab.Agents.Find("a1")?.IsDone == true && tab.Status == TabStatus.Idle, "the end of the turn");
        var done = tab.SubagentRows[0];
        var background = tab.SubagentRows[1];
        Assert.False(done.Node.IsEarlier);
        Assert.True(done.CanDismiss);
        Assert.False(tab.HasEarlierSubagents);

        await SendAsync(tab, "Now the next step");

        // Finished and before the latest message: earlier. Still running in the background: still part of the work.
        Assert.True(done.Node.IsEarlier);
        Assert.Contains("From an earlier turn", done.Tip, StringComparison.Ordinal);
        Assert.False(background.Node.IsEarlier);
        Assert.True(tab.HasEarlierSubagents);
        Assert.True(background.CanDismissEarlier);
        Assert.False(background.CanDismiss);

        background.DismissEarlierCommand.Execute(null);

        Assert.Equal([background], tab.SubagentRows);
        // Dismissing a running one does nothing; once it's finished it goes.
        background.DismissCommand.Execute(null);
        Assert.Single(tab.SubagentRows);
        h.Transport.Emit(Wire.Notification("task-b", "a2", "completed", "Waited."));
        await TabTestHarness.Eventually(() => background.CanDismiss, "the background subagent to finish");
        background.DismissCommand.Execute(null);
        Assert.Empty(tab.SubagentRows);
        // Still on the Agents page and in the conversation.
        Assert.Equal(2, tab.Agents.Subagents.Count);
        Assert.Equal(2, tab.Items.OfType<SubagentItem>().Count());
    }

    [Fact]
    public async Task A_prompt_from_the_subagent_shows_under_its_work_and_is_answered_there()
    {
        await using var h = new TabTestHarness(InSidebar);
        var tab = await h.OpenTabAsync();
        h.Transport.Emit(Wire.Init());
        h.Transport.Emit(Wire.Agent("a1", "Touch a marker file", "Create it"));
        h.Transport.Emit(Wire.TaskStarted("task-a", "a1"));
        h.Transport.Emit(Wire.Tool("a1", "b1", "Bash", new JsonObject { ["command"] = "touch marker" }));
        h.Transport.Emit(Wire.CanUseTool("perm-1", "b1", "touch marker", agentId: "task-a"));
        await TabTestHarness.Eventually(() => tab.Agents.Find("a1")?.IsWaiting == true, "the prompt");
        var node = tab.Agents.Find("a1")!;

        // The Agents page goes to the subagent's work, where the prompt waits under it.
        tab.ShowAgentCommand.Execute(node);
        Assert.Same(node, tab.ViewedAgent);
        var prompt = Assert.IsType<PermissionItem>(node.WaitingPrompt);

        prompt.AllowCommand.Execute(null);

        await TabTestHarness.Eventually(() => h.Transport.Sent.Any(m => m["response"]?["request_id"]?.GetValue<string>() == "perm-1"), "the answer");
        Assert.False(node.IsWaiting);
    }

    [Fact]
    public async Task Collapsing_the_tab_hides_its_subagents_rows_until_one_waits_on_the_user()
    {
        await using var h = new TabTestHarness(InSidebar);
        var tab = await h.OpenTabAsync();
        h.Transport.Emit(Wire.Init());
        h.Transport.Emit(Wire.Agent("a1", "Touch a marker file", "Create it"));
        h.Transport.Emit(Wire.Agent("a2", "Explore the auth code", "Find it", "Explore"));
        h.Transport.Emit(Wire.TaskStarted("task-a", "a1"));
        await TabTestHarness.Eventually(() => tab.SubagentRows.Count == 2, "the rows");
        Assert.True(tab.ShowsSubagentRows);

        tab.ToggleRowsCollapsedCommand.Execute(null);

        // Out of the sidebar, with how many and the most urgent of them on the tab's row (DESIGN.md §4, "Sidebar").
        Assert.False(tab.ShowsSubagentRows);
        Assert.Equal(2, tab.CollapsedRowCount);
        Assert.Equal("Collapsed: 2 subagents", tab.CollapsedTip);
        Assert.True(tab.CollapsedIsBusy);

        h.Transport.Emit(Wire.Tool("a1", "b1", "Bash", new JsonObject { ["command"] = "touch marker" }));
        h.Transport.Emit(Wire.CanUseTool("perm-1", "b1", "touch marker", agentId: "task-a"));
        await TabTestHarness.Eventually(() => tab.Agents.Find("a1")?.IsWaiting == true, "the prompt");

        Assert.False(tab.AreRowsCollapsed);
        Assert.True(tab.ShowsSubagentRows);

        // Collapsed again while it waits, it stays collapsed, and a subagent starting doesn't open it.
        tab.ToggleRowsCollapsedCommand.Execute(null);
        h.Transport.Emit(Wire.Agent("a3", "Write the fix", "Fix it"));
        await TabTestHarness.Eventually(() => tab.SubagentRows.Count == 3, "the third row");
        Assert.True(tab.AreRowsCollapsed);
        Assert.Equal("!", tab.CollapsedGlyph);
        Assert.Equal("Collapsed: 3 subagents\nTouch a marker file · Waiting on your permission", tab.CollapsedTip);
    }

    [Fact]
    public async Task Showing_a_subagents_work_brings_its_row_out_from_under_the_collapsed_tab()
    {
        await using var h = new TabTestHarness(InSidebar);
        var tab = await h.OpenTabAsync();
        h.Transport.Emit(Wire.Init());
        h.Transport.Emit(Wire.Agent("a1", "Explore the auth code", "Find it", "Explore"));
        await TabTestHarness.Eventually(() => tab.SubagentRows.Count == 1, "the row");
        tab.ToggleRowsCollapsedCommand.Execute(null);

        // From the Agents page, or its card in the conversation.
        tab.ShowAgentCommand.Execute(tab.Agents.Find("a1"));

        Assert.True(tab.IsViewingAgent);
        Assert.False(tab.AreRowsCollapsed);
        Assert.True(tab.SubagentRows.Single().IsShowing);
    }

    [Fact]
    public async Task Message_it_through_Claude_names_it_in_the_composer()
    {
        await using var h = new TabTestHarness(InSidebar);
        var tab = await h.OpenTabAsync();
        h.Transport.Emit(Wire.Init());
        h.Transport.Emit(Wire.Agent("a1", "Review the change", "Review it"));
        h.Transport.Emit(Wire.TaskStarted("task-a", "a1"));
        h.Transport.Emit(Wire.Agent("a2", "Explore it", "Find it", "Explore"));
        h.Transport.Emit(Wire.Result("a2", "Found it."));
        await TabTestHarness.Eventually(() => tab.Agents.Find("a1")?.TaskId is not null && tab.Agents.Find("a2")?.IsDone == true, "the subagents");

        // A finished Explore can't be resumed, so there's nothing to message.
        tab.ShowAgentCommand.Execute(tab.Agents.Find("a2"));
        Assert.False(tab.ViewedAgent!.CanBeMessaged);
        Assert.DoesNotContain("pass it a message", tab.ViewedAgentNote, StringComparison.Ordinal);

        tab.ShowAgentCommand.Execute(tab.Agents.Find("a1"));
        Assert.True(tab.ViewedAgent!.CanBeMessaged);
        Assert.Contains("pass it a message", tab.ViewedAgentNote, StringComparison.Ordinal);
        tab.ComposerText = "check the tests too";
        tab.MessageViewedAgentCommand.Execute(null);

        Assert.False(tab.IsViewingAgent);
        Assert.Equal("@\"Review the change\" check the tests too", tab.ComposerText);
    }

    [Fact]
    public async Task Switching_where_their_work_shows_applies_at_once()
    {
        await using var h = new TabTestHarness(InConversation);
        var tab = await h.OpenTabAsync();
        h.Transport.Emit(Wire.Init());
        h.Transport.Emit(Wire.Agent("a1", "Explore", "Find it", "Explore"));
        await TabTestHarness.Eventually(() => tab.HasAgents, "the subagent");
        Assert.Empty(tab.SubagentRows);

        h.Services.Settings.Appearance.ShowSubagents = SubagentDisplay.InSidebar;
        h.Services.SaveSettings();

        Assert.True(tab.SubagentsInSidebar);
        Assert.Single(tab.SubagentRows);
        tab.SubagentRows[0].OpenCommand.Execute(null);
        Assert.True(tab.IsViewingAgent);

        h.Services.Settings.Appearance.ShowSubagents = SubagentDisplay.InConversation;
        h.Services.SaveSettings();

        Assert.False(tab.IsViewingAgent);
        Assert.Empty(tab.SubagentRows);
    }

    [Fact]
    public async Task Clearing_the_conversation_takes_the_rows_and_the_view_away()
    {
        await using var h = new TabTestHarness(InSidebar);
        var tab = await h.OpenTabAsync();
        h.Transport.Emit(Wire.Init());
        h.Transport.Emit(Wire.Agent("a1", "Explore", "Find it", "Explore"));
        await TabTestHarness.Eventually(() => tab.SubagentRows.Count == 1, "the row");
        tab.SubagentRows[0].OpenCommand.Execute(null);

        h.Transport.Emit("""{"type":"conversation_reset","new_conversation_id":"s2","trigger":"clear"}""");

        await TabTestHarness.Eventually(() => !tab.IsViewingAgent && tab.SubagentRows.Count == 0, "the rows to go");
    }

    [Fact]
    public async Task A_restored_tab_brings_back_rows_only_for_its_last_turns_subagents()
    {
        await using var h = new TabTestHarness(InSidebar);
        h.WriteTranscript("s1",
            Wire.Entry("user", "2026-09-28T10:00:00Z", new JsonObject { ["role"] = "user", ["content"] = "First" }),
            Wire.Entry("assistant", "2026-09-28T10:00:01Z", Wire.Message(Wire.AgentBlock("old", "Earlier work", "p", "Explore"))),
            Wire.Entry("user", "2026-09-28T10:00:02Z", Wire.ResultMessage("old", "Found it.")),
            Wire.Entry("user", "2026-09-28T10:01:00Z", new JsonObject { ["role"] = "user", ["content"] = "Second" }),
            Wire.Entry("assistant", "2026-09-28T10:01:01Z", Wire.Message(Wire.AgentBlock("new", "Latest work", "p", "Explore"))),
            Wire.Entry("user", "2026-09-28T10:01:02Z", Wire.ResultMessage("new", "Found that.")));
        h.Services.State.Tabs = [new TabState { Folder = h.WorkFolder, IsPinned = true, SessionId = "s1" }];
        h.Shell.Restore(null);
        h.Start();
        var tab = h.Shell.AllTabs.Single();

        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle && tab.Agents.Subagents.Count == 2, "the restored conversation");

        Assert.Equal(["new"], tab.SubagentRows.Select(r => r.Node.ToolUseId));
        Assert.False(tab.SubagentRows[0].Node.IsEarlier);
        // The earlier one is still on the Agents page, and its card still opens it.
        tab.OpenSubagentCommand.Execute(tab.Agents.Find("old")!.Item);
        Assert.Same(tab.Agents.Find("old"), tab.ViewedAgent);
    }

    // ---- Whether Claude may use them ------------------------------------------------------------------------------

    [Fact]
    public async Task Claude_decides_lets_every_Agent_call_through_and_tells_Claude_nothing()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();

        h.Transport.Emit(AgentHook(h, "hk1"));
        var answer = await HookAnswerAsync(h, "hk1");

        Assert.True(answer["continue"]!.GetValue<bool>());
        Assert.Null(h.Factory.Launches.Single().AppendSystemPrompt);
        await SendAsync(tab, "hello");
        Assert.DoesNotContain(h.Transport.SentUserTexts, t => t.Contains("[Deventry]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Discourage_starts_Claude_with_the_note_and_lets_Agent_calls_through()
    {
        await using var h = new TabTestHarness(s => s.ClaudeCode.Subagents = SubagentUse.Discourage);
        var tab = await h.OpenTabAsync();

        Assert.Equal(SubagentMessages.StartNote(SubagentUse.Discourage), h.Factory.Launches.Single().AppendSystemPrompt);
        Assert.Equal(SubagentUse.Discourage, tab.State.SubagentsTold);
        h.Transport.Emit(AgentHook(h, "hk1"));
        Assert.True((await HookAnswerAsync(h, "hk1"))["continue"]!.GetValue<bool>());
        // Claude started knowing: the next message carries no note.
        await SendAsync(tab, "hello");
        Assert.Null(tab.Items.OfType<UserMessageItem>().Last().SubagentNote);
    }

    [Fact]
    public async Task Dont_allow_refuses_Agent_calls_and_the_card_says_it_never_started()
    {
        await using var h = new TabTestHarness(s =>
        {
            s.ClaudeCode.Subagents = SubagentUse.DontAllow;
            s.Appearance.ShowSubagents = SubagentDisplay.InSidebar;
        });
        var tab = await h.OpenTabAsync();
        Assert.Equal(SubagentMessages.StartNote(SubagentUse.DontAllow), h.Factory.Launches.Single().AppendSystemPrompt);

        h.Transport.Emit(Wire.Init());
        h.Transport.Emit(Wire.Agent("a1", "Explore", "Find it", "Explore"));
        // The hook is answered off the UI thread, so the call's card may come after its answer: wait for it first.
        await TabTestHarness.Eventually(() => tab.Items.OfType<SubagentItem>().Any(), "the call's card");
        h.Transport.Emit(AgentHook(h, "hk1"));
        var answer = await HookAnswerAsync(h, "hk1");
        Assert.Equal("deny", answer["hookSpecificOutput"]!["permissionDecision"]!.GetValue<string>());
        Assert.Equal(SubagentMessages.Refused, answer["hookSpecificOutput"]!["permissionDecisionReason"]!.GetValue<string>());
        // As Claude Code 2.1.284 reports the refusal.
        h.Transport.Emit(Wire.Result("a1", $"PreToolUse:Agent hook error: {SubagentMessages.Refused}", isError: true));

        await TabTestHarness.Eventually(() => tab.Items.OfType<SubagentItem>().Single().IsRefused, "the refusal");
        var card = tab.Items.OfType<SubagentItem>().Single();
        Assert.False(card.IsError);
        Assert.False(card.IsSucceeded);
        Assert.Equal("Not started: subagents are turned off for this tab", card.ResultSummary);
        Assert.False(tab.HasAgents);
        Assert.Empty(tab.SubagentRows);
        tab.OpenSubagentCommand.Execute(card);
        Assert.False(tab.IsViewingAgent);
    }

    [Fact]
    public async Task Turning_them_off_in_a_running_tab_applies_at_once_and_the_next_message_says_so()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        h.Transport.Emit(AgentHook(h, "hk1"));
        Assert.True((await HookAnswerAsync(h, "hk1"))["continue"]!.GetValue<bool>());

        // The tab's own setting, from Tab settings….
        tab.State.Overrides.Subagents = SubagentUse.DontAllow;
        Assert.True(tab.HasOverrides);

        h.Transport.Emit(AgentHook(h, "hk2"));
        Assert.Equal("deny", (await HookAnswerAsync(h, "hk2"))["hookSpecificOutput"]!["permissionDecision"]!.GetValue<string>());
        await SendAsync(tab, "carry on");
        var card = tab.Items.OfType<UserMessageItem>().Last();
        Assert.Equal(SubagentMessages.ChangeNote(SubagentUse.DontAllow), card.SubagentNote);
        Assert.Equal("Told Claude it can't use subagents", card.SubagentNoteText);
        Assert.Contains(h.Transport.SentUserTexts, t => t.Contains("carry on", StringComparison.Ordinal) && t.Contains(SubagentMessages.ChangeNote(SubagentUse.DontAllow), StringComparison.Ordinal));
        // Said once.
        await SendAsync(tab, "and again");
        Assert.Null(tab.Items.OfType<UserMessageItem>().Last().SubagentNote);
        // Allowed again: Claude hears that too.
        tab.State.Overrides.Subagents = null;
        await SendAsync(tab, "one more");
        Assert.Equal("Told Claude it can use subagents again", tab.Items.OfType<UserMessageItem>().Last().SubagentNoteText);
        Assert.Equal(SubagentUse.ClaudeDecides, tab.State.SubagentsTold);
    }

    [Fact]
    public async Task A_message_taken_back_with_the_note_leaves_it_for_the_next_one()
    {
        // Held until the turn ends, as Settings → General can say (DESIGN.md §5, "Queued messages").
        await using var h = new TabTestHarness(s => s.General.MessagesWhileWorking = WhileWorking.Queue);
        var tab = await h.OpenTabAsync();
        await SendAsync(tab, "long job");
        h.Transport.Emit(Wire.Init());
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Working, "working");
        tab.State.Overrides.Subagents = SubagentUse.Discourage;
        await SendAsync(tab, "while it works");
        var held = tab.Items.OfType<UserMessageItem>().Last();
        Assert.True(held.IsHeld);
        Assert.Equal(SubagentUse.Discourage, tab.State.SubagentsTold);

        await tab.CancelQueuedMessageCommand.ExecuteAsync(held);

        Assert.Equal(SubagentUse.ClaudeDecides, tab.State.SubagentsTold);
        Assert.Equal("while it works", tab.ComposerText);
    }

    private static void InSidebar(AppSettings settings) => settings.Appearance.ShowSubagents = SubagentDisplay.InSidebar;

    private static void InConversation(AppSettings settings) => settings.Appearance.ShowSubagents = SubagentDisplay.InConversation;

    private static async Task SendAsync(TabViewModel tab, string text)
    {
        tab.ComposerText = text;
        await tab.SendCommand.ExecuteAsync(null);
    }

    /// <summary>The subagents' hook called for an Agent call, by the id the tab registered it with at <c>initialize</c>.</summary>
    private static JsonObject AgentHook(TabTestHarness h, string requestId)
    {
        var hooks = h.Transport.Sent.First(m => m["request"]?["subtype"]?.GetValue<string>() == "initialize")["request"]!["hooks"]!["PreToolUse"]!.AsArray();
        var callback = hooks.OfType<JsonObject>().Single(m => m["matcher"]?.GetValue<string>() == SubagentMessages.ToolMatcher)["hookCallbackIds"]![0]!.GetValue<string>();
        return new JsonObject
        {
            ["type"] = "control_request",
            ["request_id"] = requestId,
            ["request"] = new JsonObject
            {
                ["subtype"] = "hook_callback",
                ["callback_id"] = callback,
                ["input"] = new JsonObject
                {
                    ["session_id"] = "s1", ["hook_event_name"] = "PreToolUse", ["tool_name"] = "Agent",
                    ["tool_input"] = new JsonObject { ["description"] = "Explore", ["prompt"] = "Find it", ["subagent_type"] = "Explore" },
                    ["tool_use_id"] = $"toolu_{requestId}",
                },
                ["tool_use_id"] = $"toolu_{requestId}",
            },
        };
    }

    private static async Task<JsonObject> HookAnswerAsync(TabTestHarness h, string requestId)
    {
        JsonObject? Answer() => h.Transport.Sent.FirstOrDefault(m => m["type"]?.GetValue<string>() == "control_response"
            && m["response"]?["request_id"]?.GetValue<string>() == requestId)?["response"]?["response"]?.AsObject();
        await TabTestHarness.Eventually(() => Answer() is not null, "the hook's answer");
        return Answer()!;
    }
}
