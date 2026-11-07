using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Deventry.App.Controls;
using Deventry.App.Conversation;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.Views;

namespace Deventry.App.UiTests;

/// <summary>The conversation as cards, rendered (DESIGN.md §18, "Conversation cards").</summary>
public sealed class ConversationCardsUiTests
{
    [Fact]
    public void Tiles_go_three_across_then_two_then_one_as_the_card_narrows()
    {
        var panel = new TilesPanel();

        Assert.Equal(3, panel.ColumnsFor(820));
        Assert.Equal(2, panel.ColumnsFor(600));
        Assert.Equal(1, panel.ColumnsFor(300));
        Assert.Equal(1, panel.ColumnsFor(double.PositiveInfinity));
    }

    [AvaloniaFact]
    public async Task A_run_of_calls_is_one_card_of_tiles_and_a_tile_opens_its_detail_and_its_window()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        h.Transport.Emit("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"b1","name":"Bash","input":{"command":"ls docs"}},{"type":"tool_use","id":"b2","name":"Bash","input":{"command":"git status"}}]}}""");
        h.Transport.Emit("""{"type":"user","tool_use_result":{"stdout":"README.md","stderr":""},"message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"b1","content":"README.md"}]}}""");
        await UiText.SettleUntilAsync(window, () => Tiles(window).Count == 2, "the tiles");

        var card = window.GetVisualDescendants().OfType<Card>().Single(c => c.Classes.Contains("tools"));
        Assert.Equal("Tool calls", card.Label);
        Assert.Equal("2 calls · 1 running", card.Detail);
        var (first, second) = (Tiles(window)[0], Tiles(window)[1]);
        // Side by side, on one row of the card.
        var (left, right) = (first.TranslatePoint(default, card)!.Value, second.TranslatePoint(default, card)!.Value);
        Assert.Equal(left.Y, right.Y);
        Assert.True(right.X >= left.X + first.Bounds.Width, $"The second tile starts at {right.X}, inside the first.");
        Assert.Equal("Bash ls docs", AutomationProperties.GetName(first));
        // Its tip has the whole command, and is wider than a tip of words, so a long command wraps less.
        ToolTip.SetIsOpen(first, true);
        UiText.Settle(window);
        var tip = Assert.IsType<ToolTip>(ToolTip.GetTip(first));
        Assert.Equal(("ls docs", 760d), (tip.Content as string, tip.MaxWidth));
        ToolTip.SetIsOpen(first, false);
        var detail = card.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("tiledetail"));
        Assert.False(detail.IsVisible);

        first.Command!.Execute(first.CommandParameter);
        UiText.Settle(window);

        Assert.True(detail.IsEffectivelyVisible);
        Assert.Contains("open", first.Classes);
        Assert.Contains("$ ls docs", UiText.Describe(detail), StringComparison.Ordinal);
        Assert.Contains("README.md", UiText.Describe(detail), StringComparison.Ordinal);

        // Its window follows the call; opening it again brings the same window back.
        var open = detail.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Open in a window");
        open.Command!.Execute(open.CommandParameter);
        UiText.Settle(window);
        var toolWindow = Assert.IsType<ToolCallWindow>(Assert.Single(window.OwnedWindows));
        Assert.Equal("Bash · ls docs", toolWindow.Title);
        open.Command!.Execute(open.CommandParameter);
        UiText.Settle(window);
        Assert.Single(window.OwnedWindows);
        toolWindow.Close();
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_reply_Claude_carried_on_after_is_quieter_until_its_pointed_at_and_the_setting_turns_that_off()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        h.Transport.Emit("""{"type":"assistant","message":{"content":[{"type":"text","text":"Looking first."},{"type":"tool_use","id":"r1","name":"Read","input":{"file_path":"a.cs"}}]}}""");
        h.Transport.Emit("""{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"r1","content":"x"}]}}""");
        h.Transport.EmitTurn("All done.");
        await UiText.SettleUntilAsync(window, () => Replies(window).Count == 2, "the replies");
        var (step, answer) = (Replies(window)[0], Replies(window)[1]);

        Assert.Contains("quiet", step.Classes);
        Assert.DoesNotContain("quiet", answer.Classes);
        Assert.Equal(Brushes.Transparent, Part(step).Background);
        Assert.Same(answer.TintBrush, Part(answer).Background);

        window.MouseMove(step.TranslatePoint(new Point(40, step.Bounds.Height / 2), window)!.Value);
        UiText.Settle(window);
        Assert.Same(step.TintBrush, Part(step).Background);

        window.MouseMove(new Point(5, 5));
        h.Services.Settings.Appearance.QuietSteps = false;
        h.Services.SaveSettings();
        UiText.Settle(window);
        Assert.Same(step.TintBrush, Part(step).Background);
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_chevron_folds_a_Tool_calls_card_to_its_header_and_the_setting_starts_new_ones_folded()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        h.Transport.Emit("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"r1","name":"Read","input":{"file_path":"a.cs"}}]}}""");
        h.Transport.Emit("""{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"r1","content":"x"}]}}""");
        h.Transport.Emit("""{"type":"assistant","message":{"content":[{"type":"text","text":"Between."}]}}""");
        h.Transport.Emit("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"r2","name":"Read","input":{"file_path":"b.cs"}}]}}""");
        await UiText.SettleUntilAsync(window, () => Named(window, "tools").Count == 2 && VisibleTiles(window) == 2, "two Tool calls cards");

        // Open by default; the chevron of the first folds it to its header line, and brings it back.
        var first = Named(window, "tools")[0];
        Assert.False(first.IsCollapsed);
        Toggle(first).Command!.Execute(null);
        UiText.Settle(window);
        Assert.True(first.IsCollapsed);
        Assert.Equal(1, VisibleTiles(window));
        Assert.True(first.IsEffectivelyVisible);
        Toggle(first).Command!.Execute(null);
        UiText.Settle(window);
        Assert.Equal(2, VisibleTiles(window));

        // The whole header line is the chevron's, as a thinking row's is: it lights from end to end under the pointer, and a
        // click on its far end, past the count, folds the card and brings it back.
        var line = first.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_HeaderLine");
        var end = line.TranslatePoint(new Point(line.Bounds.Width - 4, line.Bounds.Height / 2), window)!.Value;
        window.MouseMove(end);
        UiText.Settle(window);
        Assert.Contains(":headerover", first.Classes);
        Assert.False(line.Background is ISolidColorBrush { Color.A: 0 }, "The header line should be lit");
        window.MouseDown(end, MouseButton.Left);
        window.MouseUp(end, MouseButton.Left);
        UiText.Settle(window);
        Assert.True(first.IsCollapsed);
        window.MouseDown(end, MouseButton.Left);
        window.MouseUp(end, MouseButton.Left);
        UiText.Settle(window);
        Assert.False(first.IsCollapsed);
        window.MouseMove(new Point(5, 5));
        UiText.Settle(window);
        Assert.DoesNotContain(":headerover", first.Classes);

        // The setting starts the cards to come collapsed, and leaves the ones there as they are, as thinking's does.
        h.Services.Settings.Appearance.CollapseToolCalls = true;
        h.Services.SaveSettings();
        UiText.Settle(window);
        Assert.All(Named(window, "tools"), c => Assert.False(c.IsCollapsed));
        h.Transport.Emit("""{"type":"assistant","message":{"content":[{"type":"text","text":"More."}]}}""");
        h.Transport.Emit("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"r3","name":"Read","input":{"file_path":"c.cs"}}]}}""");
        await UiText.SettleUntilAsync(window, () => Named(window, "tools").Count == 3, "a third card");
        Assert.True(Named(window, "tools")[2].IsCollapsed);
        Assert.Equal(2, VisibleTiles(window));
        window.Close();
    }

    /// <summary>
    /// The Modified files card folds to its header line as a Tool calls card does (issue #98): the chevron after its count,
    /// or a click anywhere on the header line; the setting starts new ones folded.
    /// </summary>
    [AvaloniaFact]
    public async Task The_Modified_files_card_folds_to_its_header_line_and_the_setting_starts_new_ones_folded()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        TurnSummaryItem Footer()
        {
            var turn = new TurnSummaryItem("3s");
            turn.Files = [new TurnFileRow(turn, new Core.Diffs.TurnFileChange("src/a.cs", null, IsNew: true, BeforeKnown: true), "src/a.cs")];
            return turn;
        }
        var first = Footer();
        tab.Items.Add(first);
        await UiText.SettleUntilAsync(window, () => Named(window, "files").Count == 1 && VisibleTiles(window) == 1, "the Modified files card");

        var card = Named(window, "files")[0];
        Assert.True(card.IsCollapsible);
        var toggle = Toggle(card);
        Assert.Equal("Show or hide the files", AutomationProperties.GetName(toggle));
        // The chevron comes after the count, as a Tool calls card's does.
        var detail = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Detail");
        Assert.Equal("1 file changed", detail.Text);
        Assert.True(toggle.TranslatePoint(default, card)!.Value.X > detail.TranslatePoint(default, card)!.Value.X);

        toggle.Command!.Execute(null);
        UiText.Settle(window);
        Assert.True(first.IsFilesCollapsed);
        Assert.Equal(0, VisibleTiles(window));
        Assert.True(card.IsEffectivelyVisible);

        // A click on the header line's far end brings the tiles back.
        var line = card.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_HeaderLine");
        var end = line.TranslatePoint(new Point(line.Bounds.Width - 4, line.Bounds.Height / 2), window)!.Value;
        window.MouseDown(end, MouseButton.Left);
        window.MouseUp(end, MouseButton.Left);
        UiText.Settle(window);
        Assert.False(first.IsFilesCollapsed);
        Assert.Equal(1, VisibleTiles(window));

        // The setting starts the cards to come collapsed, and leaves the ones there as they are.
        h.Services.Settings.Appearance.CollapseModifiedFiles = true;
        h.Services.SaveSettings();
        tab.ComposerText = "go";
        await tab.SendCommand.ExecuteAsync(null);
        h.Transport.EmitTurn("Done.");
        await UiText.SettleUntilAsync(window, () => tab.Items.OfType<TurnSummaryItem>().Count() == 2, "a second footer");
        Assert.False(first.IsFilesCollapsed);
        Assert.True(tab.Items.OfType<TurnSummaryItem>().Last().IsFilesCollapsed);
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_chain_of_steps_folds_into_one_line_and_then_into_the_replys_header_which_opens_it_inside_the_card()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        h.Services.Settings.Appearance.FoldChains = true;
        h.Services.SaveSettings();
        h.Transport.Emit("""{"type":"assistant","message":{"content":[{"type":"thinking","thinking":"One."},{"type":"tool_use","id":"r1","name":"Read","input":{"file_path":"a.cs"}}]}}""");
        h.Transport.Emit("""{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"r1","content":"x"}]}}""");
        h.Transport.Emit("""{"type":"assistant","message":{"content":[{"type":"thinking","thinking":"Two."},{"type":"tool_use","id":"r2","name":"Bash","input":{"command":"ls","description":"list the files"}}]}}""");
        h.Transport.Emit("""{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"r2","content":"y"}]}}""");
        // The list builds only the steps it needs to, and a folded one has no height, so the model is what to wait for.
        await UiText.SettleUntilAsync(window, () => Named(window, "steps").Count == 1 && Named(window, "tools").Count >= 1
            && FoldedSteps(tab) == 3, "the chain's line");

        // Four steps (two thinking, two calls), the last still what Claude is on: one line, and the last step under it.
        var marker = tab.Items.OfType<StepsMarkerItem>().Single();
        Assert.Equal("Working · 3 earlier steps", marker.Title);
        Assert.Equal(1, Named(window, "tools").Count(c => c.IsEffectivelyVisible));
        Assert.True(Named(window, "steps")[0].IsCollapsed);

        // A reply follows: the run is done, and the whole of it goes into the reply's header, how many steps it was; the
        // line of its own takes no room.
        h.Transport.EmitTurn("Done.");
        await UiText.SettleUntilAsync(window, () => Replies(window).Count == 1 && FoldedSteps(tab) == 4 && marker.IsFolded, "the whole run in the reply");
        var reply = tab.Items.OfType<AssistantTextItem>().Single();
        Assert.Same(marker, reply.Steps);
        Assert.Equal("4 steps", marker.Title);
        Assert.Equal("2 tool calls · 2 thinking", marker.Summary);
        Assert.Equal(0, Named(window, "steps").Count(c => c.IsEffectivelyVisible));
        Assert.Equal(0, Named(window, "tools").Count(c => c.IsEffectivelyVisible));
        Assert.Equal(0, Named(window, "thinking").Count(c => c.IsEffectivelyVisible));
        Assert.Empty(marker.Rows);
        var card = Replies(window)[0];
        var pill = card.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("stepspill"));
        Assert.True(pill.IsEffectivelyVisible);
        Assert.Equal("4 steps", AutomationProperties.GetName(pill));
        var box = card.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ReplySteps");
        Assert.False(box.IsEffectivelyVisible);

        // The pill opens a list of the steps inside the reply's card, a line each: what Claude thought and what it did.
        pill.Command!.Execute(null);
        UiText.Settle(window);
        Assert.True(marker.IsOpen);
        Assert.True(box.IsEffectivelyVisible);
        Assert.True(box.TranslatePoint(default, card)!.Value.Y > card.GetVisualDescendants().OfType<LiveMarkdown.Avalonia.MarkdownRenderer>().Single().TranslatePoint(default, card)!.Value.Y,
            "The steps open under the reply's text.");
        Assert.Equal(["Thought", "Read a.cs", "Thought", "Ran list the files"], marker.Rows.Select(r => r.Title));
        Assert.Equal(4, FoldedSteps(tab));

        // A line opens further: the thinking's words, and the call's own detail.
        var thought = marker.Rows[0];
        Assert.False(thought.ShowsThought);
        thought.ToggleCommand.Execute(null);
        Assert.True(thought.ShowsThought);
        Assert.Equal("One.", thought.ThoughtText);
        var call = marker.Rows[1];
        call.ToggleCommand.Execute(null);
        Assert.True(call.ShowsTool);

        // Folded again, the list goes and costs nothing.
        marker.ToggleCommand.Execute(null);
        UiText.Settle(window);
        Assert.False(marker.IsOpen);
        Assert.Empty(marker.Rows);

        // Going to a step inside opens the list in the reply, and opens its row.
        var target = tab.Items.OfType<ThinkingItem>().Last();
        tab.ScrollTo(target);
        Assert.True(marker.IsOpen);
        Assert.True(marker.Rows.Single(r => ReferenceEquals(r.Source, target)).IsExpanded);

        // Back to showing every step in this tab, from the message box's bar: no line, nothing folded, and Settings as they were.
        var stepsButton = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "StepsButton" && b.IsEffectivelyVisible);
        Assert.Equal("Steps: folded", tab.StepsButtonText);
        stepsButton.Command!.Execute(null);
        UiText.Settle(window);
        Assert.True(h.Services.Settings.Appearance.FoldChains);
        Assert.False(tab.State.Overrides.FoldChains);
        Assert.Equal("Steps: all", tab.StepsButtonText);
        Assert.Empty(Named(window, "steps"));
        Assert.DoesNotContain(tab.Items, i => i is StepsMarkerItem);
        Assert.Null(reply.Steps);
        Assert.Equal(0, FoldedSteps(tab));
        window.Close();
    }

    /// <summary>
    /// Opening a reply's steps at the foot of a long conversation keeps the reply where it was clicked: the steps grow
    /// down, and the list stops following new output rather than jumping to the end (DESIGN.md §18).
    /// </summary>
    [AvaloniaFact]
    public async Task Opening_a_replys_steps_keeps_it_where_it_was_clicked()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        h.Services.Settings.Appearance.FoldChains = true;
        h.Services.SaveSettings();
        for (var i = 0; i < 6; i++)
        {
            h.Transport.EmitTurn(string.Join("\n\n", Enumerable.Repeat($"Earlier reply {i}.", 4)));
        }
        for (var i = 0; i < 12; i++)
        {
            h.Transport.Emit("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"sN","name":"Read","input":{"file_path":"fN.cs"}}]}}""".Replace("N", $"{i}", StringComparison.Ordinal));
            h.Transport.Emit("""{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"sN","content":"x"}]}}""".Replace("N", $"{i}", StringComparison.Ordinal));
        }
        h.Transport.EmitTurn("Done.");
        await UiText.SettleUntilAsync(window, () => tab.Items.OfType<AssistantTextItem>().LastOrDefault()?.Steps is not null
            && window.GetVisualDescendants().OfType<Button>().Any(b => b.Classes.Contains("stepspill") && b.IsEffectivelyVisible), "the last reply's steps");
        var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "ConversationScroll");
        scroll.Offset = new Vector(0, scroll.Extent.Height);
        UiText.Settle(window);
        var pill = window.GetVisualDescendants().OfType<Button>().Last(b => b.Classes.Contains("stepspill") && b.IsEffectivelyVisible);
        var before = pill.TranslatePoint(default, scroll)!.Value.Y;

        Click(window, pill);
        UiText.Settle(window);

        Assert.True(tab.Items.OfType<AssistantTextItem>().Last().Steps!.IsOpen);
        Assert.Equal(before, pill.TranslatePoint(default, scroll)!.Value.Y, 1);
        window.Close();
    }

    private static void Click(Window window, Control target)
    {
        var center = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseMove(center);
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        UiText.Settle(window);
    }

    [AvaloniaFact]
    public async Task A_lone_thinking_row_before_a_reply_folds_into_the_replys_header()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        h.Services.Settings.Appearance.FoldChains = true;
        h.Services.SaveSettings();
        h.Transport.Emit("""{"type":"assistant","message":{"content":[{"type":"thinking","thinking":"Hm."}]}}""");
        h.Transport.EmitTurn("Hi.");
        await UiText.SettleUntilAsync(window, () => Replies(window).Count == 1 && tab.Items.OfType<StepsMarkerItem>().Count() == 1, "the folded thinking");

        var marker = tab.Items.OfType<StepsMarkerItem>().Single();
        Assert.True(tab.Items.OfType<ThinkingItem>().Single().IsFolded);
        Assert.Equal("1 step", marker.Title);
        Assert.Same(marker, tab.Items.OfType<AssistantTextItem>().Single().Steps);
        Assert.Equal(0, Named(window, "thinking").Count(c => c.IsEffectivelyVisible));
        marker.ToggleCommand.Execute(null);
        UiText.Settle(window);
        Assert.Equal("Thought", marker.Rows.Single().Title);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Your_messages_and_Claudes_steps_each_start_further_in_with_their_own_setting()
    {
        // Both off by default; your messages' turned on here.
        await using var h = new TabTestHarness(s => s.Appearance.IndentYourMessages = true, dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        tab.ComposerText = "Hello";
        await tab.SendCommand.ExecuteAsync(null);
        h.Transport.Emit("""{"type":"assistant","message":{"content":[{"type":"thinking","thinking":"Hmm."},{"type":"tool_use","id":"r1","name":"Read","input":{"file_path":"a.cs"}}]}}""");
        h.Transport.Emit("""{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"r1","content":"x"}]}}""");
        h.Transport.EmitTurn("Hi.");
        await UiText.SettleUntilAsync(window, () => YourCards(window).Count == 1 && Replies(window).Count == 1
            && Named(window, "thinking").Count == 1 && Named(window, "tools").Count == 1, "the cards");
        var (you, reply) = (YourCards(window)[0], Replies(window)[0]);
        var (thinking, tools) = (Named(window, "thinking")[0], Named(window, "tools")[0]);

        // Your messages alone: Claude's steps stay at its replies' edge.
        Assert.Equal(new Thickness(96, 0, 0, 0), you.Margin);
        Assert.Equal(new Thickness(0), reply.Margin);
        Assert.Equal(96, you.Bounds.Left - reply.Bounds.Left);
        Assert.Equal(reply.Bounds.Left, thinking.Bounds.Left);
        Assert.Equal(reply.Bounds.Left, tools.Bounds.Left);
        Assert.Equal(you.Bounds.Right, reply.Bounds.Right);

        // Claude's steps alone: a little in, ending at the same right edge; your messages back at the left.
        h.Services.Settings.Appearance.IndentYourMessages = false;
        h.Services.Settings.Appearance.IndentClaudesSteps = true;
        h.Services.SaveSettings();
        UiText.Settle(window);
        Assert.Equal(reply.Bounds.Left, you.Bounds.Left);
        Assert.Equal(24, thinking.Bounds.Left - reply.Bounds.Left);
        Assert.Equal(24, tools.Bounds.Left - reply.Bounds.Left);
        Assert.Equal(thinking.Bounds.Right, reply.Bounds.Right);
        Assert.Equal(tools.Bounds.Right, reply.Bounds.Right);

        // Both off: every card at the same edge.
        h.Services.Settings.Appearance.IndentClaudesSteps = false;
        h.Services.SaveSettings();
        UiText.Settle(window);
        Assert.Equal(new Thickness(0), you.Margin);
        Assert.Equal(reply.Bounds.Left, you.Bounds.Left);
        Assert.Equal(reply.Bounds.Left, thinking.Bounds.Left);
        Assert.Equal(reply.Bounds.Left, tools.Bounds.Left);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Marks_in_the_scroll_bar_go_to_your_messages_and_F3_and_F4_step_between_messages()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        var first = new UserMessageItem("The first question");
        tab.Items.Add(first);
        for (var i = 0; i < 150; i++)
        {
            tab.Items.Add(new NoteItem($"Note {i}", NoteKind.Info));
        }
        var second = new UserMessageItem("The second question");
        tab.Items.Add(second);
        for (var i = 0; i < 150; i++)
        {
            tab.Items.Add(new NoteItem($"Later note {i}", NoteKind.Info));
        }
        UiText.Settle(window);
        var view = window.GetVisualDescendants().OfType<TabView>().Single();
        var list = view.GetVisualDescendants().OfType<ItemsControl>().Single(c => c.Name == "ConversationItems");
        var marks = view.GetVisualDescendants().OfType<ScrollMarks>().Single(m => m.Name == "ScrollMarks");
        await UiText.SettleUntilAsync(window, () => marks.Marks.Count == 2, "a mark for each message");
        Assert.Equal([0, 151], marks.Marks.Select(m => m.Index));
        Assert.Equal("Your message: The first question", marks.Marks[0].Tip);
        Assert.True(marks.Marks[0].Fraction < marks.Marks[1].Fraction);
        Assert.Null(list.ContainerFromItem(first));

        // The marks are in the conversation's scroll bar, as wide as its thumb is drawn: thin while the bar rests, scaled from
        // its right edge, and its whole width once the bar widens.
        var bar = view.GetVisualDescendants().OfType<ScrollBar>().First(b => b.Orientation == Orientation.Vertical && b.IsEffectivelyVisible);
        var onFirst = marks.PointOf(marks.Marks[0]);
        Assert.True(new Rect(bar.Bounds.Size).Contains(marks.TranslatePoint(onFirst, bar)!.Value), "The mark is outside the scroll bar.");
        var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
        double Drawn() => Math.Abs(thumb.TranslatePoint(new Point(thumb.Bounds.Width, 0), marks)!.Value.X - thumb.TranslatePoint(default, marks)!.Value.X);
        Assert.InRange(marks.Lane().Width, Drawn() - 0.5, Drawn() + 0.5);
        // At rest, 6 pixels: the app's resting thumb, wider than Fluent's 2.
        Assert.InRange(marks.Lane().Width, 5.5, 6.5);
        var (restingWidth, resting) = (marks.Lane().Width, thumb.RenderTransform);
        // Widened, as the pointer near it does, and back: Fluent animates the thumb, and the marks keep up.
        thumb.RenderTransform = null;
        await UiText.SettleUntilAsync(window, () => Math.Abs(marks.Lane().Width - bar.Bounds.Width) < 0.5, "the marks as wide as the bar");
        thumb.RenderTransform = resting;
        await UiText.SettleUntilAsync(window, () => Math.Abs(marks.Lane().Width - restingWidth) < 0.5, "the marks thin again");
        onFirst = marks.PointOf(marks.Marks[0]);

        // A click on the first mark goes to the first message.
        var at = marks.TranslatePoint(onFirst, window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        await UiText.SettleUntilAsync(window, () => list.ContainerFromItem(first) is not null, "the first message");

        // There, the thumb is over the first mark, and takes the pointer to be dragged.
        await UiText.SettleUntilAsync(window, () => !marks.HitTest(marks.PointOf(marks.Marks[0])), "the thumb over the first mark");
        Assert.True(marks.HitTest(marks.PointOf(marks.Marks[1])));

        // F4 goes on to the next message, F3 back, Ctrl+F4 to the latest.
        window.KeyPressQwerty(PhysicalKey.F4, RawInputModifiers.None);
        await UiText.SettleUntilAsync(window, () => list.ContainerFromItem(second) is { } shown && IsAtTheTop(shown, view), "the second message");
        window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.None);
        await UiText.SettleUntilAsync(window, () => list.ContainerFromItem(first) is { } shown && IsAtTheTop(shown, view), "back to the first");
        window.KeyPressQwerty(PhysicalKey.F4, OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control);
        await UiText.SettleUntilAsync(window, () => UiText.Describe(view).Contains("Later note 149", StringComparison.Ordinal), "the latest");
        window.Close();
    }

    /// <summary>
    /// Settings → Conversation → Marks in the scroll bar (issue #99): Claude's answers get marks in its color once turned
    /// on, the replies it carried on after with a tool call don't, and your messages' go once turned off, at once.
    /// </summary>
    [AvaloniaFact]
    public async Task The_scroll_bar_marks_Claudes_answers_and_your_messages_as_settings_say()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        tab.Items.Add(new UserMessageItem("Fix the build"));
        var carriedOn = new AssistantTextItem { IsStreaming = false, IsIntermediate = true };
        carriedOn.Append("Let me look at the log.");
        tab.Items.Add(carriedOn);
        for (var i = 0; i < 150; i++)
        {
            tab.Items.Add(new NoteItem($"Note {i}", NoteKind.Info));
        }
        var answer = new AssistantTextItem { IsStreaming = false };
        answer.Append("The build passes now.");
        tab.Items.Add(answer);
        for (var i = 0; i < 150; i++)
        {
            tab.Items.Add(new NoteItem($"Later note {i}", NoteKind.Info));
        }
        UiText.Settle(window);
        var view = window.GetVisualDescendants().OfType<TabView>().Single();
        var marks = view.GetVisualDescendants().OfType<ScrollMarks>().Single(m => m.Name == "ScrollMarks");
        await UiText.SettleUntilAsync(window, () => marks.Marks.Count == 1, "your message's mark");
        Assert.Equal([0], marks.Marks.Select(m => m.Index));

        h.Services.Settings.Appearance.MarkClaudesAnswers = true;
        h.Services.SaveSettings();
        await UiText.SettleUntilAsync(window, () => marks.Marks.Count == 2, "the answer's mark");
        Assert.Equal([0, 152], marks.Marks.Select(m => m.Index));
        Assert.Equal("Claude: The build passes now.", marks.Marks[1].Tip);
        Assert.Equal(((ISolidColorBrush)view.FindResource(view.ActualThemeVariant, "CardClaudeStripeBrush")!).Color, ((ISolidColorBrush)marks.Marks[1].Brush).Color);

        h.Services.Settings.Appearance.MarkYourMessages = false;
        h.Services.SaveSettings();
        await UiText.SettleUntilAsync(window, () => marks.Marks.Count == 1, "only the answer's mark");
        Assert.Equal([152], marks.Marks.Select(m => m.Index));
        window.Close();
    }

    [AvaloniaFact]
    public async Task An_open_thinking_card_ends_where_the_thinking_does_whatever_blank_lines_it_ends_with()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        const string thought = "The tests pass, so I should write up a short report and check whether it was pushed.";
        foreach (var text in new[] { thought, "\n" + thought + "\n\n\n" })
        {
            var thinking = new ThinkingItem { IsExpanded = true };
            thinking.Append(text);
            thinking.IsStreaming = false;
            tab.Items.Add(thinking);
        }
        UiText.Settle(window);

        var cards = window.GetVisualDescendants().OfType<Card>().Where(c => c.Classes.Contains("thinking")).ToList();
        Assert.Equal(2, cards.Count);
        // Claude's thinking often starts or ends with blank lines: they leave no empty room in the card.
        Assert.Equal(cards[0].Bounds.Height, cards[1].Bounds.Height);
        Assert.Contains(thought, UiText.Describe(cards[1]), StringComparison.Ordinal);
        window.Close();
    }

    /// <summary>
    /// Italics aren't cut off at the text's edge (#126). Inter has no italic face, so its italics are Inter slanted, and a
    /// tall letter's top leans past the width the text was measured at, where a text block clips. Headless drawing has no
    /// slant to see, so this checks that the thinking's text doesn't clip.
    /// </summary>
    [AvaloniaFact]
    public async Task Thinking_is_in_italics_that_draw_past_the_texts_width()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        var thinking = new ThinkingItem { IsExpanded = true };
        thinking.Append("Check the crawl");
        thinking.IsStreaming = false;
        tab.Items.Add(thinking);
        UiText.Settle(window);

        var text = Named(window, "thinking").Single().GetVisualDescendants().OfType<SelectableTextBlock>().Single(b => b.Text == "Check the crawl");
        Assert.Equal((FontStyle.Italic, false), (text.FontStyle, text.ClipToBounds));
        window.Close();
    }

    /// <summary>
    /// A reply's emphasis and a step's thinking in a folded chain draw past their text's width too (#126), as the
    /// thinking does: a table's cell is as wide as its text, so <c>*optional*</c> in one lost the top of its l every time.
    /// Unclipped, nothing may be wider than its block, or it would draw over the next: a long cell wraps.
    /// </summary>
    [AvaloniaFact]
    public async Task A_replys_emphasis_and_a_steps_thinking_draw_past_their_width_and_a_long_cell_still_wraps()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell }, width: 900);
        h.Services.Settings.Appearance.FoldChains = true;
        h.Services.SaveSettings();
        h.Transport.Emit("""{"type":"assistant","message":{"content":[{"type":"thinking","thinking":"One."},{"type":"tool_use","id":"r1","name":"Read","input":{"file_path":"a.cs"}}]}}""");
        h.Transport.Emit("""{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"r1","content":"x"}]}}""");
        h.Transport.EmitTurn("Done, with *one flag* left to add\n\n| Flag | Use |\n|---|---|\n| `-q` | *optional* |\n| `-v` | "
            + string.Concat(Enumerable.Repeat("only when the log would otherwise be too long to read through, ", 4)) + "*if ever* |");
        await UiText.SettleUntilAsync(window, () => Replies(window).Count == 1 && tab.Items.OfType<StepsMarkerItem>().SingleOrDefault() is { IsFolded: true },
            "the reply, carrying the steps");
        var card = Replies(window)[0];
        await UiText.SettleUntilAsync(window, () => Blocks(card).Count >= 7, "the reply's paragraph and the table's cells");

        Assert.All(Blocks(card), b =>
        {
            Assert.False(b.ClipToBounds, $"\"{b.LayoutText}\" clips");
            Assert.True(b.TextLayout.Width <= b.Bounds.Width - b.Padding.Left - b.Padding.Right + 0.5,
                $"\"{b.LayoutText}\" is {b.TextLayout.Width} wide in a block of {b.Bounds.Width}");
        });

        var marker = tab.Items.OfType<StepsMarkerItem>().Single();
        marker.ToggleCommand.Execute(null);
        UiText.Settle(window);
        marker.Rows[0].ToggleCommand.Execute(null);
        UiText.Settle(window);
        var thought = card.GetVisualDescendants().OfType<SelectableTextBlock>().Single(b => b.Text == "One." && b.IsEffectivelyVisible);
        Assert.Equal((FontStyle.Italic, false), (thought.FontStyle, thought.ClipToBounds));
        window.Close();

        static List<LiveMarkdown.Avalonia.MarkdownTextBlock> Blocks(Card card) =>
            [.. card.GetVisualDescendants().OfType<LiveMarkdown.Avalonia.MarkdownRenderer>().Single()
                .GetVisualDescendants().OfType<LiveMarkdown.Avalonia.MarkdownTextBlock>().Where(b => b.IsEffectivelyVisible)];
    }

    private static bool IsAtTheTop(Control container, TabView view)
    {
        var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "ConversationScroll");
        var top = container.TranslatePoint(default, scroll)!.Value.Y;
        return top is >= 0 and < 40;
    }

    private static List<Button> Tiles(Window window) =>
        [.. window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("tile"))];

    private static List<Card> YourCards(Window window) =>
        [.. window.GetVisualDescendants().OfType<Card>().Where(c => c.Classes.Contains("you"))];

    /// <summary>The steps folded away, not counting the lines of the chains themselves.</summary>
    private static int FoldedSteps(Deventry.App.ViewModels.TabViewModel tab) =>
        tab.Items.Count(i => i is not StepsMarkerItem && i.IsFolded);

    private static Button Toggle(Card card) =>
        card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Toggle");

    private static int VisibleTiles(Window window) => Tiles(window).Count(t => t.IsEffectivelyVisible);

    private static List<Card> Named(Window window, string kind) =>
        [.. window.GetVisualDescendants().OfType<Card>().Where(c => c.Classes.Contains(kind))];

    private static List<Card> Replies(Window window) =>
        [.. window.GetVisualDescendants().OfType<Card>().Where(c => c.Classes.Contains("claude"))];

    private static Border Part(Card card) => card.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Card");
}
