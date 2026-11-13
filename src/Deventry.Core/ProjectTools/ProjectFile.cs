using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Deventry.Core.Files;
using Deventry.Core.Json;

namespace Deventry.Core.ProjectTools;

/// <summary>What a folder's project files hold, both merged: the shared file's entries, then the local file's.</summary>
/// <param name="Actions">The actions for this OS, in order. <see cref="ProjectFile.Read"/> also leaves out those whose <c>ifExists</c> is missing.</param>
/// <param name="Problems">Why entries (or a whole file) were skipped, such as "deventry.json: actions[2] has no command".</param>
/// <param name="Icon">The picture the folder's group shows in place of its colored dot; null keeps the dot.</param>
/// <param name="Color">The folder's group's color, <c>#RRGGBB</c>; null leaves the one remembered on this computer.</param>
/// <param name="Solution">The project's own solution, or another file that defines it, which its menu opens; null for none.</param>
public sealed record ProjectFileContents(
    IReadOnlyList<CustomProjectAction> Actions, IReadOnlyList<ProjectLink> Links, IReadOnlyList<string> Problems, ProjectIcon? Icon = null, string? Color = null,
    ProjectSolution? Solution = null)
{
    public static ProjectFileContents Empty { get; } = new([], [], []);

    public bool IsEmpty => Actions.Count == 0 && Links.Count == 0 && Problems.Count == 0 && Icon is null && Color is null && Solution is null;
}

/// <summary>
/// A project's <c>icon</c> (DESIGN.md §4, "Grouped by folder"): the picture its group shows in the sidebar in place of
/// its colored dot.
/// </summary>
/// <param name="Path">As the file has it: from the project's folder (<c>Assets/logo.png</c>), or a full path.</param>
/// <param name="Scope">The file it's in. The local file's wins.</param>
/// <param name="FullPath">Where the picture is, once <see cref="ProjectFile.Read"/> has found it there and fit to show.</param>
public sealed record ProjectIcon(string Path, ProjectFileScope Scope, string? FullPath = null);

/// <summary>One entry of a file's <c>actions</c>, as the editor sees it: the JSON as written, and what it reads as.</summary>
/// <param name="Raw">The entry as it is in the file. Saving keeps its other fields, such as <c>os</c>.</param>
/// <param name="Action">What it reads as; null when it can't be read.</param>
/// <param name="Problem">Why it can't be read.</param>
/// <param name="ForThisOS">Its <c>os</c> includes this machine's.</param>
/// <param name="Note">Something it has that was left out, such as an icon Deventry doesn't have, while the rest is read.</param>
public sealed record ProjectFileEntry(JsonNode? Raw, CustomProjectAction? Action, string? Problem, bool ForThisOS, string? Note = null);

/// <summary>One entry of a file's <c>links</c>, as the Links page of Settings sees it (DESIGN.md §18, "Links").</summary>
/// <param name="Raw">The entry as it is in the file. Saving keeps its other fields.</param>
/// <param name="Link">What it reads as; null when it can't be read.</param>
/// <param name="Problem">Why it can't be read.</param>
/// <param name="Note">Something it has that was left out, such as an icon Deventry doesn't have, while the rest is read.</param>
public sealed record ProjectFileLinkEntry(JsonNode? Raw, ProjectLink? Link, string? Problem, string? Note = null)
{
    /// <summary>The name as written: empty when the entry has none, and the sidebar shows its address instead.</summary>
    public string GivenName => LenientJson.String(Raw, "name")?.Trim() ?? "";
}

/// <summary>One file's <c>tickets</c>, as they're written (DESIGN.md §18, "Tickets").</summary>
/// <param name="File">The file's name, for its problems.</param>
/// <param name="HasKey">The file has <c>tickets</c>, even an empty list: it names the project's sources, so none is found from git.</param>
/// <param name="Problems">Why the file, or its list, couldn't be read.</param>
public sealed record ProjectFileTickets(string File, IReadOnlyList<JsonNode?> Entries, bool HasKey, IReadOnlyList<string> Problems);

/// <summary>
/// <c>deventry.json</c> and <c>deventry.local.json</c> in a tab's folder (DESIGN.md §18, "deventry.json"): the
/// folder's own actions and links. The shared file is committed with the project; the local one is the user's.
/// Both are read tolerantly: comments and trailing commas are fine, unknown fields are ignored, and a bad entry is
/// skipped with a reason rather than failing the file. A missing file means nothing.
/// <code>
/// {
///   "icon": "Assets/logo.png",
///   "color": "#4A6FC4",
///   "solution": "NightOwl.sln",
///   "actions": [
///     { "name": "Run tests", "command": "dotnet test", "folder": "src", "mode": "output", "group": "Build", "icon": "test" },
///     { "name": "Open Grafana", "command": "start https://grafana", "mode": "launch", "os": ["windows"] },
///     { "name": "Play", "command": "Build\\Game.exe", "mode": "launch", "ifExists": "Build/Game.exe" }
///   ],
///   "links": [
///     { "name": "Pull request", "url": "https://github.com/org/repo/compare/{branch}?expand=1", "group": "Review" }
///   ]
/// }
/// </code>
/// </summary>
public static class ProjectFile
{
    public const string SharedName = "deventry.json";

    public const string LocalName = "deventry.local.json";

    private const string ObjectShape = "{ \"actions\": [ … ], \"links\": [ … ] }";

    /// <summary>Indented, and without escaping characters such as <c>&amp;</c> that commands are full of.</summary>
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The pictures an <c>icon</c> can be: what Skia decodes on every platform.</summary>
    public static IReadOnlySet<string> IconExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".ico", ".jpg", ".jpeg", ".bmp", ".gif", ".webp",
    };

    /// <summary>An icon shows 14 pixels across, so a picture over 1 MB is far more than it needs.</summary>
    public const long IconMaxBytes = 1024 * 1024;

    public static string FileName(ProjectFileScope scope) => scope == ProjectFileScope.Shared ? SharedName : LocalName;

    /// <summary>
    /// Where <paramref name="folder"/>'s file is, or is to be written: <c>deventry.json</c> or <c>deventry.local.json</c>,
    /// or Deventry's name for it in a project that has only that.
    /// </summary>
    public static string PathFor(string folder, ProjectFileScope scope)
    {
        var path = Path.Combine(folder, FileName(scope));
        // FromClaudette: a project's deventry.json, read and written in its place while it has no deventry.json.
        return FromClaudette.ClaudetteProjectFiles.InPlaceOf(folder, scope, path) ?? path;
    }

    /// <summary>The name of <paramref name="folder"/>'s file, as <see cref="PathFor"/> finds it.</summary>
    public static string FileName(string folder, ProjectFileScope scope) => Path.GetFileName(PathFor(folder, scope));

    /// <summary>
    /// Both files of <paramref name="folder"/>, the shared one first. Actions whose <c>ifExists</c> paths aren't all
    /// there are left out; they're checked again each time the files are read.
    /// </summary>
    public static ProjectFileContents Read(string folder, ToolOS os)
    {
        var sharedPath = PathFor(folder, ProjectFileScope.Shared);
        var localPath = PathFor(folder, ProjectFileScope.Local);
        var sharedName = Path.GetFileName(sharedPath);
        var localName = Path.GetFileName(localPath);
        var shared = Parse(ReadText(sharedPath, out var sharedError), ProjectFileScope.Shared, os, sharedName);
        var local = Parse(ReadText(localPath, out var localError), ProjectFileScope.Local, os, localName);
        // The local file's icon wins; one that can't be shown says why, and the group keeps its dot.
        ProjectIcon? icon = null;
        string[] iconProblem = [];
        if ((local.Icon ?? shared.Icon) is { } written)
        {
            var (fullPath, problem) = ResolveIcon(folder, written.Path);
            icon = fullPath is null ? null : written with { FullPath = fullPath };
            iconProblem = problem is null ? [] : [$"{(written.Scope == ProjectFileScope.Local ? localName : sharedName)}: {problem}"];
        }
        return new ProjectFileContents(
            [.. shared.Actions.Concat(local.Actions).Where(a => a.IsShownIn(folder))],
            [.. shared.Links, .. local.Links],
            [.. Error(sharedName, sharedError), .. shared.Problems, .. Error(localName, localError), .. local.Problems, .. iconProblem],
            icon,
            local.Color ?? shared.Color,
            local.Solution ?? shared.Solution);

        static IEnumerable<string> Error(string file, string? error) => error is null ? [] : [$"{file} couldn't be read: {error}"];
    }

    /// <summary>One file's text; null (no file) reads as nothing.</summary>
    /// <param name="fileName">The file's name, for its problems; <see cref="FileName(ProjectFileScope)"/> by default.</param>
    public static ProjectFileContents Parse(string? text, ProjectFileScope scope, ToolOS os, string? fileName = null)
    {
        if (text is null)
        {
            return ProjectFileContents.Empty;
        }
        var file = fileName ?? FileName(scope);
        var problems = new List<string>();
        if (!TryParseRoot(text, out var root, out var error))
        {
            return new ProjectFileContents([], [], [error is null
                ? $"{file} isn't a JSON object, so it was skipped. It should look like {ObjectShape}."
                : $"{file} isn't valid JSON, so it was skipped: {error}"]);
        }
        var actions = new List<CustomProjectAction>();
        foreach (var entry in Entries(root, "actions", file, problems).Select((node, index) => ReadAction(node, index, scope, os)))
        {
            if (entry.Problem is { } problem)
            {
                problems.Add($"{file}: {problem}");
            }
            else if (entry.ForThisOS && entry.Action is { } action)
            {
                actions.Add(action);
                if (entry.Note is { } note)
                {
                    problems.Add($"{file}: {note}");
                }
            }
        }
        var links = new List<ProjectLink>();
        foreach (var entry in Entries(root, "links", file, problems).Select((node, index) => ReadLink(node, index, scope)))
        {
            if (entry.Link is { } link)
            {
                links.Add(link);
                if (entry.Note is { } note)
                {
                    problems.Add($"{file}: {note}");
                }
            }
            else
            {
                problems.Add($"{file}: {entry.Problem}");
            }
        }
        ProjectIcon? icon = null;
        if (root.TryGetPropertyValue("icon", out var iconNode) && iconNode is not null)
        {
            if (LenientJson.String(root, "icon")?.Trim() is { Length: > 0 } path)
            {
                icon = new ProjectIcon(path, scope);
            }
            else
            {
                problems.Add($"{file}: \"icon\" should be a picture's path, such as \"Assets/logo.png\", so it was skipped.");
            }
        }
        string? color = null;
        if (root.TryGetPropertyValue("color", out var colorNode) && colorNode is not null)
        {
            color = NormalizeColor(LenientJson.String(root, "color"));
            if (color is null)
            {
                problems.Add($"{file}: \"color\" should be a color such as \"#4A6FC4\", so it was skipped.");
            }
        }
        ProjectSolution? solution = null;
        if (root.TryGetPropertyValue("solution", out var solutionNode) && solutionNode is not null)
        {
            if (LenientJson.String(root, "solution")?.Trim() is { Length: > 0 } path)
            {
                solution = new ProjectSolution(path, scope);
            }
            else
            {
                problems.Add($"{file}: \"solution\" should be a file's path, such as \"NightOwl.sln\", so it was skipped.");
            }
        }
        return new ProjectFileContents(actions, links, problems, icon, color, solution);
    }

    /// <summary>A color as it's written and kept, <c>#RRGGBB</c>, from <c>#RRGGBB</c> or <c>#RGB</c>, with or without the <c>#</c>; null when it isn't one.</summary>
    public static string? NormalizeColor(string? text)
    {
        var hex = text?.Trim().TrimStart('#') ?? "";
        if (hex.Length is not (3 or 6) || !hex.All(char.IsAsciiHexDigit))
        {
            return null;
        }
        return "#" + (hex.Length == 3 ? string.Concat(hex.Select(c => $"{c}{c}")) : hex).ToUpperInvariant();
    }

    /// <summary>
    /// Where an <c>icon</c> is, read from <paramref name="folder"/>, or why it can't be the group's: not a picture Deventry
    /// shows, not there, too big, or on a network share, which is never looked at by itself, since Windows would hand its
    /// server the user's credentials and a cloned project's file could name any.
    /// </summary>
    public static (string? FullPath, string? Problem) ResolveIcon(string folder, string icon)
    {
        var network = $"icon {icon} is on a network share, which Deventry doesn't look at by itself.";
        if (Protocol.ReplyFileReference.IsNetworkPath(icon))
        {
            return (null, network);
        }
        string full;
        try
        {
            full = Path.GetFullPath(Path.Combine(folder, icon));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return (null, $"icon {icon} isn't a path.");
        }
        if (Protocol.ReplyFileReference.IsNetworkPath(full))
        {
            return (null, network);
        }
        if (!IconExtensions.Contains(Path.GetExtension(full)))
        {
            return (null, $"icon {icon} isn't a PNG, ICO, JPEG, BMP, GIF or WebP picture.");
        }
        try
        {
            var info = new FileInfo(full);
            if (!info.Exists)
            {
                return (null, $"icon {icon} isn't there.");
            }
            if (info.Length > IconMaxBytes)
            {
                return (null, $"icon {icon} is over 1 MB, far more than an icon needs.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, $"icon {icon} couldn't be read: {ex.Message}");
        }
        return (full, null);
    }

    /// <summary>
    /// How a picture chosen for the icon is written: from the project's folder, with forward slashes, so it's the same on
    /// every machine; a picture elsewhere by its full path, which only this computer has.
    /// </summary>
    public static string IconPathFor(string folder, string picked)
    {
        var relative = Path.GetRelativePath(folder, picked);
        return relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? picked : relative.Replace('\\', '/');
    }

    /// <summary>How a file chosen for the <c>solution</c> is written: as <see cref="IconPathFor"/> writes a picture.</summary>
    public static string SolutionPathFor(string folder, string picked) => IconPathFor(folder, picked);

    /// <summary>One file's <c>solution</c> as it has it, for the project's Solution page; null when it has none.</summary>
    /// <exception cref="InvalidOperationException">The file exists but isn't JSON.</exception>
    public static string? ReadSolution(string folder, ProjectFileScope scope) => ReadValue(folder, scope, "solution");

    /// <summary>Sets one file's <c>solution</c>, or takes it out when null, as <see cref="WriteIcon"/> does.</summary>
    /// <exception cref="InvalidOperationException">The file exists but isn't JSON.</exception>
    public static void WriteSolution(string folder, ProjectFileScope scope, string? solution) => WriteValue(folder, scope, "solution", solution);

    /// <summary>One file's <c>icon</c> as it has it, for Settings' Group page; null when it has none.</summary>
    /// <exception cref="InvalidOperationException">The file exists but isn't JSON.</exception>
    public static string? ReadIcon(string folder, ProjectFileScope scope) => ReadValue(folder, scope, "icon");

    /// <summary>
    /// Sets one file's <c>icon</c>, or takes it out when <paramref name="icon"/> is null, keeping its other keys, as
    /// <see cref="WriteActions"/> does. Creates the file for an icon when there's none.
    /// </summary>
    /// <exception cref="InvalidOperationException">The file exists but isn't JSON.</exception>
    public static void WriteIcon(string folder, ProjectFileScope scope, string? icon) => WriteValue(folder, scope, "icon", icon);

    /// <summary>One file's <c>color</c>, as <c>#RRGGBB</c>; null when it has none, or none that's a color.</summary>
    /// <exception cref="InvalidOperationException">The file exists but isn't JSON.</exception>
    public static string? ReadColor(string folder, ProjectFileScope scope) => NormalizeColor(ReadValue(folder, scope, "color"));

    /// <summary>Sets one file's <c>color</c> (<c>#RRGGBB</c>), or takes it out when null, as <see cref="WriteIcon"/> does.</summary>
    /// <exception cref="InvalidOperationException">The file exists but isn't JSON.</exception>
    public static void WriteColor(string folder, ProjectFileScope scope, string? color) =>
        WriteValue(folder, scope, "color", color is null ? null : NormalizeColor(color) ?? throw new ArgumentException($"{color} isn't a color.", nameof(color)));

    private static string? ReadValue(string folder, ProjectFileScope scope, string key) =>
        ReadRoot(folder, scope) is { } root && LenientJson.String(root, key)?.Trim() is { Length: > 0 } value ? value : null;

    private static void WriteValue(string folder, ProjectFileScope scope, string key, string? value)
    {
        var root = ReadRoot(folder, scope);
        if (value is null && root is null)
        {
            return;
        }
        root ??= [];
        if (value is null)
        {
            root.Remove(key);
        }
        else
        {
            root[key] = value;
        }
        AtomicFile.WriteAllText(PathFor(folder, scope), root.ToJsonString(WriteOptions) + Environment.NewLine);
    }

    /// <summary>Every entry of one file's <c>actions</c>, for the editor, including ones for other OSes and ones it can't read.</summary>
    /// <exception cref="InvalidOperationException">The file exists but isn't JSON: it can't be edited here without losing it.</exception>
    public static IReadOnlyList<ProjectFileEntry> ReadEntries(string folder, ProjectFileScope scope, ToolOS os) =>
        ReadRoot(folder, scope) is { } root
            ? Entries(root, "actions", FileName(folder, scope), []).Select((node, index) => ReadAction(node, index, scope, os)).ToArray()
            : [];

    /// <summary>Every entry of one file's <c>links</c>, for Settings' Links page, including ones it can't read.</summary>
    /// <exception cref="InvalidOperationException">The file exists but isn't JSON: it can't be edited here without losing it.</exception>
    public static IReadOnlyList<ProjectFileLinkEntry> ReadLinkEntries(string folder, ProjectFileScope scope) =>
        ReadRoot(folder, scope) is { } root
            ? Entries(root, "links", FileName(folder, scope), []).Select((node, index) => ReadLink(node, index, scope)).ToArray()
            : [];

    /// <summary>
    /// Replaces one file's <c>actions</c> with <paramref name="entries"/>, keeping its other keys, and writes it
    /// indented. Comments in the file are lost: JSON can't keep them. Creates the file when there's none.
    /// </summary>
    /// <exception cref="InvalidOperationException">The file exists but isn't JSON.</exception>
    public static void WriteActions(string folder, ProjectFileScope scope, IEnumerable<JsonNode?> entries) => WriteList(folder, scope, "actions", entries);

    /// <summary>
    /// Replaces one file's <c>links</c> with <paramref name="entries"/>, keeping its other keys (its actions among them),
    /// and writes it indented, as <see cref="WriteActions"/> does. Creates the file when there's none.
    /// </summary>
    /// <exception cref="InvalidOperationException">The file exists but isn't JSON.</exception>
    public static void WriteLinks(string folder, ProjectFileScope scope, IEnumerable<JsonNode?> entries) => WriteList(folder, scope, "links", entries);

    /// <summary>
    /// One file's <c>tickets</c> (DESIGN.md §18, "Tickets"): its entries as they're written, and whether the file has
    /// the key at all, since an empty list turns finding a source from git off. Never throws: a file that can't be read
    /// or isn't JSON has none, with why.
    /// </summary>
    public static ProjectFileTickets ReadTickets(string folder, ProjectFileScope scope)
    {
        var path = PathFor(folder, scope);
        var file = Path.GetFileName(path);
        var text = ReadText(path, out var error);
        if (error is not null)
        {
            return new ProjectFileTickets(file, [], false, [$"{file} couldn't be read: {error}"]);
        }
        if (text is null)
        {
            return new ProjectFileTickets(file, [], false, []);
        }
        if (!TryParseRoot(text, out var root, out var parseError))
        {
            return new ProjectFileTickets(file, [], false, [parseError is null
                ? $"{file} isn't a JSON object, so its tickets were skipped."
                : $"{file} isn't valid JSON, so its tickets were skipped: {parseError}"]);
        }
        var problems = new List<string>();
        var entries = Entries(root, "tickets", file, problems).ToArray();
        return new ProjectFileTickets(file, entries, root.ContainsKey("tickets"), problems);
    }

    /// <summary>Every entry of one file's <c>tickets</c>, for the project's settings, including ones that can't be read.</summary>
    /// <exception cref="InvalidOperationException">The file exists but isn't JSON: it can't be edited here without losing it.</exception>
    public static IReadOnlyList<JsonNode?> ReadTicketEntries(string folder, ProjectFileScope scope) =>
        ReadRoot(folder, scope) is { } root ? [.. Entries(root, "tickets", FileName(folder, scope), [])] : [];

    /// <summary>
    /// Replaces one file's <c>tickets</c> with <paramref name="entries"/>, keeping its other keys, and writes it indented,
    /// as <see cref="WriteLinks"/> does. Creates the file when there's none.
    /// </summary>
    /// <exception cref="InvalidOperationException">The file exists but isn't JSON.</exception>
    public static void WriteTickets(string folder, ProjectFileScope scope, IEnumerable<JsonNode?> entries) => WriteList(folder, scope, "tickets", entries);

    private static void WriteList(string folder, ProjectFileScope scope, string key, IEnumerable<JsonNode?> entries)
    {
        var root = ReadRoot(folder, scope) ?? [];
        root[key] = new JsonArray([.. entries.Select(e => e?.DeepClone())]);
        AtomicFile.WriteAllText(PathFor(folder, scope), root.ToJsonString(WriteOptions) + Environment.NewLine);
    }

    /// <summary>One file's root object, to edit; null when there's no file.</summary>
    /// <exception cref="InvalidOperationException">The file can't be read, or isn't JSON.</exception>
    private static JsonObject? ReadRoot(string folder, ProjectFileScope scope)
    {
        var text = ReadText(PathFor(folder, scope), out var error);
        if (error is not null)
        {
            throw new InvalidOperationException($"{FileName(folder, scope)} couldn't be read: {error}");
        }
        if (text is null)
        {
            return null;
        }
        if (!TryParseRoot(text, out var root, out var parseError))
        {
            throw new InvalidOperationException(parseError is null
                ? $"{FileName(folder, scope)} isn't a JSON object, so Deventry won't rewrite it. Fix it by hand first."
                : $"{FileName(folder, scope)} isn't valid JSON ({parseError}), so Deventry won't rewrite it. Fix it by hand first.");
        }
        return root;
    }

    /// <summary>An action as an entry of <c>actions</c>: <paramref name="raw"/>'s other fields kept, its own written over them.</summary>
    public static JsonObject ToJson(CustomProjectAction action, JsonNode? raw = null)
    {
        var json = raw is JsonObject existing ? (JsonObject)existing.DeepClone() : [];
        json["name"] = action.Name;
        json["command"] = action.Command;
        if (string.IsNullOrWhiteSpace(action.WorkingFolder))
        {
            json.Remove("folder");
        }
        else
        {
            json["folder"] = action.WorkingFolder.Trim();
        }
        switch (action.IfExists)
        {
            case null or []:
                json.Remove("ifExists");
                break;
            case [var one]:
                json["ifExists"] = one;
                break;
            case var several:
                json["ifExists"] = new JsonArray([.. several.Select(p => (JsonNode?)p)]);
                break;
        }
        json["mode"] = action.Mode == CustomActionMode.LaunchAndForget ? "launch" : "output";
        if (action.RunOnNewWorktree)
        {
            json["runOnNewWorktree"] = true;
        }
        else
        {
            json.Remove("runOnNewWorktree");
        }
        SetGroupAndIcon(json, action.Group, action.Icon);
        return json;
    }

    /// <summary>Writes <paramref name="value"/> as <paramref name="key"/>, or leaves the key out when it's empty.</summary>
    private static void SetOrRemove(JsonObject json, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            json.Remove(key);
        }
        else
        {
            json[key] = value.Trim();
        }
    }

    /// <summary>
    /// Writes an entry's <c>"group"</c> and <c>"icon"</c>, but leaves each as written while it still reads as
    /// <paramref name="group"/> and <paramref name="icon"/>: an icon a newer Deventry has, which reads as none here, isn't
    /// lost when another entry of the file is edited or moved.
    /// </summary>
    private static void SetGroupAndIcon(JsonObject json, string? group, string? icon)
    {
        if (!json.TryGetPropertyValue("group", out var writtenGroup) || ReadGroup(writtenGroup).Group != group)
        {
            SetOrRemove(json, "group", group);
        }
        if (!json.TryGetPropertyValue("icon", out var writtenIcon) || MenuIcons.Find(ReadString(writtenIcon)) != icon)
        {
            SetOrRemove(json, "icon", icon);
        }
    }

    /// <summary>A <c>"group"</c> as written: its name, null for none, and whether it's a name at all.</summary>
    private static (string? Group, bool IsName) ReadGroup(JsonNode? node) =>
        ReadString(node) is { } text ? (text.Trim() is { Length: > 0 } trimmed ? trimmed : null, true) : (null, node is null);

    private static string? ReadString(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>
    /// A link as an entry of <c>links</c>: <paramref name="raw"/>'s other fields kept, its name, address, group and icon
    /// written over them. An empty name is left out, and the menu shows the address instead; so are an empty group and
    /// icon.
    /// </summary>
    public static JsonObject LinkToJson(string? name, string url, JsonNode? raw = null, string? group = null, string? icon = null)
    {
        var json = raw is JsonObject existing ? (JsonObject)existing.DeepClone() : [];
        SetOrRemove(json, "name", name);
        json["url"] = url.Trim();
        SetGroupAndIcon(json, group, icon);
        return json;
    }

    /// <summary>Does an <c>os</c> list include <paramref name="os"/>? No list means every OS.</summary>
    public static bool AppliesTo(IReadOnlyList<string>? list, ToolOS os) =>
        list is null || list.Any(name => name.Trim().ToLowerInvariant() switch
        {
            "windows" or "win" or "win64" => os == ToolOS.Windows,
            "macos" or "mac" or "osx" or "darwin" => os == ToolOS.MacOS,
            "linux" => os == ToolOS.Linux,
            _ => false,
        });

    private static ProjectFileEntry ReadAction(JsonNode? node, int index, ProjectFileScope scope, ToolOS os)
    {
        var where = $"actions[{index}]";
        if (node is not JsonObject entry)
        {
            return new ProjectFileEntry(node, null, $"{where} isn't an object, so it was skipped.", true);
        }
        var name = LenientJson.String(entry, "name")?.Trim();
        var command = LenientJson.String(entry, "command")?.Trim();
        var forThisOS = true;
        IReadOnlyList<string>? osList = null;
        if (entry["os"] is JsonNode osNode)
        {
            osList = StringList(osNode);
            if (osList is null)
            {
                return new ProjectFileEntry(node, null, $"{where}: \"os\" should be a list such as [\"windows\", \"macos\"], so it was skipped.", true);
            }
            forThisOS = AppliesTo(osList, os);
        }
        if (string.IsNullOrEmpty(name))
        {
            return new ProjectFileEntry(node, null, $"{where} has no name, so it was skipped.", forThisOS);
        }
        if (string.IsNullOrEmpty(command))
        {
            return new ProjectFileEntry(node, null, $"{where} (\"{name}\") has no command, so it was skipped.", forThisOS);
        }
        var modeText = LenientJson.String(entry, "mode")?.Trim().ToLowerInvariant();
        CustomActionMode? mode = modeText switch
        {
            null or "" or "output" => CustomActionMode.RunWithOutput,
            "launch" => CustomActionMode.LaunchAndForget,
            _ => null,
        };
        if (mode is null)
        {
            return new ProjectFileEntry(node, null, $"{where} (\"{name}\") has mode \"{modeText}\", which isn't \"output\" or \"launch\", so it was skipped.", forThisOS);
        }
        IReadOnlyList<string>? ifExists = null;
        if (entry["ifExists"] is JsonNode existsNode)
        {
            if (StringList(existsNode) is not { } paths)
            {
                return new ProjectFileEntry(node, null, $"{where} (\"{name}\"): \"ifExists\" should be a path or a list of paths, such as \"Build/Game.exe\", so it was skipped.", forThisOS);
            }
            var given = paths.Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
            ifExists = given.Length > 0 ? given : null;
        }
        var folder = LenientJson.String(entry, "folder")?.Trim();
        var (group, icon, note) = GroupAndIcon(entry, $"{where} (\"{name}\")");
        var action = new CustomProjectAction
        {
            Id = $"{(scope == ProjectFileScope.Shared ? "shared" : "local")}:{index}",
            Scope = scope,
            Name = name,
            Command = command,
            WorkingFolder = string.IsNullOrEmpty(folder) ? null : folder,
            Mode = mode.Value,
            Os = osList,
            IfExists = ifExists,
            // Anything but true leaves it to a click.
            RunOnNewWorktree = entry["runOnNewWorktree"] is JsonValue run && run.TryGetValue<bool>(out var on) && on,
            Group = group,
            Icon = icon,
        };
        return new ProjectFileEntry(node, action, null, forThisOS, note);
    }

    /// <summary>
    /// An entry's <c>"group"</c> and <c>"icon"</c> (DESIGN.md §18, "deventry.json"). One that can't be used is left out,
    /// with a note, and the entry is read without it: at the top of its submenu, with its usual icon.
    /// </summary>
    private static (string? Group, string? Icon, string? Note) GroupAndIcon(JsonObject entry, string where)
    {
        var notes = new List<string>();
        var (group, isName) = ReadGroup(entry["group"]);
        if (!isName)
        {
            notes.Add("\"group\" should be a name, such as \"Deploy\"");
        }
        string? icon = null;
        if (entry["icon"] is JsonNode iconNode)
        {
            var written = ReadString(iconNode)?.Trim();
            icon = MenuIcons.Find(written);
            if (icon is null && written is not { Length: 0 })
            {
                notes.Add(written is null
                    ? "\"icon\" should be the name of one of Deventry's icons, such as \"play\""
                    : $"\"icon\" is \"{written}\", which isn't one of Deventry's icons ({string.Join(", ", MenuIcons.Names)})");
            }
        }
        return (group, icon, notes.Count == 0 ? null : $"{where}: {string.Join("; ", notes)}, so it shows without it.");
    }

    /// <summary>A string or a list of strings, such as <c>"os"</c>; null when it's neither. Other values in a list are ignored.</summary>
    private static IReadOnlyList<string>? StringList(JsonNode node) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var one) => [one],
        JsonArray array => array.Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).OfType<string>().ToArray(),
        _ => null,
    };

    private static ProjectFileLinkEntry ReadLink(JsonNode? node, int index, ProjectFileScope scope)
    {
        var where = $"links[{index}]";
        if (node is not JsonObject entry)
        {
            return new ProjectFileLinkEntry(node, null, $"{where} isn't an object, so it was skipped.");
        }
        var url = LenientJson.String(entry, "url")?.Trim();
        if (string.IsNullOrEmpty(url))
        {
            return new ProjectFileLinkEntry(node, null, $"{where} has no url, so it was skipped.");
        }
        var name = LenientJson.String(entry, "name")?.Trim();
        var shown = string.IsNullOrEmpty(name) ? url : name;
        var (group, icon, note) = GroupAndIcon(entry, $"{where} (\"{shown}\")");
        return new ProjectFileLinkEntry(node, new ProjectLink(shown, url, scope, group, icon), null, note);
    }

    private static IEnumerable<JsonNode?> Entries(JsonObject root, string name, string file, List<string> problems)
    {
        if (!root.TryGetPropertyValue(name, out var node) || node is null)
        {
            return [];
        }
        if (node is JsonArray array)
        {
            return array;
        }
        problems.Add($"{file}: \"{name}\" should be a list, so it was skipped.");
        return [];
    }

    /// <summary>The file's object; when it has none, <paramref name="error"/> is the parser's reason, or null for JSON that isn't an object.</summary>
    private static bool TryParseRoot(string text, out JsonObject root, out string? error)
    {
        root = [];
        error = null;
        if (text.Trim().TrimStart('﻿').Length == 0)
        {
            return true;
        }
        try
        {
            var node = JsonTree.Parse(text.TrimStart('﻿'), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (node is JsonObject obj)
            {
                root = obj;
                return true;
            }
            return false;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>A file's text; null when there's no file, with <paramref name="error"/> set when it can't be read.</summary>
    private static string? ReadText(string path, out string? error)
    {
        error = null;
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return null;
        }
    }
}
