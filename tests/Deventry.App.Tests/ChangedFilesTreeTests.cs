using System.Text.Json.Nodes;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Settings;

namespace Deventry.App.Tests;

/// <summary>The tree Changed files shows its files in, folding by folder, and the list it switches to (DESIGN.md §8, "The tree").</summary>
public class ChangedFilesTreeTests
{
    private static readonly string[] Starfall =
    [
        "Source/Starfall/Player/StarfallCharacter.h",
        "Source/Starfall/Abilities/GrappleComponent.h",
        "README.md",
        "Source/Starfall/Abilities/GrappleComponent.cpp",
    ];

    [Fact]
    public async Task Changed_files_show_under_their_folders_with_each_folders_count_and_counts()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();

        await EditAsync(h, tab, Starfall);

        Assert.True(tab.ChangedFiles.ShowAsTree);
        Assert.Equal(
        [
            "Source/Starfall (3 files, +3 −3) 0",
            "Abilities (2 files, +2 −2) 1",
            "GrappleComponent.cpp 2",
            "GrappleComponent.h 2",
            "Player (1 file, +1 −1) 1",
            "StarfallCharacter.h 2",
            "README.md 0",
        ], Shown(tab));
        var folder = Folder(tab, "Source/Starfall");
        Assert.Equal(Path.Combine(h.WorkFolder, "Source", "Starfall"), folder.Path);
        Assert.Equal(0, folder.Indent);
        // A file in the tree is in past its folder's arrow, and its path isn't under its name.
        var file = File(tab, "GrappleComponent.h");
        Assert.True(file.InTree);
        Assert.Equal(2 * ChangedTreeRow.TreeLevel + ChangedTreeRow.ArrowWidth, file.Indent);
        Assert.True(tab.ChangedFiles.HasFolders);
        // Files stays every file, in the order Claude changed them.
        Assert.Equal(["StarfallCharacter.h", "GrappleComponent.h", "README.md", "GrappleComponent.cpp"], tab.ChangedFiles.Files.Select(f => f.FileName));
    }

    [Fact]
    public async Task A_collapsed_folder_hides_its_files_stays_collapsed_as_files_change_and_is_saved_with_the_tab()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        await EditAsync(h, tab, Starfall);
        var abilities = Folder(tab, "Abilities");

        h.OnUiThread(() => tab.ChangedFiles.ToggleFolderCommand.Execute(abilities));

        Assert.False(abilities.IsExpanded);
        Assert.Equal(["Source/Starfall", "Abilities", "Player", "StarfallCharacter.h", "README.md"], Names(tab));
        Assert.Equal([abilities.Path], tab.State.CollapsedChangedFolders);

        // Claude changes another file in it: it stays collapsed, and counts it.
        await EditAsync(h, tab, ["Source/Starfall/Abilities/GrappleTypes.h"]);
        Assert.Equal(["Source/Starfall", "Abilities", "Player", "StarfallCharacter.h", "README.md"], Names(tab));
        Assert.Equal("3 files", Folder(tab, "Abilities").FilesText);

        h.OnUiThread(() => tab.ChangedFiles.ToggleFolderCommand.Execute(abilities));

        Assert.Equal(["Source/Starfall", "Abilities", "GrappleComponent.cpp", "GrappleComponent.h", "GrappleTypes.h", "Player", "StarfallCharacter.h", "README.md"], Names(tab));
        Assert.Empty(tab.State.CollapsedChangedFolders);
    }

    [Fact]
    public async Task A_restored_tab_keeps_its_collapsed_folders_and_clear_forgets_them()
    {
        await using var h = new TabTestHarness();
        var player = Path.Combine(h.WorkFolder, "Source", "Player");
        var (character, grapple) = (Path.Combine(player, "Character.h"), Path.Combine(h.WorkFolder, "Source", "Abilities", "Grapple.h"));
        foreach (var path in new[] { character, grapple })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await System.IO.File.WriteAllTextAsync(path, "b\n", TestContext.Current.CancellationToken);
        }
        h.WriteTranscript("s1", [.. TranscriptEdit("e1", character), .. TranscriptEdit("e2", grapple)]);
        h.Services.State.Tabs = [new TabState { Folder = h.WorkFolder, IsPinned = true, SessionId = "s1", CollapsedChangedFolders = [player] }];

        h.Shell.Restore(null);
        h.Start();
        var tab = h.Shell.AllTabs.Single();

        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle && Names(tab).Count == 4, "the tree");
        Assert.Equal(["Source", "Abilities", "Grapple.h", "Player"], Names(tab));
        Assert.True(Folder(tab, "Player").IsCollapsed);

        h.OnUiThread(tab.ChangedFiles.Clear);

        Assert.Empty(tab.State.CollapsedChangedFolders);
    }

    [Fact]
    public async Task Alt_click_folds_every_level_under_a_folder_and_collapse_all_folds_them_all()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        await EditAsync(h, tab, Starfall);

        h.OnUiThread(() => tab.ChangedFiles.ToggleFolderAllLevels(Folder(tab, "Source/Starfall")));

        Assert.Equal(["Source/Starfall", "README.md"], Names(tab));
        // Expanded on its own, its folders show collapsed.
        h.OnUiThread(() => tab.ChangedFiles.ToggleFolderCommand.Execute(Folder(tab, "Source/Starfall")));
        Assert.Equal(["Source/Starfall", "Abilities", "Player", "README.md"], Names(tab));

        h.OnUiThread(() => tab.ChangedFiles.ExpandAllCommand.Execute(null));
        Assert.Equal(7, Names(tab).Count);
        Assert.Empty(tab.State.CollapsedChangedFolders);

        h.OnUiThread(() => tab.ChangedFiles.CollapseAllCommand.Execute(null));
        Assert.Equal(["Source/Starfall", "README.md"], Names(tab));
        Assert.Equal(3, tab.State.CollapsedChangedFolders.Count);
    }

    [Fact]
    public async Task A_folders_box_ticks_every_file_in_it_and_is_partly_ticked_while_some_are()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        await EditAsync(h, tab, Starfall);
        var (starfall, abilities) = (Folder(tab, "Source/Starfall"), Folder(tab, "Abilities"));

        h.OnUiThread(() => tab.ChangedFiles.ToggleFileReviewedCommand.Execute(File(tab, "GrappleComponent.h")));

        Assert.Null(abilities.IsReviewed);
        Assert.Null(starfall.IsReviewed);
        Assert.False(Folder(tab, "Player").IsReviewed);

        // Partly ticked, its box ticks the rest.
        h.OnUiThread(() => tab.ChangedFiles.ToggleFolderReviewedCommand.Execute(starfall));

        Assert.Equal((true, true, true), (starfall.IsReviewed, abilities.IsReviewed, starfall.IsAllReviewed));
        Assert.Equal([true, true, false, true], tab.ChangedFiles.Files.Select(f => f.IsReviewed));
        Assert.Equal("4 files changed · 3 reviewed", tab.ChangedFiles.Summary);
        Assert.Equal(3, tab.State.ReviewedFiles.Count);

        // All ticked, it unticks them.
        h.OnUiThread(() => tab.ChangedFiles.ToggleFolderReviewedCommand.Execute(abilities));

        Assert.Equal((false, null), (abilities.IsReviewed, starfall.IsReviewed));
        Assert.Equal("4 files changed · 1 reviewed", tab.ChangedFiles.Summary);
    }

    [Fact]
    public async Task The_switch_shows_the_list_as_it_was_and_back_and_is_remembered()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        await EditAsync(h, tab, Starfall);

        h.OnUiThread(() => tab.ChangedFiles.ToggleViewCommand.Execute(null));

        Assert.False(tab.ChangedFiles.ShowAsTree);
        Assert.True(h.Services.State.ChangedFilesAsList);
        Assert.Equal("Show as a tree", tab.ChangedFiles.ViewSwitchText);
        Assert.False(tab.ChangedFiles.HasFolders);
        // The list as it was: every file, in the order Claude changed them, each with its path under its name.
        Assert.Equal<ChangedTreeRow>(tab.ChangedFiles.Files, tab.ChangedFiles.Rows);
        Assert.All(tab.ChangedFiles.Files, f => Assert.Equal((false, 0.0), (f.InTree, f.Indent)));

        h.OnUiThread(() => tab.ChangedFiles.ToggleViewCommand.Execute(null));

        Assert.True(tab.ChangedFiles.ShowAsTree);
        Assert.False(h.Services.State.ChangedFilesAsList);
        Assert.Equal(7, tab.ChangedFiles.Rows.Count);
    }

    [Fact]
    public async Task Each_working_tree_heads_a_tree_of_its_own_which_folds()
    {
        await using var h = new TabTestHarness();
        FakeGitFolders.Repository(h.WorkFolder, "main");
        var worktree = FakeGitFolders.Worktree(h.WorkFolder, "brisk-otter");
        var tab = await h.OpenTabAsync();
        var files = new[] { Path.Combine(h.WorkFolder, "src", "a.cs"), Path.Combine(worktree, "src", "b.cs") };
        for (var i = 0; i < files.Length; i++)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(files[i])!);
            await System.IO.File.WriteAllTextAsync(files[i], "b\n", TestContext.Current.CancellationToken);
            EmitEdit(h, $"e{i}", files[i]);
        }

        await TabTestHarness.Eventually(() => Read(() => tab.ChangedFiles.Rows.Count) == 6, "the working trees' trees");

        Assert.Equal(
        [
            "work · main (1 file, +1 −1) 0",
            "src (1 file, +1 −1) 1",
            "a.cs 2",
            "brisk-otter · worktree-brisk-otter (1 file, +1 −1) 0",
            "src (1 file, +1 −1) 1",
            "b.cs 2",
        ], Shown(tab));
        var heading = Folders(tab).First(f => f.Name.StartsWith("brisk-otter", StringComparison.Ordinal));
        Assert.True(heading.IsWorkTree);
        Assert.Equal(worktree, heading.Path);
        // The list's headings are rows of their own in the tree.
        Assert.All(tab.ChangedFiles.Files, f => Assert.False(f.ShowsGroup));

        h.OnUiThread(() => tab.ChangedFiles.ToggleFolderCommand.Execute(heading));

        Assert.Equal(["work · main", "src", "a.cs", "brisk-otter · worktree-brisk-otter"], Names(tab));
        Assert.Equal(["worktree:" + worktree], tab.State.CollapsedChangedFolders);
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------------

    /// <summary>Claude edits each file, by its path in the work folder, and the tree shows them all.</summary>
    private static async Task EditAsync(TabTestHarness h, TabViewModel tab, IEnumerable<string> relative)
    {
        var before = Read(() => tab.ChangedFiles.Files.Count);
        var paths = relative.Select(r => Path.Combine([h.WorkFolder, .. r.Split('/')])).ToList();
        foreach (var path in paths)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await System.IO.File.WriteAllTextAsync(path, "b\n", TestContext.Current.CancellationToken);
            EmitEdit(h, $"e{Guid.NewGuid():N}", path);
        }
        await TabTestHarness.Eventually(
            () => Read(() => tab.ChangedFiles.Files.Count == before + paths.Count && tab.ChangedFiles.Files.All(f => f.Stats is not null)
                && tab.ChangedFiles.Rows.OfType<ChangedFileRow>().Count() + tab.ChangedFiles.Rows.OfType<ChangedFolderRow>().Where(r => r.IsCollapsed).Sum(r => r.Files.Count) == before + paths.Count),
            "the changed files in the tree");
    }

    private static T Read<T>(Func<T> read) => InlineDispatcher.Read(read);

    /// <summary>Each row shown, with a folder's count and counts, and its depth.</summary>
    private static List<string> Shown(TabViewModel tab) => Read(() => tab.ChangedFiles.Rows.Select(r => r switch
    {
        ChangedFolderRow f => $"{f.Name} ({f.FilesText}, {f.Stats}) {f.Depth}",
        ChangedFileRow f => $"{f.FileName} {f.Depth}",
        _ => "",
    }).ToList());

    private static List<string> Names(TabViewModel tab) => Read(() => tab.ChangedFiles.Rows.Select(r => r switch
    {
        ChangedFolderRow f => f.Name,
        ChangedFileRow f => f.FileName,
        _ => "",
    }).ToList());

    private static List<ChangedFolderRow> Folders(TabViewModel tab) => Read(() => tab.ChangedFiles.Rows.OfType<ChangedFolderRow>().ToList());

    private static ChangedFolderRow Folder(TabViewModel tab, string name) => Folders(tab).Single(f => f.Name == name);

    private static ChangedFileRow File(TabViewModel tab, string name) => Read(() => tab.ChangedFiles.Files.Single(f => f.FileName == name));

    /// <summary>A successful Edit of <paramref name="path"/>, from "a" to "b".</summary>
    private static void EmitEdit(TabTestHarness h, string id, string path)
    {
        h.Transport.Emit(new JsonObject
        {
            ["type"] = "assistant",
            ["message"] = new JsonObject { ["content"] = new JsonArray(EditUse(id, path)) },
        });
        h.Transport.Emit(new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = id, ["content"] = "The file has been updated." }) },
            ["tool_use_result"] = EditResult(path),
        });
    }

    /// <summary>A successful Edit of <paramref name="path"/>, as a transcript has it.</summary>
    private static string[] TranscriptEdit(string id, string path) =>
    [
        Wire.Entry("assistant", "2026-09-28T11:00:00Z", Wire.Message(EditUse(id, path))),
        Wire.Entry("user", "2026-09-28T11:00:01Z", Wire.ResultMessage(id, "The file has been updated."), EditResult(path)),
    ];

    private static JsonObject EditUse(string id, string path) => new()
    {
        ["type"] = "tool_use",
        ["id"] = id,
        ["name"] = "Edit",
        ["input"] = new JsonObject { ["file_path"] = path, ["old_string"] = "a", ["new_string"] = "b" },
    };

    private static JsonObject EditResult(string path) => new() { ["filePath"] = path, ["oldString"] = "a", ["newString"] = "b", ["originalFile"] = "a\n" };
}
