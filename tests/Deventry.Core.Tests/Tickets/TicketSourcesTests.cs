using Deventry.Core.ProjectTools;
using Deventry.Core.Tests.Support;
using Deventry.Core.Tickets;
using Deventry.Core.Tickets.GitHub;
using Deventry.Core.Tickets.YouTrack;

namespace Deventry.Core.Tests.Tickets;

/// <summary>Where a project's tickets come from (DESIGN.md §18, "Tickets"): its files' <c>tickets</c>, or its git remote.</summary>
public sealed class TicketSourcesTests : IDisposable
{
    private static readonly IReadOnlyList<ITicketProviderType> Types = [new GitHubTicketsType(), new YouTrackTicketsType()];

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static TicketSources.Detection Detection(bool fromGit = true, params string[] servers) =>
        new(new Dictionary<string, IReadOnlyCollection<string>> { ["github"] = ["github.com", .. servers] }, _ => fromGit);

    private void Remote(string url)
    {
        _folder.Write(".git/HEAD", "ref: refs/heads/main\n");
        _folder.Write(".git/config", $"[remote \"origin\"]\n\turl = {url}\n\tfetch = +refs/heads/*:refs/remotes/origin/*\n");
    }

    [Fact]
    public void Both_files_sources_are_read_the_shared_files_first()
    {
        _folder.Write("deventry.json", """
            { "tickets": [
              { "provider": "github", "repository": "dotBunny/NightOwl" },
              { "provider": "youtrack", "server": "dotbunny.youtrack.cloud/", "project": "NO", "query": "#Unresolved", "name": "NightOwl" },
            ] }
            """);
        _folder.Write("deventry.local.json", """{ "tickets": [ { "provider": "GitHub", "repository": "studio/engine", "server": "https://GitHub.Studio.Example/" } ] }""");

        var read = TicketSources.Read(_folder.Path, Types, Detection());

        Assert.Empty(read.Problems);
        Assert.Equal(
        [
            new TicketSource("github", "github.com", "dotBunny/NightOwl", null, null, TicketSourceOrigin.Shared, 0),
            new TicketSource("youtrack", "https://dotbunny.youtrack.cloud", "NO", "#Unresolved", "NightOwl", TicketSourceOrigin.Shared, 1),
            new TicketSource("github", "github.studio.example", "studio/engine", null, null, TicketSourceOrigin.Local, 0),
        ], read.Sources);
        Assert.Equal("github:github.com/dotBunny/NightOwl", read.Sources[0].Id);
        Assert.Equal("youtrack:https://dotbunny.youtrack.cloud/NO?#Unresolved", read.Sources[1].Id);
        Assert.Equal("NightOwl", read.Sources[1].DisplayName);
        Assert.Equal("dotBunny/NightOwl", read.Sources[0].DisplayName);
        Assert.Equal("dotbunny.youtrack.cloud", read.Sources[1].ServerName);
    }

    [Fact]
    public void A_bad_entry_is_skipped_with_why_and_the_rest_are_read()
    {
        _folder.Write("deventry.json", """
            { "tickets": [
              "github",
              { "repository": "a/b" },
              { "provider": "jira", "project": "ABC" },
              { "provider": "github" },
              { "provider": "github", "repository": "just-a-name" },
              { "provider": "youtrack", "project": "NO" },
              { "provider": "youtrack", "server": "ftp://x", "project": "NO" },
              { "provider": "github", "repository": "a/b" }
            ] }
            """);

        var read = TicketSources.Read(_folder.Path, Types, Detection());

        Assert.Equal("a/b", Assert.Single(read.Sources).Project);
        Assert.Equal(7, read.Problems.Count);
        Assert.Equal("deventry.json: tickets[0] isn't an object, such as { \"provider\": \"github\", \"repository\": \"owner/name\" }, so it was skipped.", read.Problems[0]);
        Assert.Equal("deventry.json: tickets[1] has no provider, such as \"github\" or \"youtrack\", so it was skipped.", read.Problems[1]);
        Assert.Equal("deventry.json: tickets[2]: \"jira\" isn't a provider Deventry knows (github, youtrack), so it was skipped.", read.Problems[2]);
        Assert.Equal("deventry.json: tickets[3] has no repository, such as \"dotBunny/NightOwl\", so it was skipped.", read.Problems[3]);
        Assert.Contains("isn't a repository", read.Problems[4]);
        Assert.Contains("has no server", read.Problems[5]);
        Assert.Contains("isn't a server's address", read.Problems[6]);
    }

    [Fact]
    public void A_file_that_isnt_JSON_says_so()
    {
        _folder.Write("deventry.json", "{ not json");

        var read = TicketSources.Read(_folder.Path, Types, Detection());

        Assert.Empty(read.Sources);
        Assert.StartsWith("deventry.json isn't valid JSON, so its tickets were skipped:", Assert.Single(read.Problems));
    }

    [Theory]
    [InlineData("https://github.com/dotBunny/NightOwl.git")]
    [InlineData("git@github.com:dotBunny/NightOwl.git")]
    [InlineData("ssh://git@github.com/dotBunny/NightOwl")]
    public void With_no_sources_named_the_git_remotes_repository_is_the_source(string url)
    {
        Remote(url);

        var read = TicketSources.Read(_folder.Path, Types, Detection());

        Assert.Equal(new TicketSource("github", "github.com", "dotBunny/NightOwl", null, null, TicketSourceOrigin.Git), Assert.Single(read.Sources));
    }

    [Fact]
    public void A_remote_on_another_server_has_a_source_only_once_Settings_has_the_server()
    {
        Remote("https://github.studio.example/studio/engine.git");

        Assert.Empty(TicketSources.Read(_folder.Path, Types, Detection()).Sources);
        Assert.Equal("github.studio.example", Assert.Single(TicketSources.Read(_folder.Path, Types, Detection(true, "github.studio.example")).Sources).Server);
    }

    [Fact]
    public void Another_providers_source_sits_beside_the_one_from_git_and_the_same_providers_takes_its_place()
    {
        Remote("https://github.com/dotBunny/NightOwl.git");
        _folder.Write("deventry.json", """{ "tickets": [ { "provider": "youtrack", "server": "https://dotbunny.youtrack.cloud", "project": "NO" } ] }""");

        Assert.Equal(["youtrack:https://dotbunny.youtrack.cloud/NO", "github:github.com/dotBunny/NightOwl"],
            TicketSources.Read(_folder.Path, Types, Detection()).Sources.Select(s => s.Id));

        _folder.Write("deventry.local.json", """{ "tickets": [ { "provider": "github", "repository": "dotBunny/Engine" } ] }""");
        Assert.Equal(["youtrack:https://dotbunny.youtrack.cloud/NO", "github:github.com/dotBunny/Engine"],
            TicketSources.Read(_folder.Path, Types, Detection()).Sources.Select(s => s.Id));
    }

    [Fact]
    public void An_empty_list_and_the_setting_turn_finding_one_from_git_off()
    {
        Remote("https://github.com/dotBunny/NightOwl.git");
        _folder.Write("deventry.local.json", """{ "tickets": [] }""");

        Assert.Empty(TicketSources.Read(_folder.Path, Types, Detection()).Sources);

        _folder.Write("deventry.local.json", """{ "actions": [] }""");
        Assert.Single(TicketSources.Read(_folder.Path, Types, Detection()).Sources);
        Assert.Empty(TicketSources.Read(_folder.Path, Types, Detection(fromGit: false)).Sources);
    }

    [Fact]
    public void A_folder_outside_git_with_no_files_has_none()
    {
        var read = TicketSources.Read(_folder.Path, Types, Detection());

        Assert.Empty(read.Sources);
        Assert.Empty(read.Problems);
    }

    [Fact]
    public void A_source_is_written_back_keeping_the_entrys_other_fields()
    {
        _folder.Write("deventry.json", """{ "tickets": [ { "provider": "github", "repository": "a/b", "note": "kept" } ], "links": [ { "url": "https://example.com" } ] }""");
        var raw = Assert.Single(ProjectFile.ReadTicketEntries(_folder.Path, ProjectFileScope.Shared));
        var source = new TicketSource("github", "github.studio.example", "c/d", null, "Engine", TicketSourceOrigin.Shared, 0);

        ProjectFile.WriteTickets(_folder.Path, ProjectFileScope.Shared, [new GitHubTicketsType().ToJson(source, raw)]);

        var read = TicketSources.Read(_folder.Path, Types, Detection());
        Assert.Equal(source, Assert.Single(read.Sources));
        var written = Assert.Single(ProjectFile.ReadTicketEntries(_folder.Path, ProjectFileScope.Shared))!;
        Assert.Equal("kept", (string)written["note"]!);
        Assert.Single(ProjectFile.Read(_folder.Path, ToolOS.Windows).Links);
    }

    [Theory]
    [InlineData("github.com", false, "github.com")]
    [InlineData("https://GitHub.com/", false, "github.com")]
    [InlineData("github.studio.example:8443", false, "github.studio.example:8443")]
    [InlineData("dotbunny.youtrack.cloud", true, "https://dotbunny.youtrack.cloud")]
    [InlineData("https://tracker.studio.example/youtrack/", true, "https://tracker.studio.example/youtrack")]
    [InlineData("http://localhost:8080", true, "http://localhost:8080")]
    [InlineData("ftp://tracker", true, null)]
    [InlineData("https://user:pw@github.com", false, null)]
    [InlineData("two words", true, null)]
    [InlineData("", true, null)]
    public void A_server_is_written_one_way(string text, bool withScheme, string? expected) =>
        Assert.Equal(expected, TicketServers.Normalize(text, withScheme));

    [Fact]
    public void A_servers_secret_and_settings_are_kept_by_its_address_without_the_scheme()
    {
        Assert.Equal("tickets/youtrack/tracker.studio.example/youtrack", TicketSecrets.Key("youtrack", "https://tracker.studio.example/youtrack"));
        Assert.Equal("tickets/github/github.com", TicketSecrets.Key("github", "github.com"));
        Assert.True(TicketServers.Same("https://Dotbunny.YouTrack.cloud", "dotbunny.youtrack.cloud"));
    }
}
