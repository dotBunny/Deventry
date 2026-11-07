using System.Text.Json.Nodes;
using Deventry.Core.ProjectTools;
using Deventry.Core.Tests.Support;

namespace Deventry.Core.Tests.ProjectTools;

/// <summary><c>deventry.json</c> and <c>deventry.local.json</c>: reading, merging, editing and links (DESIGN.md §18).</summary>
public sealed class ProjectFileTests : IDisposable
{
    private readonly TempFolder _temp = new("deventry-projectfile");

    public void Dispose() => _temp.Dispose();

    private const string Shared = """
        {
          // Committed with the project.
          "actions": [
            { "name": "Run tests", "command": "dotnet test", "folder": "src", "mode": "output", "note": "unknown fields are fine", },
            { "name": "Open Grafana", "command": "start https://grafana", "mode": "launch", "os": ["windows"] },
            { "name": "Tail logs", "command": "tail -f log.txt", "os": "linux" },
            { "command": "no name" },
            { "name": "No command" },
            { "name": "Odd mode", "command": "x", "mode": "background" },
            "not an object",
          ],
          "links": [
            { "name": "Board", "url": "https://example.atlassian.net/jira/software/projects/ABC/boards/1" },
            { "url": "https://github.com/org/repo/compare/{branch}?expand=1" },
            { "name": "No url" },
          ],
          "somethingElse": { "kept": true },
        }
        """;

    [Fact]
    public void The_shared_file_is_read_tolerantly_and_bad_entries_are_skipped_with_a_reason()
    {
        var contents = ProjectFile.Parse(Shared, ProjectFileScope.Shared, ToolOS.Linux);

        Assert.Equal(["Run tests", "Tail logs"], contents.Actions.Select(a => a.Name));
        var tests = contents.Actions[0];
        Assert.Equal(("shared:0", ProjectFileScope.Shared, "dotnet test", "src", CustomActionMode.RunWithOutput), (tests.Id, tests.Scope, tests.Command, tests.WorkingFolder, tests.Mode));
        Assert.Equal("shared:2", contents.Actions[1].Id);
        Assert.Equal(
        [
            "deventry.json: actions[3] has no name, so it was skipped.",
            "deventry.json: actions[4] (\"No command\") has no command, so it was skipped.",
            "deventry.json: actions[5] (\"Odd mode\") has mode \"background\", which isn't \"output\" or \"launch\", so it was skipped.",
            "deventry.json: actions[6] isn't an object, so it was skipped.",
            "deventry.json: links[2] has no url, so it was skipped.",
        ], contents.Problems);
        Assert.Equal(["Board", "https://github.com/org/repo/compare/{branch}?expand=1"], contents.Links.Select(l => l.Name));
    }

    [Theory]
    [InlineData(ToolOS.Windows, new[] { "Run tests", "Open Grafana" })]
    [InlineData(ToolOS.MacOS, new[] { "Run tests" })]
    [InlineData(ToolOS.Linux, new[] { "Run tests", "Tail logs" })]
    public void An_action_whose_os_leaves_out_this_machine_isnt_shown(ToolOS os, string[] names)
    {
        Assert.Equal(names, ProjectFile.Parse(Shared, ProjectFileScope.Shared, os).Actions.Select(a => a.Name));
        Assert.Equal(LaunchMode(os), ProjectFile.Parse(Shared, ProjectFileScope.Shared, os).Actions.Any(a => a.Mode == CustomActionMode.LaunchAndForget));

        static bool LaunchMode(ToolOS os) => os == ToolOS.Windows;
    }

    [Fact]
    public void The_local_file_comes_after_the_shared_one_and_missing_files_mean_nothing()
    {
        var folder = _temp.CreateFolder("game");
        Assert.True(ProjectFile.Read(folder, ToolOS.Linux).IsEmpty);

        _temp.Write("game/deventry.json", Shared);
        _temp.Write("game/deventry.local.json", """
            { "actions": [ { "name": "My build", "command": "make mine" } ], "links": [ { "name": "My board", "url": "https://mine.example" } ] }
            """);

        var both = ProjectFile.Read(folder, ToolOS.Linux);

        Assert.Equal(["Run tests", "Tail logs", "My build"], both.Actions.Select(a => a.Name));
        Assert.Equal(("local:0", ProjectFileScope.Local), (both.Actions[2].Id, both.Actions[2].Scope));
        Assert.Equal(["Board", "https://github.com/org/repo/compare/{branch}?expand=1", "My board"], both.Links.Select(l => l.Name));
        Assert.Equal(["Run tests", "Tail logs"], both.Actions.Where(a => a.Scope == ProjectFileScope.Shared).Select(a => a.Name));
    }

    [Fact]
    public void An_action_with_ifExists_is_shown_only_while_all_its_paths_exist()
    {
        var folder = _temp.CreateFolder("game");
        _temp.Write("game/deventry.json", """
            {
              "actions": [
                { "name": "Play", "command": "Build/Game", "ifExists": "Build/Game" },
                { "name": "Open logs", "command": "open Saved/Logs", "ifExists": [" Saved/Logs ", "Build/Game", ""] },
                { "name": "Always", "command": "make", "ifExists": [] },
                { "name": "Odd", "command": "x", "ifExists": { "path": "Build" } },
              ],
            }
            """);

        var before = ProjectFile.Read(folder, ToolOS.Linux);
        Assert.Equal(["Always"], before.Actions.Select(a => a.Name));
        Assert.Equal(["deventry.json: actions[3] (\"Odd\"): \"ifExists\" should be a path or a list of paths, such as \"Build/Game.exe\", so it was skipped."], before.Problems);

        _temp.Write("game/Build/Game", "");
        Assert.Equal(["Play", "Always"], ProjectFile.Read(folder, ToolOS.Linux).Actions.Select(a => a.Name));

        // A folder counts as well as a file.
        _temp.CreateFolder("game/Saved/Logs");
        var after = ProjectFile.Read(folder, ToolOS.Linux);
        Assert.Equal(["Play", "Open logs", "Always"], after.Actions.Select(a => a.Name));
        Assert.Equal("shared:1", after.Actions[1].Id);
        Assert.Equal(["Saved/Logs", "Build/Game"], after.Actions[1].IfExists!);
        Assert.Null(after.Actions[2].IfExists);
    }

    [Fact]
    public void A_file_that_isnt_JSON_is_skipped_with_a_reason_and_the_other_still_counts()
    {
        var folder = _temp.CreateFolder("game");
        _temp.Write("game/deventry.json", "{ \"actions\": [ oops");
        _temp.Write("game/deventry.local.json", """{ "actions": [ { "name": "Mine", "command": "make" } ] }""");

        var contents = ProjectFile.Read(folder, ToolOS.Linux);

        Assert.Equal(["Mine"], contents.Actions.Select(a => a.Name));
        Assert.StartsWith("deventry.json isn't valid JSON, so it was skipped:", Assert.Single(contents.Problems), StringComparison.Ordinal);
        Assert.Single(ProjectFile.Parse("[1, 2]", ProjectFileScope.Local, ToolOS.Linux).Problems);
        Assert.Single(ProjectFile.Parse("""{ "actions": { "name": "x" } }""", ProjectFileScope.Local, ToolOS.Linux).Problems);
        Assert.True(ProjectFile.Parse("", ProjectFileScope.Local, ToolOS.Linux).IsEmpty);
    }

    // ---- Editing --------------------------------------------------------------------------------------------------

    [Fact]
    public void Saving_actions_keeps_the_other_keys_and_each_entrys_own_fields()
    {
        var folder = _temp.CreateFolder("game");
        var path = _temp.Write("game/deventry.json", Shared);
        var entries = ProjectFile.ReadEntries(folder, ProjectFileScope.Shared, ToolOS.MacOS);
        Assert.Equal(7, entries.Count);
        Assert.False(entries[1].ForThisOS);
        Assert.NotNull(entries[3].Problem);

        var edited = entries[0].Action!.Clone();
        edited.Name = "Run all tests";
        edited.Command = "dotnet test && echo <done>";
        edited.WorkingFolder = null;
        ProjectFile.WriteActions(folder, ProjectFileScope.Shared,
            [ProjectFile.ToJson(edited, entries[0].Raw), entries[1].Raw, ProjectFile.ToJson(new CustomProjectAction { Name = "Serve", Command = "./serve", Mode = CustomActionMode.LaunchAndForget })]);

        var text = File.ReadAllText(path);
        var root = JsonNode.Parse(text)!.AsObject();
        Assert.Equal(["actions", "links", "somethingElse"], root.Select(p => p.Key));
        Assert.Contains("\n  \"actions\": [", text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("dotnet test && echo <done>", text, StringComparison.Ordinal);
        var first = root["actions"]![0]!.AsObject();
        Assert.Equal("unknown fields are fine", first["note"]!.GetValue<string>());
        Assert.Null(first["folder"]);
        Assert.Equal("windows", root["actions"]![1]!["os"]![0]!.GetValue<string>());
        Assert.Equal("launch", root["actions"]![2]!["mode"]!.GetValue<string>());
        Assert.True(root["somethingElse"]!["kept"]!.GetValue<bool>());
        Assert.Equal(["Run all tests", "Serve"], ProjectFile.Read(folder, ToolOS.MacOS).Actions.Select(a => a.Name));
    }

    [Fact]
    public void An_action_can_run_in_each_new_worktree_and_saving_keeps_or_drops_the_flag()
    {
        var contents = ProjectFile.Parse("""
            { "actions": [
                { "name": "Install", "command": "npm ci", "runOnNewWorktree": true },
                { "name": "Lint", "command": "npm run lint", "runOnNewWorktree": "yes" },
                { "name": "Test", "command": "npm test" }
            ] }
            """, ProjectFileScope.Shared, ToolOS.Linux);

        // Only true turns it on (DESIGN.md §18, "Setting up a new worktree").
        Assert.Equal([true, false, false], contents.Actions.Select(a => a.RunOnNewWorktree));
        Assert.True(ProjectFile.ToJson(contents.Actions[0])["runOnNewWorktree"]!.GetValue<bool>());
        var off = contents.Actions[0].Clone();
        off.RunOnNewWorktree = false;
        Assert.False(ProjectFile.ToJson(off, ProjectFile.ToJson(contents.Actions[0])).ContainsKey("runOnNewWorktree"));
    }

    [Fact]
    public void Actions_and_links_have_a_group_and_an_icon_and_one_that_cant_be_used_is_left_out_with_a_note()
    {
        var contents = ProjectFile.Parse("""
            {
              "actions": [
                { "name": "Deploy", "command": "./deploy", "group": " Release ", "icon": "Upload" },
                { "name": "Lint", "command": "npm run lint", "group": "", "icon": "" },
                { "name": "Rocket", "command": "./go", "icon": "rocket" },
                { "name": "Numbered", "command": "./n", "group": 3, "icon": 4 }
              ],
              "links": [
                { "name": "Dashboard", "url": "https://example.com", "group": "Release", "icon": "web" },
                { "url": "https://example.com/odd", "icon": "sparkles" }
              ]
            }
            """, ProjectFileScope.Shared, ToolOS.Linux);

        // An action or link with a group or icon that can't be used is still read, under Actions or Links, with its own icon.
        Assert.Equal(["Deploy", "Lint", "Rocket", "Numbered"], contents.Actions.Select(a => a.Name));
        Assert.Equal<string?[]>(["Release", null, null, null], [.. contents.Actions.Select(a => a.Group)]);
        Assert.Equal<string?[]>([MenuIcons.Upload, null, null, null], [.. contents.Actions.Select(a => a.Icon)]);
        Assert.Equal<(string?, string?)[]>([("Release", MenuIcons.Web), (null, null)], [.. contents.Links.Select(l => (l.Group, l.Icon))]);
        Assert.Equal(
        [
            $"deventry.json: actions[2] (\"Rocket\"): \"icon\" is \"rocket\", which isn't one of Deventry's icons ({string.Join(", ", MenuIcons.Names)}), so it shows without it.",
            "deventry.json: actions[3] (\"Numbered\"): \"group\" should be a name, such as \"Deploy\"; \"icon\" should be the name of one of Deventry's icons, such as \"play\", so it shows without it.",
            $"deventry.json: links[1] (\"https://example.com/odd\"): \"icon\" is \"sparkles\", which isn't one of Deventry's icons ({string.Join(", ", MenuIcons.Names)}), so it shows without it.",
        ], contents.Problems);

        // They carry on to what the menu shows.
        var deploy = contents.Actions[0].ToAction(_temp.Path, ToolOS.Linux, "/bin/sh");
        Assert.Equal(("Release", MenuIcons.Upload, MenuIcons.Upload), (deploy.Group, deploy.Icon, deploy.MenuIcon));
        Assert.Equal(MenuIcons.Terminal, contents.Actions[1].ToAction(_temp.Path, ToolOS.Linux, "/bin/sh").MenuIcon);
        var link = ProjectLinks.Resolve(contents.Links[0], new LinkValues("main", null, "game"));
        Assert.Equal(("Release", MenuIcons.Web), (link.Group, link.Icon));
    }

    [Fact]
    public void Saving_writes_the_group_and_icon_and_leaves_them_out_when_empty()
    {
        var action = new CustomProjectAction { Name = "Deploy", Command = "./deploy", Group = "Release", Icon = MenuIcons.Upload };
        var json = ProjectFile.ToJson(action);
        Assert.Equal(("Release", "upload"), (json["group"]!.GetValue<string>(), json["icon"]!.GetValue<string>()));
        action.Group = " ";
        action.Icon = null;
        var cleared = ProjectFile.ToJson(action, json);
        Assert.False(cleared.ContainsKey("group"));
        Assert.False(cleared.ContainsKey("icon"));

        var link = ProjectFile.LinkToJson("Dashboard", "https://example.com", new JsonObject { ["note"] = "kept" }, " Release ", MenuIcons.Web);
        Assert.Equal(("Release", "web", "kept"), (link["group"]!.GetValue<string>(), link["icon"]!.GetValue<string>(), link["note"]!.GetValue<string>()));
        var plain = ProjectFile.LinkToJson("Dashboard", "https://example.com", link);
        Assert.False(plain.ContainsKey("group"));
        Assert.False(plain.ContainsKey("icon"));

        // What reads as none here, such as an icon a newer Deventry has, stays as written when the entry is saved again.
        var entries = ProjectFile.Parse("""
            { "actions": [ { "name": "Go", "command": "./go", "group": 3, "icon": "rocket" } ],
              "links": [ { "url": "https://example.com", "icon": "sparkles" } ] }
            """, ProjectFileScope.Shared, ToolOS.Linux);
        var raw = new JsonObject { ["name"] = "Go", ["command"] = "./go", ["group"] = 3, ["icon"] = "rocket" };
        var again = ProjectFile.ToJson(entries.Actions[0], raw);
        Assert.Equal((3, "rocket"), (again["group"]!.GetValue<int>(), again["icon"]!.GetValue<string>()));
        var linkRaw = new JsonObject { ["url"] = "https://example.com", ["icon"] = "sparkles" };
        Assert.Equal("sparkles", ProjectFile.LinkToJson(null, "https://example.com", linkRaw, entries.Links[0].Group, entries.Links[0].Icon)["icon"]!.GetValue<string>());
        // Chosen in the editor, it's written over them.
        var chosen = entries.Actions[0].Clone();
        chosen.Icon = MenuIcons.Play;
        Assert.Equal("play", ProjectFile.ToJson(chosen, raw)["icon"]!.GetValue<string>());
    }

    [Fact]
    public void An_icons_name_is_found_whatever_its_case() =>
        Assert.All(MenuIcons.Names, name => Assert.Equal(name, MenuIcons.Find($" {name.ToUpperInvariant()} ")));

    [Fact]
    public void A_missing_file_is_created_and_one_that_isnt_JSON_is_never_overwritten()
    {
        var folder = _temp.CreateFolder("game");

        ProjectFile.WriteActions(folder, ProjectFileScope.Local, [ProjectFile.ToJson(new CustomProjectAction { Name = "Mine", Command = "make", WorkingFolder = "src" })]);
        Assert.Equal(("Mine", "src"), (ProjectFile.Read(folder, ToolOS.Linux).Actions.Single().Name, ProjectFile.Read(folder, ToolOS.Linux).Actions.Single().WorkingFolder));

        var broken = _temp.Write("game/deventry.json", "{ broken");
        Assert.Throws<InvalidOperationException>(() => ProjectFile.WriteActions(folder, ProjectFileScope.Shared, []));
        Assert.Throws<InvalidOperationException>(() => ProjectFile.ReadEntries(folder, ProjectFileScope.Shared, ToolOS.Linux));
        Assert.Equal("{ broken", File.ReadAllText(broken));
    }

    [Fact]
    public void Saving_links_rewrites_only_links_and_keeps_the_actions_and_each_entrys_own_fields()
    {
        var folder = _temp.CreateFolder("game");
        var path = _temp.Write("game/deventry.json", Shared);
        var entries = ProjectFile.ReadLinkEntries(folder, ProjectFileScope.Shared);
        Assert.Equal(["Board", "https://github.com/org/repo/compare/{branch}?expand=1", null], entries.Select(e => e.Link?.Name));
        Assert.Equal(("Board", ""), (entries[0].GivenName, entries[1].GivenName));
        Assert.Equal("links[2] has no url, so it was skipped.", entries[2].Problem);
        var actionsBefore = JsonNode.Parse(File.ReadAllText(path), documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true })!["actions"]!.ToJsonString();

        ProjectFile.WriteLinks(folder, ProjectFileScope.Shared,
        [
            ProjectFile.LinkToJson(entries[1].GivenName, entries[1].Link!.Url, entries[1].Raw),
            ProjectFile.LinkToJson("Sprint board", " https://example.com/sprint?a=1&b=2 ", entries[0].Raw),
            entries[2].Raw,
        ]);

        var text = File.ReadAllText(path);
        var root = JsonNode.Parse(text)!.AsObject();
        Assert.Equal(["actions", "links", "somethingElse"], root.Select(p => p.Key));
        Assert.Equal(actionsBefore, root["actions"]!.ToJsonString());
        Assert.DoesNotContain("Committed with the project", text, StringComparison.Ordinal);
        Assert.Contains("https://example.com/sprint?a=1&b=2", text, StringComparison.Ordinal);
        Assert.Null(root["links"]![0]!["name"]);
        Assert.Equal("Sprint board", root["links"]![1]!["name"]!.GetValue<string>());
        Assert.Equal("No url", root["links"]![2]!["name"]!.GetValue<string>());
        Assert.Equal(["https://github.com/org/repo/compare/{branch}?expand=1", "Sprint board"], ProjectFile.Read(folder, ToolOS.Linux).Links.Select(l => l.Name));
    }

    [Fact]
    public void Links_go_in_a_new_file_when_there_is_none_and_never_in_one_that_isnt_JSON()
    {
        var folder = _temp.CreateFolder("game");

        ProjectFile.WriteLinks(folder, ProjectFileScope.Local, [ProjectFile.LinkToJson("", "mailto:team@example.com")]);
        Assert.Equal(("mailto:team@example.com", ProjectFileScope.Local), (ProjectFile.Read(folder, ToolOS.Linux).Links.Single().Name, ProjectFile.Read(folder, ToolOS.Linux).Links.Single().Scope));
        Assert.Empty(ProjectFile.ReadLinkEntries(folder, ProjectFileScope.Shared));

        var broken = _temp.Write("game/deventry.json", "{ broken");
        var refused = Assert.Throws<InvalidOperationException>(() => ProjectFile.WriteLinks(folder, ProjectFileScope.Shared, []));
        Assert.StartsWith("deventry.json isn't valid JSON (", refused.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => ProjectFile.ReadLinkEntries(folder, ProjectFileScope.Shared));
        Assert.Equal("{ broken", File.ReadAllText(broken));
    }

    // ---- The group's icon ---------------------------------------------------------------------------------------------

    [Fact]
    public void The_icon_is_read_from_the_projects_folder_and_the_local_files_wins()
    {
        var folder = _temp.CreateFolder("game");
        var logo = _temp.Write("game/Assets/logo.png", "png");
        _temp.Write("game/mine.ico", "ico");

        _temp.Write("game/deventry.json", """{ "icon": "Assets/logo.png" }""");
        var shared = ProjectFile.Read(folder, ToolOS.Linux);
        Assert.Equal(new ProjectIcon("Assets/logo.png", ProjectFileScope.Shared, logo), shared.Icon);
        Assert.Empty(shared.Problems);

        _temp.Write("game/deventry.local.json", """{ "icon": "mine.ico" }""");
        var local = ProjectFile.Read(folder, ToolOS.Linux).Icon;
        Assert.Equal(("mine.ico", ProjectFileScope.Local, Path.Combine(folder, "mine.ico")), (local?.Path, local?.Scope, local?.FullPath));
    }

    [Theory]
    [InlineData("Assets/logo.svg", "deventry.json: icon Assets/logo.svg isn't a PNG, ICO, JPEG, BMP, GIF or WebP picture.")]
    [InlineData("Assets/gone.png", "deventry.json: icon Assets/gone.png isn't there.")]
    [InlineData("Assets/huge.png", "deventry.json: icon Assets/huge.png is over 1 MB, far more than an icon needs.")]
    [InlineData(@"\\server\share\logo.png", @"deventry.json: icon \\server\share\logo.png is on a network share, which Deventry doesn't look at by itself.")]
    [InlineData("//server/share/logo.png", "deventry.json: icon //server/share/logo.png is on a network share, which Deventry doesn't look at by itself.")]
    public void An_icon_that_cant_be_shown_says_why_and_the_group_keeps_its_dot(string icon, string problem)
    {
        var folder = _temp.CreateFolder("game");
        _temp.Write("game/Assets/logo.svg", "<svg/>");
        _temp.Write("game/Assets/huge.png", new string('x', (int)ProjectFile.IconMaxBytes + 1));
        _temp.Write("game/deventry.json", new JsonObject { ["icon"] = icon }.ToJsonString());

        var contents = ProjectFile.Read(folder, ToolOS.Linux);

        Assert.Null(contents.Icon);
        Assert.Equal([problem], contents.Problems);
    }

    [Fact]
    public void An_icon_that_isnt_a_path_is_skipped_with_a_reason()
    {
        var folder = _temp.CreateFolder("game");
        _temp.Write("game/deventry.json", """{ "icon": 42 }""");

        Assert.Equal(["deventry.json: \"icon\" should be a picture's path, such as \"Assets/logo.png\", so it was skipped."], ProjectFile.Read(folder, ToolOS.Linux).Problems);
    }

    [Fact]
    public void Saving_the_icon_keeps_the_rest_and_taking_it_out_leaves_no_new_file()
    {
        var folder = _temp.CreateFolder("game");
        _temp.Write("game/deventry.json", """{ "links": [ { "url": "https://example.com" } ] }""");

        ProjectFile.WriteIcon(folder, ProjectFileScope.Shared, "Assets/logo.png");
        Assert.Equal("Assets/logo.png", ProjectFile.ReadIcon(folder, ProjectFileScope.Shared));
        Assert.Equal("https://example.com", ProjectFile.Read(folder, ToolOS.Linux).Links.Single().Url);

        ProjectFile.WriteIcon(folder, ProjectFileScope.Shared, null);
        Assert.Null(ProjectFile.ReadIcon(folder, ProjectFileScope.Shared));
        Assert.Single(ProjectFile.Read(folder, ToolOS.Linux).Links);

        // Nothing to take out of a file that isn't there, so it isn't made.
        ProjectFile.WriteIcon(folder, ProjectFileScope.Local, null);
        Assert.False(File.Exists(Path.Combine(folder, ProjectFile.LocalName)));
    }

    [Fact]
    public void A_picture_in_the_project_is_written_from_its_folder_and_one_elsewhere_in_full()
    {
        var folder = _temp.CreateFolder("game");
        var elsewhere = _temp.Write("pictures/logo.png", "png");

        Assert.Equal("Assets/Icons/logo.png", ProjectFile.IconPathFor(folder, Path.Combine(folder, "Assets", "Icons", "logo.png")));
        Assert.Equal(elsewhere, ProjectFile.IconPathFor(folder, elsewhere));
    }

    // ---- The project's solution (issue #112) ---------------------------------------------------------------------------

    [Fact]
    public void The_solution_is_read_from_either_file_and_the_local_files_wins()
    {
        var folder = _temp.CreateFolder("game");
        _temp.Write("game/deventry.json", """{ "solution": "NightOwl.sln" }""");
        Assert.Equal(new ProjectSolution("NightOwl.sln", ProjectFileScope.Shared), ProjectFile.Read(folder, ToolOS.Linux).Solution);

        _temp.Write("game/deventry.local.json", """{ "solution": "Tools/Tools.slnx" }""");
        Assert.Equal(new ProjectSolution("Tools/Tools.slnx", ProjectFileScope.Local), ProjectFile.Read(folder, ToolOS.Linux).Solution);

        _temp.Write("game/deventry.json", """{ "solution": 7 }""");
        Assert.Equal(["deventry.json: \"solution\" should be a file's path, such as \"NightOwl.sln\", so it was skipped."], ProjectFile.Read(folder, ToolOS.Linux).Problems);
    }

    [Fact]
    public void A_solution_opens_with_the_IDE_another_file_with_the_systems_app_and_one_that_isnt_there_says_so()
    {
        var folder = _temp.CreateFolder("game");
        var sln = _temp.Write("game/NightOwl.sln", "");
        var project = _temp.Write("game/Code/Game.csproj", "");

        var open = new ProjectSolution("NightOwl.sln", ProjectFileScope.Shared).ToAction(folder, "deventry.json");
        Assert.Equal((ProjectSolution.ActionId, "Open NightOwl.sln", ProjectActionKind.Open), (open.Id, open.Label, open.Kind));
        Assert.Equal((sln, true, true), (open.OpenPath, open.OpenWithIde, open.IsEnabled));

        // Any file that defines the project, with the app the system has for it.
        var other = new ProjectSolution("Code/Game.csproj", ProjectFileScope.Local).ToAction(folder, "deventry.local.json");
        Assert.Equal(("Open Game.csproj", project, false), (other.Label, other.OpenPath, other.OpenWithIde));

        // A workspace that's a folder, as Xcode's is.
        _temp.CreateFolder("game/NightOwl.xcworkspace");
        var workspace = new ProjectSolution("NightOwl.xcworkspace", ProjectFileScope.Shared).ToAction(folder, "deventry.json");
        Assert.Equal((true, true), (workspace.IsEnabled, workspace.OpenWithIde));

        var gone = new ProjectSolution("Gone.sln", ProjectFileScope.Shared).ToAction(folder, "deventry.json");
        Assert.Equal((null, "deventry.json's solution, Gone.sln, isn't there."), (gone.OpenPath, gone.DisabledReason));
        var share = new ProjectSolution(@"\\server\share\NightOwl.sln", ProjectFileScope.Shared).ToAction(folder, "deventry.json");
        Assert.Equal(@"deventry.json's solution, \\server\share\NightOwl.sln, is on a network share, which Deventry doesn't look at by itself.", share.DisabledReason);
    }

    [Fact]
    public void Saving_the_solution_keeps_the_rest_and_is_written_from_the_projects_folder()
    {
        var folder = _temp.CreateFolder("game");
        _temp.Write("game/deventry.json", """{ "links": [ { "url": "https://example.com" } ] }""");

        ProjectFile.WriteSolution(folder, ProjectFileScope.Shared, ProjectFile.SolutionPathFor(folder, Path.Combine(folder, "Code", "Game.sln")));
        Assert.Equal("Code/Game.sln", ProjectFile.ReadSolution(folder, ProjectFileScope.Shared));
        Assert.Equal("https://example.com", ProjectFile.Read(folder, ToolOS.Linux).Links.Single().Url);

        ProjectFile.WriteSolution(folder, ProjectFileScope.Shared, null);
        Assert.Null(ProjectFile.ReadSolution(folder, ProjectFileScope.Shared));
        Assert.Single(ProjectFile.Read(folder, ToolOS.Linux).Links);
    }

    // ---- The group's color --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("#4a6fc4", "#4A6FC4")]
    [InlineData("4A6FC4", "#4A6FC4")]
    [InlineData(" #1a3 ", "#11AA33")]
    [InlineData("purple", null)]
    [InlineData("#12345", null)]
    [InlineData(null, null)]
    public void A_color_is_kept_as_six_hex_digits(string? text, string? color) => Assert.Equal(color, ProjectFile.NormalizeColor(text));

    [Fact]
    public void The_color_is_read_from_either_file_and_the_local_files_wins()
    {
        var folder = _temp.CreateFolder("game");
        _temp.Write("game/deventry.json", """{ "color": "#4a6fc4" }""");
        Assert.Equal("#4A6FC4", ProjectFile.Read(folder, ToolOS.Linux).Color);

        _temp.Write("game/deventry.local.json", """{ "color": "#1a3" }""");
        Assert.Equal("#11AA33", ProjectFile.Read(folder, ToolOS.Linux).Color);
    }

    [Fact]
    public void A_color_that_isnt_one_is_skipped_with_a_reason()
    {
        var folder = _temp.CreateFolder("game");
        _temp.Write("game/deventry.json", """{ "color": "purple" }""");

        var contents = ProjectFile.Read(folder, ToolOS.Linux);

        Assert.Null(contents.Color);
        Assert.Equal(["deventry.json: \"color\" should be a color such as \"#4A6FC4\", so it was skipped."], contents.Problems);
    }

    [Fact]
    public void Saving_the_color_keeps_the_rest_and_taking_it_out_leaves_the_icon()
    {
        var folder = _temp.CreateFolder("game");
        _temp.Write("game/deventry.json", """{ "icon": "logo.png" }""");

        ProjectFile.WriteColor(folder, ProjectFileScope.Shared, "#1a3");
        Assert.Equal("#11AA33", ProjectFile.ReadColor(folder, ProjectFileScope.Shared));
        Assert.Equal("logo.png", ProjectFile.ReadIcon(folder, ProjectFileScope.Shared));

        ProjectFile.WriteColor(folder, ProjectFileScope.Shared, null);
        Assert.Null(ProjectFile.ReadColor(folder, ProjectFileScope.Shared));
        Assert.Equal("logo.png", ProjectFile.ReadIcon(folder, ProjectFileScope.Shared));
        Assert.Throws<ArgumentException>(() => ProjectFile.WriteColor(folder, ProjectFileScope.Shared, "purple"));
    }

    // ---- Links ----------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("https://github.com/org/repo/compare/{branch}?expand=1", null)]
    [InlineData("  http://swarm.example/changes/{changelist}  ", null)]
    [InlineData("mailto:team@example.com?subject={folderName}", null)]
    [InlineData("", "Type the address to open, such as https://example.com.")]
    [InlineData("file:///etc/passwd", "\"file:\" links don't open from Deventry; only https, http and mailto do.")]
    [InlineData("javascript:alert(1)", "\"javascript:\" links don't open from Deventry; only https, http and mailto do.")]
    [InlineData("example.com/{branch}", "example.com/{branch} isn't a full web address, such as https://example.com.")]
    [InlineData("https://example.com/{user}", "Unknown placeholder {user}: use {branch}, {changelist} or {folderName}.")]
    public void An_address_being_edited_is_checked_by_the_rules_links_open_by(string url, string? problem) =>
        Assert.Equal(problem, ProjectLinks.Validate(url));

    private static readonly LinkValues Values = new("feature/owl eyes", "12345", "Night Owl");

    [Fact]
    public void Placeholders_are_filled_and_escaped()
    {
        var link = ProjectLinks.Resolve(new ProjectLink("PR", "https://github.com/org/repo/compare/{branch}?expand=1&cl={changelist}&f={folderName}", ProjectFileScope.Shared), Values);

        Assert.Equal("https://github.com/org/repo/compare/feature%2Fowl%20eyes?expand=1&cl=12345&f=Night%20Owl", link.Url);
        Assert.True(link.IsEnabled);
        Assert.Equal(link.Url, link.Tip);
        Assert.Equal("mailto:team@example.com?subject=Night%20Owl", ProjectLinks.Resolve(new ProjectLink("Mail", "mailto:team@example.com?subject={FolderName}", ProjectFileScope.Local), Values).Url);
    }

    [Theory]
    [InlineData("file:///etc/passwd", "\"file:\" links don't open from Deventry; only https, http and mailto do.")]
    [InlineData("javascript:alert(1)", "\"javascript:\" links don't open from Deventry; only https, http and mailto do.")]
    [InlineData("vscode://file/{folderName}", "\"vscode:\" links don't open from Deventry; only https, http and mailto do.")]
    [InlineData("example.com/board", "example.com/board isn't a full web address, such as https://example.com.")]
    [InlineData("https://example.com/{user}", "Unknown placeholder {user}: use {branch}, {changelist} or {folderName}.")]
    public void Only_https_http_and_mailto_links_open(string url, string reason)
    {
        var link = ProjectLinks.Resolve(new ProjectLink("Link", url, ProjectFileScope.Shared), Values);

        Assert.False(link.IsEnabled);
        Assert.Null(link.Url);
        Assert.Equal(reason, link.Problem);
    }

    [Fact]
    public void A_placeholder_that_cant_be_filled_disables_the_link_and_says_why()
    {
        var none = new LinkValues(null, null, "game");

        Assert.Equal("No git branch.", ProjectLinks.Resolve(new ProjectLink("PR", "https://github.com/o/r/compare/{branch}", ProjectFileScope.Shared), none).Problem);
        Assert.Equal("No Perforce changelist.", ProjectLinks.Resolve(new ProjectLink("Swarm", "https://swarm/changes/{changelist}", ProjectFileScope.Shared), none).Problem);
        Assert.True(ProjectLinks.Resolve(new ProjectLink("Board", "https://example.com/{folderName}", ProjectFileScope.Shared), none).IsEnabled);
    }
}
