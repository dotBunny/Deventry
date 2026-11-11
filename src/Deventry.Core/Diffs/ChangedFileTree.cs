namespace Deventry.Core.Diffs;

/// <summary>
/// A folder of the Changed files tree (DESIGN.md §8, "The tree"), with the folders and files in it, each in the order
/// the tree shows them.
/// </summary>
/// <param name="Name">
/// What its row says: its name, or a chain of folders that each hold only the next, <i>Source/Starfall</i>. A folder
/// outside the root is named by its full path, with <c>/</c> between its folders. Empty for the root.
/// </param>
/// <param name="Path">The full path of the last folder its row names: the folder that holds <see cref="Folders"/> and <see cref="Files"/>.</param>
/// <param name="IsOutside">It's outside the root, so its name is its full path.</param>
public sealed record ChangedFolder<T>(string Name, string Path, bool IsOutside, IReadOnlyList<ChangedFolder<T>> Folders, IReadOnlyList<T> Files)
{
    /// <summary>Every file in it and in the folders under it, in the tree's order.</summary>
    public IEnumerable<T> AllFiles() => Folders.SelectMany(f => f.AllFiles()).Concat(Files);

    /// <summary>It and every folder under it, top down.</summary>
    public IEnumerable<ChangedFolder<T>> AllFolders() => Folders.SelectMany(f => f.AllFolders()).Prepend(this);
}

/// <summary>
/// Builds the Changed files tree (DESIGN.md §8, "The tree"): the files by their folders, from the paths shown for them.
/// A folder that holds only one other folder and no files shares its row with that folder, <i>Source/Starfall</i>, down to
/// where something branches. Folders come before files, each by name; the folders outside the root, named by their full
/// paths, come after those in it.
/// </summary>
public static class ChangedFileTree
{
    /// <summary>The tree of <paramref name="files"/>: the root folder, <paramref name="root"/>, with the folders and files at its top.</summary>
    /// <param name="fullPath">A file's full path, which its folders' full paths are worked out from.</param>
    /// <param name="shownPath">
    /// Its path as the panel shows it: from the root, with <c>/</c> between folders, or its full path, with <c>/</c> too, when
    /// it's outside the root (<see cref="ChangedFiles.DisplayPath"/>).
    /// </param>
    /// <param name="comparer">How folder names compare: the platform's, by default, so <c>Src</c> and <c>src</c> are one on Windows and macOS.</param>
    public static ChangedFolder<T> Build<T>(IEnumerable<T> files, Func<T, string> fullPath, Func<T, string> shownPath, string root, StringComparer? comparer = null)
    {
        comparer ??= ChangedFiles.PlatformPathComparer;
        var top = new Node<T>("", root, isOutside: false, comparer);
        foreach (var file in files)
        {
            var parts = Parts(shownPath(file));
            var full = ChangedFiles.Normalize(fullPath(file));
            var node = top;
            for (var i = 0; i < parts.Count - 1; i++)
            {
                if (!node.Folders.TryGetValue(parts[i], out var child))
                {
                    // The folder i is the file's ancestor as many levels up as there are parts after it, less the file's own.
                    var path = Ancestor(full, parts.Count - 1 - i) ?? string.Join('/', parts.Take(i + 1));
                    child = new Node<T>(parts[i], path, node.IsOutside || i == 0 && IsRooted(parts[0]), comparer);
                    node.Folders.Add(parts[i], child);
                }
                node = child;
            }
            node.Files.Add((file, parts[^1]));
        }
        return Finish(top, top.Name);
    }

    /// <summary>A shown path's folders and name. A full path's root stays with its first folder: <c>/home</c>, <c>C:</c>.</summary>
    private static List<string> Parts(string shown)
    {
        var parts = shown.Split('/').ToList();
        var leading = "";
        while (parts.Count > 1 && parts[0].Length == 0)
        {
            leading += "/";
            parts.RemoveAt(0);
        }
        parts[0] = leading + parts[0];
        // A folder named twice over, a//b, or a path that ends in one: the empty parts aren't folders.
        return [.. parts.Where((p, i) => p.Length > 0 || i == parts.Count - 1)];
    }

    private static bool IsRooted(string first) => first.StartsWith('/') || first.EndsWith(':');

    private static string? Ancestor(string path, int levels)
    {
        string? at = path;
        for (var i = 0; i < levels && at is not null; i++)
        {
            at = Path.GetDirectoryName(at);
        }
        return string.IsNullOrEmpty(at) ? null : at;
    }

    private static ChangedFolder<T> Finish<T>(Node<T> node, string name)
    {
        var folders = node.Folders.Values
            .Select(child =>
            {
                var (at, chain) = (child, child.Name);
                while (at.Files.Count == 0 && at.Folders.Count == 1)
                {
                    at = at.Folders.Values.Single();
                    chain = $"{chain}/{at.Name}";
                }
                return Finish(at, chain);
            })
            .OrderBy(f => f.IsOutside)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Name, StringComparer.Ordinal)
            .ToList();
        var files = node.Files
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Name, StringComparer.Ordinal)
            .Select(f => f.File)
            .ToList();
        return new ChangedFolder<T>(name, node.Path, node.IsOutside, folders, files);
    }

    private sealed class Node<T>(string name, string path, bool isOutside, StringComparer comparer)
    {
        public string Name { get; } = name;

        public string Path { get; } = path;

        public bool IsOutside { get; } = isOutside;

        public Dictionary<string, Node<T>> Folders { get; } = new(comparer);

        public List<(T File, string Name)> Files { get; } = [];
    }
}
