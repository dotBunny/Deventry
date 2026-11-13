using System.ComponentModel;
using System.Net;
using System.Text.Json.Nodes;
using Deventry.Core.Processes;
using Deventry.Core.Tests.Support;
using Deventry.Core.Tickets;
using Deventry.Core.Tickets.GitHub;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.Core.Tests.Tickets;

/// <summary>
/// GitHub's tokens from elsewhere (DESIGN.md §18, "Tickets", "Signing in"): the GitHub CLI's, Git Credential Manager's,
/// and Sign in with GitHub's device flow, with fake processes and a fake GitHub.
/// </summary>
public sealed class GitHubSignInTests
{
    private readonly FakeProcessLauncher _launcher = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private GitHubCredentialSources Sources() => new(_launcher, _time,
        (spec, _) => Task.FromResult(spec with { Environment = new Dictionary<string, string> { ["PATH"] = "/usr/bin", ["GIT_ASKPASS"] = "/opt/askpass", ["HOME"] = "/home/kim" } }),
        () => "/usr/bin/git");

    [Fact]
    public async Task The_GitHub_CLIs_token_for_the_server()
    {
        _launcher.Respond = _ => new ProcessResult(0, "gho_cliToken123\n", "");

        var token = await Sources().FromGitHubCliAsync("github.com", Token);

        Assert.Equal("gho_cliToken123", token);
        var spec = Assert.Single(_launcher.Started);
        Assert.Equal("gh", spec.FileName);
        Assert.Equal(["auth", "token", "--hostname", "github.com"], spec.Arguments);
    }

    [Fact]
    public async Task The_GitHub_CLI_not_signed_in_says_how_to()
    {
        _launcher.Respond = _ => new ProcessResult(1, "", "no oauth token found for github.com\n");

        var error = await Assert.ThrowsAsync<TicketException>(() => Sources().FromGitHubCliAsync("github.com", Token));

        Assert.Equal(TicketErrorKind.Refused, error.Kind);
        Assert.Equal("The GitHub CLI isn't signed in to github.com (no oauth token found for github.com). Run gh auth login --hostname github.com in a terminal.",
            error.Message);
    }

    [Fact]
    public async Task The_GitHub_CLI_not_installed_says_so()
    {
        _launcher.StartFailure = new Win32Exception("The system cannot find the file specified.");

        var error = await Assert.ThrowsAsync<TicketException>(() => Sources().FromGitHubCliAsync("github.com", Token));

        Assert.StartsWith("The GitHub CLI (gh) couldn't be started:", error.Message);
    }

    [Fact]
    public async Task Git_Credential_Managers_token_is_asked_for_without_any_prompt()
    {
        _launcher.Respond = _ => new ProcessResult(0, "protocol=https\nhost=github.com\nusername=kim\npassword=gho_gcmToken456\n", "");

        var token = await Sources().FromGitCredentialManagerAsync("github.com", Token);

        Assert.Equal("gho_gcmToken456", token);
        var spec = Assert.Single(_launcher.Started);
        Assert.Equal("/usr/bin/git", spec.FileName);
        Assert.Equal(["-c", "credential.interactive=never", "-c", "core.askPass=", "credential", "fill"], spec.Arguments);
        Assert.Equal("0", spec.Environment!["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("never", spec.Environment["GCM_INTERACTIVE"]);
        Assert.False(spec.Environment.ContainsKey("GIT_ASKPASS"));
        Assert.Equal("/home/kim", spec.Environment["HOME"]);
        var process = Assert.Single(_launcher.Processes);
        Assert.Equal(["protocol=https\nhost=github.com\n"], process.Input);
        Assert.True(process.StandardInputClosed);
    }

    [Fact]
    public async Task Git_with_no_credential_says_how_to_get_one()
    {
        _launcher.Respond = _ => new ProcessResult(128, "", "fatal: could not read Username for 'https://github.com': terminal prompts disabled\n");

        var error = await Assert.ThrowsAsync<TicketException>(() => Sources().FromGitCredentialManagerAsync("github.com", Token));

        Assert.Equal(TicketErrorKind.Refused, error.Kind);
        Assert.StartsWith("Git has no credential for github.com (fatal: could not read Username", error.Message);
    }

    [Fact]
    public async Task Sign_in_with_GitHub_waits_for_the_code_to_be_entered()
    {
        var github = new DeviceGitHub();
        using var http = new HttpClient(github);
        var flow = new GitHubDeviceFlow(http, _time, "Iv1.test", "Deventry/test");

        var code = await flow.StartAsync(Token);
        var waiting = flow.WaitForTokenAsync(code, Token);
        // A second at a time: it asks after 5 seconds, again after 5, and after 10 once GitHub says to slow down.
        await Waiting.UntilAsync(() => waiting.IsCompleted, "the token", poll: () => _time.Advance(TimeSpan.FromSeconds(1)));

        Assert.Equal(("WDJB-MJHT", "https://github.com/login/device"), (code.UserCode, code.VerificationUri));
        Assert.Equal("gho_deviceToken789", await waiting);
        Assert.Equal(3, github.Polls);
        Assert.Contains("client_id=Iv1.test", github.Bodies[0]);
        Assert.Contains("scope=repo", github.Bodies[0]);
        Assert.Contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code", github.Bodies[1]);
    }

    [Fact]
    public async Task A_code_refused_at_GitHub_stops_waiting()
    {
        var github = new DeviceGitHub { Refuse = true };
        using var http = new HttpClient(github);
        var flow = new GitHubDeviceFlow(http, _time, "Iv1.test", "Deventry/test");

        var code = await flow.StartAsync(Token);
        var waiting = flow.WaitForTokenAsync(code, Token);
        await Waiting.UntilAsync(() => waiting.IsCompleted, "the refusal", poll: () => _time.Advance(TimeSpan.FromSeconds(1)));

        var error = await Assert.ThrowsAsync<TicketException>(() => waiting);
        Assert.Equal("Signing in was cancelled at GitHub.", error.Message);
    }

    [Fact]
    public void Deventry_offers_Sign_in_with_GitHub_only_with_its_app()
    {
        Assert.Equal(GitHubDeviceFlow.IsAvailable, new GitHubTicketsType().SignIns.Contains(TicketSignIn.Device));
    }

    /// <summary>GitHub's device flow: a code, then pending, slowing down, and the token on the third ask.</summary>
    private sealed class DeviceGitHub : HttpMessageHandler
    {
        private int _polls;

        public bool Refuse { get; init; }

        public List<string> Bodies { get; } = [];

        public int Polls => Volatile.Read(ref _polls);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            lock (Bodies)
            {
                Bodies.Add(body);
            }
            if (request.RequestUri!.AbsolutePath == "/login/device/code")
            {
                return FakeGitHub.Json(HttpStatusCode.OK, new JsonObject
                {
                    ["device_code"] = "3584d83530557fdd1f46af8289938c8ef79f9dc5",
                    ["user_code"] = "WDJB-MJHT",
                    ["verification_uri"] = "https://github.com/login/device",
                    ["expires_in"] = 900,
                    ["interval"] = 5,
                });
            }
            var poll = Interlocked.Increment(ref _polls);
            JsonObject answer = Refuse ? new() { ["error"] = "access_denied" }
                : poll switch
                {
                    1 => new() { ["error"] = "authorization_pending" },
                    2 => new() { ["error"] = "slow_down", ["interval"] = 10 },
                    _ => new() { ["access_token"] = "gho_deviceToken789", ["token_type"] = "bearer", ["scope"] = "repo" },
                };
            return FakeGitHub.Json(HttpStatusCode.OK, answer);
        }
    }
}
