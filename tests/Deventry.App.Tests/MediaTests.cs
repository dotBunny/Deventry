using System.Text.Json.Nodes;
using Deventry.App.Conversation;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.App.ViewModels.ToolWindows;
using Deventry.Core.Media;
using Deventry.Core.Settings;

namespace Deventry.App.Tests;

/// <summary>
/// The Media window's gallery (DESIGN.md §18, "Media"): the user's images and the media files their messages name, and the
/// agent's, from tools' results, the files its tools work on and its replies, newest first, each once.
/// </summary>
public sealed class MediaTests
{
    [Fact]
    public async Task An_attached_image_is_the_users_a_tools_image_the_agents_and_the_icon_shows_once_there_is_either()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        var window = h.ToolWindow(ToolWindowKinds.Media);
        Assert.False(InlineDispatcher.Read(() => window.IsAvailable));

        await SendAsync(tab, "What's wrong in this?", TestImages.Png);
        await TabTestHarness.Eventually(() => tab.Media.User.Count == 1, "the attached image");
        Assert.True(InlineDispatcher.Read(() => window.IsAvailable));

        h.Transport.Emit(Wire.Tool(null, "t1", "mcp__playwright__browser_take_screenshot", []));
        h.Transport.Emit(ImageResult("t1", parent: null));
        await TabTestHarness.Eventually(() => tab.Media.Agent.Count == 1, "the screenshot");

        var (attached, screenshot) = InlineDispatcher.Read(() => (tab.Media.User.Entries.Single(), tab.Media.Agent.Entries.Single()));
        Assert.Equal(("Attached image", "Attached", MediaKind.Image, false), (attached.Name, attached.Origin, attached.Kind, attached.IsFile));
        Assert.IsType<UserMessageItem>(attached.Item);
        Assert.Equal(("Image", "mcp__playwright__browser_take_screenshot"), (screenshot.Name, screenshot.Origin));
        Assert.IsType<ToolUseItem>(screenshot.Item);
        // Each gets a thumbnail of its own, made off the UI thread.
        await TabTestHarness.Eventually(() => attached.HasThumbnail && screenshot.HasThumbnail, "the thumbnails");
        Assert.Equal("User 1 · Agent 1", InlineDispatcher.Read(() => h.ToolWindow(ToolWindowKinds.Media).Tip));
    }

    [Fact]
    public async Task The_media_files_a_message_names_are_the_users_once_they_are_found_in_order()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        var bug = WriteFile(h, "shots/bug.png", TestImages.Png);
        var clip = WriteFile(h, "clips/run.mp4", [0, 0, 0, 0x18]);

        await SendAsync(tab, "The menu in shots/bug.png is wrong; see @clips/run.mp4, and missing.png, and notes.md.");
        await TabTestHarness.Eventually(() => tab.Media.User.Count == 2, "the two files");

        var entries = InlineDispatcher.Read(() => tab.Media.User.Entries.ToArray());
        // In the order the message names them.
        Assert.Equal([(bug, MediaKind.Image), (clip, MediaKind.Video)], entries.Select(e => (e.Path!, e.Kind)));
        Assert.All(entries, e => Assert.Equal("Named in your message", e.Origin));
        Assert.Contains(Path.Combine("shots", "bug.png"), entries[0].Tip, StringComparison.Ordinal);
        await TabTestHarness.Eventually(() => entries[0].HasThumbnail, "the image file's thumbnail");
        Assert.False(entries[1].HasThumbnail);
        Assert.Equal("IconVideo", entries[1].KindIcon);
        Assert.Empty(InlineDispatcher.Read(() => tab.Media.Agent.Entries.ToArray()));
    }

    [Fact]
    public async Task What_the_agent_writes_runs_a_command_on_or_names_is_the_agents_newest_first_each_once()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        var logo = WriteFile(h, "art/logo.svg", "<svg/>"u8.ToArray());
        var plot = WriteFile(h, "out/plot.png", TestImages.Png);
        var final = WriteFile(h, "out/final.gif", TestImages.Gif([10]));

        h.Transport.Emit(Wire.Tool(null, "t1", "Write", new JsonObject { ["file_path"] = logo, ["content"] = "<svg/>" }));
        h.Transport.Emit(Wire.Result("t1", "File created"));
        h.Transport.Emit(Wire.Tool(null, "t2", "Bash", new JsonObject { ["command"] = "python plot.py --out=out/plot.png" }));
        h.Transport.Emit(Wire.Result("t2", ""));
        await TabTestHarness.Eventually(() => tab.Media.Agent.Count == 2, "the written file and the command's");
        h.Transport.Emit(Wire.Text(null, "The plot is in `out/plot.png`, and the animation in [final.gif](out/final.gif)."));

        // The reply named the plot again: it's there once, as the newest, with the animation after it. The files a reply
        // names are each looked for on their own, and come back in any order.
        (string, string)[] expected = [(plot, "Named in a reply"), (final, "Named in a reply"), (logo, "Write")];
        await TabTestHarness.Eventually(() => tab.Media.Agent.Entries.Select(e => (e.Path!, e.Origin)).SequenceEqual(expected), "the files the reply names");
        var entries = InlineDispatcher.Read(() => tab.Media.Agent.Entries.ToArray());
        Assert.IsType<AssistantTextItem>(entries[0].Item);
        // An SVG is shown by the OS's app: its tile has the picture icon.
        await TabTestHarness.Eventually(() => entries[0].HasThumbnail && entries[1].HasThumbnail, "the thumbnails");
        Assert.False(entries[2].HasThumbnail);
    }

    [Fact]
    public async Task A_subagents_screenshot_and_reply_are_the_agents_too()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        var shot = WriteFile(h, "shots/sub.webp", TestImages.Png);

        h.Transport.Emit(Wire.Agent("a1", "Check the page", "Take a screenshot"));
        h.Transport.Emit(Wire.Tool("a1", "t1", "mcp__chrome__screenshot", []));
        h.Transport.Emit(ImageResult("t1", parent: "a1"));
        h.Transport.Emit(Wire.Text("a1", "Also saved it to shots/sub.webp."));
        await TabTestHarness.Eventually(() => tab.Media.Agent.Count == 2, "the subagent's media");

        Assert.Equal([shot, "(none)"], InlineDispatcher.Read(() => tab.Media.Agent.Entries.Select(e => e.Path ?? "(none)").ToArray()));
    }

    [Fact]
    public async Task The_same_screenshot_twice_shows_once()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();

        h.Transport.Emit(Wire.Tool(null, "t1", "mcp__playwright__browser_take_screenshot", []));
        h.Transport.Emit(ImageResult("t1", parent: null));
        h.Transport.Emit(Wire.Tool(null, "t2", "mcp__playwright__browser_take_screenshot", []));
        h.Transport.Emit(ImageResult("t2", parent: null));
        await TabTestHarness.Eventually(() => tab.Items.ToolCalls().Count(c => c.HasImages) == 2, "both screenshots");

        var entry = InlineDispatcher.Read(() => Assert.Single(tab.Media.Agent.Entries));
        Assert.Equal("t2", Assert.IsType<ToolUseItem>(entry.Item).ToolUseId);
    }

    [Fact]
    public async Task A_tile_opens_an_image_in_the_window_and_anything_else_in_the_OSs_app_and_its_menu_does_the_rest()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        var bug = WriteFile(h, "shots/bug.png", TestImages.Png);
        var clip = WriteFile(h, "clips/run.mp4", [0, 0, 0, 0x18]);
        await SendAsync(tab, "See shots/bug.png and clips/run.mp4", TestImages.Png);
        await TabTestHarness.Eventually(() => tab.Media.User.Count == 3, "the three");
        var (attached, image, video) = InlineDispatcher.Read(() => (tab.Media.User.Entries[0], tab.Media.User.Entries[1], tab.Media.User.Entries[2]));
        var shown = new List<ImageViewerViewModel>();
        h.Shell.ShowImageWindow = shown.Add;

        await InlineDispatcher.RunAsync(() => tab.OpenMediaCommand.ExecuteAsync(attached));
        await InlineDispatcher.RunAsync(() => tab.OpenMediaCommand.ExecuteAsync(image));
        await InlineDispatcher.RunAsync(() => tab.OpenMediaCommand.ExecuteAsync(video));

        Assert.Equal([("Attached image", false), ("bug.png", true)], shown.Select(v => (v.Title, v.CanOpen)));
        Assert.Equal([clip], h.Platform.OpenedFiles);
        await InlineDispatcher.RunAsync(() => tab.OpenMediaInSystemCommand.ExecuteAsync(image));
        Assert.Equal([clip, bug], h.Platform.OpenedFiles);
        await InlineDispatcher.RunAsync(() => tab.CopyMediaPathCommand.ExecuteAsync(video));
        Assert.Equal(clip, h.Platform.Clipboard);
        await InlineDispatcher.RunAsync(() => tab.RevealMediaCommand.ExecuteAsync(image));
        Assert.Equal([Path.GetDirectoryName(bug)!], h.Platform.RevealedFolders);

        ConversationItem? scrolledTo = null;
        tab.ScrollToRequested += item => scrolledTo = item;
        InlineDispatcher.Run(() => tab.ShowMediaInConversationCommand.Execute(video));
        Assert.Same(video.Item, scrolledTo);
        foreach (var viewer in shown)
        {
            viewer.Dispose();
        }
    }

    [Fact]
    public async Task Going_back_to_an_earlier_message_leaves_out_the_media_after_it()
    {
        await using var h = new TabTestHarness();
        var first = WriteFile(h, "shots/first.png", TestImages.Png);
        WriteFile(h, "shots/second.png", TestImages.Png);
        h.WriteTranscript("s1",
            Entry("user", "u1", null, "Look at shots/first.png"),
            Entry("assistant", "a1", "u1", new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "one" })),
            Entry("user", "u2", "a1", "Now shots/second.png"),
            Entry("assistant", "a2", "u2", new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "two" })));
        h.Services.State.Tabs = [new TabState { Folder = h.WorkFolder, IsPinned = true, SessionId = "s1" }];
        h.Shell.Restore(null);
        h.Start();
        var tab = h.Shell.AllTabs.Single();
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle && tab.IsSettled && tab.Media.User.Count == 2, "the restored tab's media");

        var second = InlineDispatcher.Read(() => tab.Items.OfType<UserMessageItem>().Single(m => m.Text.StartsWith("Now", StringComparison.Ordinal)));
        await InlineDispatcher.RunAsync(() => tab.EditAndResendCommand.ExecuteAsync(second));
        await InlineDispatcher.RunAsync(() => h.Shell.Confirmation!.ConfirmCommand.ExecuteAsync(null));
        await TabTestHarness.Eventually(() => h.Factory.Launches.Count == 2 && tab.Status == TabStatus.Idle && tab.IsSettled, "the restart");
        await TabTestHarness.Eventually(() => tab.Media.User.Count == 1, "the first message's media alone");

        Assert.Equal(first, InlineDispatcher.Read(() => tab.Media.User.Entries.Single().Path));
    }

    [Fact]
    public void A_section_folds_to_its_header()
    {
        var gallery = new MediaGallery();

        Assert.Equal(("Collapse User", true), (gallery.User.ToggleName, gallery.User.ShowsEmptyText));
        gallery.User.ToggleCommand.Execute(null);

        Assert.Equal(("Expand User", false), (gallery.User.ToggleName, gallery.User.ShowsEmptyText));
        Assert.True(gallery.Agent.IsExpanded);
    }

    private static async Task SendAsync(TabViewModel tab, string text, byte[]? image = null)
    {
        InlineDispatcher.Run(() =>
        {
            if (image is not null)
            {
                Assert.True(tab.AddImage(image, "Pasted image"));
            }
            tab.ComposerText = text;
        });
        await InlineDispatcher.RunAsync(() => tab.SendCommand.ExecuteAsync(null));
    }

    /// <summary>Writes <paramref name="relative"/> in the tab's folder, returning its full path.</summary>
    private static string WriteFile(TabTestHarness h, string relative, byte[] data)
    {
        var path = Path.Combine(h.WorkFolder, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        return path;
    }

    /// <summary>A tool's result that's an image, as an MCP tool's screenshot is.</summary>
    private static string ImageResult(string toolUseId, string? parent) => new JsonObject
    {
        ["type"] = "user",
        ["message"] = new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "tool_result",
                ["tool_use_id"] = toolUseId,
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "image",
                    ["source"] = new JsonObject { ["type"] = "base64", ["data"] = Convert.ToBase64String(TestImages.Png), ["media_type"] = "image/png" },
                }),
            }),
        },
        ["parent_tool_use_id"] = parent,
    }.ToJsonString();

    private static string Entry(string type, string uuid, string? parent, JsonNode content) => new JsonObject
    {
        ["type"] = type,
        ["uuid"] = uuid,
        ["parentUuid"] = parent,
        ["sessionId"] = "s1",
        ["timestamp"] = "2026-09-28T10:00:00Z",
        ["message"] = new JsonObject { ["role"] = type, ["content"] = content },
    }.ToJsonString();
}
