using Deventry.Core.Tickets.GitHub;

namespace Deventry.Core.Tests.Tickets;

/// <summary>A GitHub repository from a git remote's address (DESIGN.md §18, "Tickets", "Found from git").</summary>
public sealed class GitHubRemoteTests
{
    [Theory]
    [InlineData("https://github.com/dotBunny/Deventry.git", "github.com", "dotBunny/Deventry")]
    [InlineData("https://github.com/dotBunny/Deventry", "github.com", "dotBunny/Deventry")]
    [InlineData("https://user:token@github.com/dotBunny/Deventry/", "github.com", "dotBunny/Deventry")]
    [InlineData("git@github.com:dotBunny/Deventry.git", "github.com", "dotBunny/Deventry")]
    [InlineData("ssh://git@github.com/dotBunny/Deventry", "github.com", "dotBunny/Deventry")]
    [InlineData("ssh://git@ssh.github.com:443/dotBunny/Deventry.git", "github.com", "dotBunny/Deventry")]
    [InlineData("https://GitHub.Studio.Example:8443/studio/engine.git", "github.studio.example:8443", "studio/engine")]
    [InlineData("git@github.studio.example:studio/engine.git", "github.studio.example", "studio/engine")]
    public void A_remote_is_its_server_and_repository(string url, string server, string repository) =>
        Assert.Equal((server, repository), GitHubRemote.Parse(url));

    [Theory]
    [InlineData("")]
    [InlineData("C:/Repos/game")]
    [InlineData("/home/me/game")]
    [InlineData("file:///home/me/game")]
    [InlineData("https://gitlab.com/group/sub/project.git")]
    [InlineData("https://github.com/only-owner")]
    public void Anything_else_isnt_one(string url) => Assert.Null(GitHubRemote.Parse(url));
}
