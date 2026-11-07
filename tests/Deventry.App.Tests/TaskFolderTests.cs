using System.Text.Json.Nodes;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Sessions;

namespace Deventry.App.Tests;

/// <summary>
/// A tab's task list kept in step with the list Claude Code keeps in its tasks folder (DESIGN.md §5, "Tasks"), which is
/// what Claude sees.
/// </summary>
public class TaskFolderTests
{
    private static string ConfigFolder(TabTestHarness h) => Path.Combine(h.Root, "claude");

    private static string ListFolder(TabTestHarness h, string listId = "s1") => ClaudeTaskFolder.Folder(ConfigFolder(h), listId);

    /// <summary>Writes a task's file as Claude Code does; returns its path.</summary>
    private static string WriteTask(TabTestHarness h, string id, string subject, string status = "pending", string listId = "s1")
    {
        var folder = ListFolder(h, listId);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{id}.json");
        File.WriteAllText(path, new JsonObject
        {
            ["id"] = id, ["subject"] = subject, ["description"] = "", ["status"] = status, ["blocks"] = new JsonArray(), ["blockedBy"] = new JsonArray(),
        }.ToJsonString());
        return path;
    }

    /// <summary>Claude makes a task: Claude Code writes its file, then answers with its number.</summary>
    private static void Create(TabTestHarness h, string toolUseId, string id, string subject, bool written = true, string listId = "s1")
    {
        h.Transport.Emit(Wire.Tool(null, toolUseId, "TaskCreate", new JsonObject { ["subject"] = subject }));
        if (written)
        {
            WriteTask(h, id, subject, listId: listId);
        }
        h.Transport.Emit(Wire.Result(toolUseId, $"Task #{id} created successfully: {subject}", toolUseResult: new JsonObject { ["task"] = new JsonObject { ["id"] = id, ["subject"] = subject } }));
    }

    private static void Update(TabTestHarness h, string toolUseId, string id, string status)
    {
        h.Transport.Emit(Wire.Tool(null, toolUseId, "TaskUpdate", new JsonObject { ["taskId"] = id, ["status"] = status }));
        h.Transport.Emit(Wire.Result(toolUseId, $"Updated task #{id} status", toolUseResult: new JsonObject { ["success"] = true, ["taskId"] = id }));
    }

    /// <summary>A tab whose session has had its first turn, reading the folder of the list it uses.</summary>
    private static async Task<(TabTestHarness H, TabViewModel Tab)> StartAsync(Action<TabTestHarness>? setUp = null, string listId = "s1")
    {
        var h = new TabTestHarness();
        h.Services.ClaudeConfigDirectory = ConfigFolder(h);
        setUp?.Invoke(h);
        var tab = await h.OpenTabAsync();
        h.Transport.Emit(Wire.Init());
        await TabTestHarness.Eventually(() => tab.TaskFolderRead == ListFolder(h, listId), "the list's folder followed");
        return (h, tab);
    }

    [Fact]
    public async Task The_list_follows_the_folder_as_another_session_sharing_it_changes_it()
    {
        var (h, tab) = await StartAsync();
        await using var _ = h;
        Create(h, "c1", "1", "Fix the config");
        Create(h, "c2", "2", "Run the tests");
        await TabTestHarness.Eventually(() => h.FolderWatcher.Folders.Contains(ListFolder(h)), "the folder watched");

        // Another session on the same list finishes #1 and adds #3.
        h.FolderWatcher.Changed(WriteTask(h, "1", "Fix the config", "completed"));
        h.FolderWatcher.Changed(WriteTask(h, "3", "Write the notes"));

        await TabTestHarness.Eventually(() => tab.TodoList.Items.Count == 3 && tab.TodoList.Items[0].IsDone, "the other session's changes");
        Assert.Equal(["#1 Fix the config", "#2 Run the tests", "#3 Write the notes"], tab.TodoList.Items.Select(i => i.Title));
        Assert.Equal("1 of 3", tab.TodoList.Badge);

        // It deletes #2: gone here too.
        var two = Path.Combine(ListFolder(h), "2.json");
        File.Delete(two);
        h.FolderWatcher.Changed(two);
        await TabTestHarness.Eventually(() => tab.TodoList.Items.Count == 2, "#2 gone");
    }

    [Fact]
    public async Task A_folder_that_hasnt_the_task_Claude_just_made_is_left_alone()
    {
        var (h, tab) = await StartAsync();
        await using var _ = h;
        // Claude Code keeps this list somewhere else: it never writes the task to the folder Deventry looks in, which
        // has someone else's.
        Create(h, "c1", "1", "Fix the config", written: false);
        await TabTestHarness.Eventually(() => tab.TodoList.Items.SingleOrDefault()?.Id == "1", "#1 numbered");
        WriteTask(h, "7", "Someone else's");
        Update(h, "u1", "1", "in_progress");

        await TabTestHarness.Eventually(() => tab.TaskFolderRead is null, "the folder left alone");
        Assert.Equal(["#1 Fix the config"], tab.TodoList.Items.Select(i => i.Title));
        Assert.True(tab.TodoList.Items[0].IsActive);
        Assert.Empty(h.FolderWatcher.Folders);
    }

    [Fact]
    public async Task The_variable_names_the_list_the_session_shares()
    {
        var (h, tab) = await StartAsync(h => h.ClaudeUserEnvironment["CLAUDE_CODE_TASK_LIST_ID"] = "my-project", listId: "my-project");
        await using var _ = h;
        WriteTask(h, "4", "From the shared list", listId: "my-project");
        Create(h, "c1", "5", "Mine", listId: "my-project");

        await TabTestHarness.Eventually(() => tab.TodoList.Items.Count == 2, "the shared list");
        Assert.Equal(["#4 From the shared list", "#5 Mine"], tab.TodoList.Items.OrderBy(i => i.Id).Select(i => i.Title));
    }

    [Fact]
    public async Task A_restored_tab_shows_the_list_as_Claude_Code_has_it_now()
    {
        await using var h = new TabTestHarness();
        h.Services.ClaudeConfigDirectory = ConfigFolder(h);
        var created = new[] { ("c1", "1", "Done and deleted"), ("c2", "2", "Fix the config"), ("c3", "3", "Run the tests") };
        h.WriteTranscript("s1", [.. created.SelectMany(c => new[]
        {
            Wire.Entry("assistant", "2026-09-28T11:00:00Z", Wire.Message(new JsonObject { ["type"] = "tool_use", ["id"] = c.Item1, ["name"] = "TaskCreate", ["input"] = new JsonObject { ["subject"] = c.Item3 } })),
            Wire.Entry("user", "2026-09-28T11:00:01Z", Wire.ResultMessage(c.Item1, $"Task #{c.Item2} created successfully: {c.Item3}"),
                new JsonObject { ["task"] = new JsonObject { ["id"] = c.Item2, ["subject"] = c.Item3 } }),
        })]);
        // Since then, #1 was deleted and #2 finished, as the folder has it.
        WriteTask(h, "2", "Fix the config", "completed");
        WriteTask(h, "3", "Run the tests");
        h.Services.State.Tabs = [new Core.Settings.TabState { Folder = h.WorkFolder, IsPinned = true, SessionId = "s1" }];
        h.Shell.Restore(null);
        h.Start();
        var tab = h.Shell.AllTabs.Single();
        h.Shell.SelectedTab = tab;

        await TabTestHarness.Eventually(() => tab.TodoList.Items.Count == 2, "the list as Claude Code has it");
        Assert.Equal(["#2 Fix the config", "#3 Run the tests"], tab.TodoList.Items.Select(i => i.Title));
        Assert.True(tab.TodoList.Items[0].IsDone);
    }
}
