using Deventry.Core.Diffs;

namespace Deventry.Core.Tests.Diffs;

/// <summary>The tree Changed files shows its files in (DESIGN.md §8, "The tree").</summary>
public sealed class ChangedFileTreeTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "deventry-tree", "work");

    private sealed record File(string Full, string Shown);

    /// <summary>A file in the root, shown from it as the panel shows it.</summary>
    private static File In(string shown) => new(Path.Combine([Root, .. shown.Split('/')]), shown);

    private static ChangedFolder<File> Build(IEnumerable<File> files, StringComparer? comparer = null) =>
        ChangedFileTree.Build(files, f => f.Full, f => f.Shown, Root, comparer);

    private static IEnumerable<string> Names(IEnumerable<File> files) => files.Select(f => f.Shown);

    [Fact]
    public void Files_go_under_their_folders_with_a_chain_of_single_folders_on_one_row()
    {
        var tree = Build([
            In("Source/Starfall/Player/StarfallCharacter.h"),
            In("Source/Starfall/Abilities/GrappleComponent.h"),
            In("README.md"),
            In("Source/Starfall/Tests/GrappleComponentTest.cpp"),
            In("Source/Starfall/Abilities/GrappleComponent.cpp"),
        ]);

        Assert.Equal(("", Root), (tree.Name, tree.Path));
        var starfall = Assert.Single(tree.Folders);
        Assert.Equal(("Source/Starfall", Path.Combine(Root, "Source", "Starfall"), false), (starfall.Name, starfall.Path, starfall.IsOutside));
        Assert.Equal(["README.md"], Names(tree.Files));
        // Folders by name, and each one's files by name.
        Assert.Equal(["Abilities", "Player", "Tests"], starfall.Folders.Select(f => f.Name));
        Assert.Equal(Path.Combine(Root, "Source", "Starfall", "Abilities"), starfall.Folders[0].Path);
        Assert.Equal(["Source/Starfall/Abilities/GrappleComponent.cpp", "Source/Starfall/Abilities/GrappleComponent.h"], Names(starfall.Folders[0].Files));
        Assert.Empty(starfall.Files);
    }

    [Fact]
    public void A_chain_stops_at_a_folder_with_a_file_in_it_and_goes_on_under_it()
    {
        var tree = Build([In("a/b/c/deep.txt"), In("a/top.txt"), In("x/y/z/alone.txt")]);

        Assert.Equal(["a", "x/y/z"], tree.Folders.Select(f => f.Name));
        var a = tree.Folders[0];
        Assert.Equal(["a/top.txt"], Names(a.Files));
        Assert.Equal(("b/c", Path.Combine(Root, "a", "b", "c")), (a.Folders.Single().Name, a.Folders.Single().Path));
        Assert.Equal(Path.Combine(Root, "x", "y", "z"), tree.Folders[1].Path);
    }

    [Fact]
    public void A_folder_outside_the_root_is_named_by_its_full_path_after_those_in_it()
    {
        var outside = Path.Combine(Path.GetTempPath(), "deventry-tree", "elsewhere", "notes", "todo.md");
        // Its full path, with / between folders, as the panel shows a file outside the tab's folder.
        var shown = outside.Replace('\\', '/');

        var tree = Build([new File(outside, shown), In("src/auth.cs")]);

        Assert.Equal(["src", Path.GetDirectoryName(outside)!.Replace('\\', '/')], tree.Folders.Select(f => f.Name));
        Assert.Equal((Path.GetDirectoryName(outside), true), (tree.Folders[1].Path, tree.Folders[1].IsOutside));
        Assert.Equal([shown], Names(tree.Folders[1].Files));
    }

    [Fact]
    public void Folders_whose_names_differ_only_in_case_are_one_where_the_system_says_so()
    {
        File[] files = [In("src/b.cs"), In("Src/a.cs")];

        var one = Build(files, StringComparer.OrdinalIgnoreCase);
        var two = Build(files, StringComparer.Ordinal);

        Assert.Equal(["src"], one.Folders.Select(f => f.Name));
        Assert.Equal(["Src/a.cs", "src/b.cs"], Names(one.Folders[0].Files));
        Assert.Equal(["Src", "src"], two.Folders.Select(f => f.Name));
    }

    [Fact]
    public void Every_file_and_folder_under_one_comes_in_the_trees_order()
    {
        var tree = Build([In("b.txt"), In("lib/z.cs"), In("lib/core/a.cs"), In("app/main.cs")]);

        Assert.Equal(["app/main.cs", "lib/core/a.cs", "lib/z.cs", "b.txt"], Names(tree.AllFiles()));
        Assert.Equal(["", "app", "lib", "core"], tree.AllFolders().Select(f => f.Name));
    }

    [Fact]
    public void Files_at_the_top_have_no_folder()
    {
        var tree = Build([In("login.cs"), In("auth.cs")]);

        Assert.Empty(tree.Folders);
        Assert.Equal(["auth.cs", "login.cs"], Names(tree.Files));
    }
}
