using System.ComponentModel;
using Deventry.Core.Processes;

namespace Deventry.Core.Tickets.GitHub;

/// <summary>
/// A GitHub token from a program that already has one (DESIGN.md §18, "Tickets", "Signing in"): the GitHub CLI's
/// (<c>gh auth token</c>), or Git Credential Manager's for the server (<c>git credential fill</c>), which it keeps once
/// a clone or push has signed in. Each runs as the user's other programs do, with prompts turned off so a missing
/// token never opens a dialog, and the token is only handed back: Deventry never stores it.
/// </summary>
/// <param name="userEnvironment">Gives a command the user's environment, with the login shell's (<c>UserEnvironment.ApplyAsync</c>).</param>
/// <param name="gitExecutable">The git Settings → Git names (<c>GitWorkingTree.Executable</c>).</param>
public sealed class GitHubCredentialSources(
    IProcessLauncher launcher, TimeProvider time, Func<ProcessStartSpec, CancellationToken, Task<ProcessStartSpec>> userEnvironment, Func<string> gitExecutable)
{
    /// <summary>How long either may take.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>What git and Git Credential Manager are told, so neither asks for anything.</summary>
    public static IReadOnlyDictionary<string, string> NoPrompts { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["GIT_TERMINAL_PROMPT"] = "0",
        ["GCM_INTERACTIVE"] = "never",
    };

    /// <summary>Variables that would have git ask through a program of their own, taken out.</summary>
    public static IReadOnlyList<string> Askpass { get; } = ["GIT_ASKPASS", "SSH_ASKPASS"];

    /// <summary>The GitHub CLI's token for <paramref name="server"/>.</summary>
    /// <exception cref="TicketException">It isn't installed, or isn't signed in to the server.</exception>
    public async Task<string> FromGitHubCliAsync(string server, CancellationToken cancellationToken = default)
    {
        var spec = await userEnvironment(new ProcessStartSpec("gh", ["auth", "token", "--hostname", server]), cancellationToken).ConfigureAwait(false);
        var result = await RunAsync(spec, null, "The GitHub CLI (gh)", cancellationToken).ConfigureAwait(false);
        var token = result.StandardOutput.Trim();
        if (result.ExitCode != 0 || token.Length == 0 || token.Any(char.IsWhiteSpace))
        {
            throw new TicketException(TicketErrorKind.Refused,
                $"The GitHub CLI isn't signed in to {server}{Said(result.StandardError)}. Run gh auth login --hostname {server} in a terminal.");
        }
        return token;
    }

    /// <summary>Git Credential Manager's token for <paramref name="server"/>, or whichever credential helper git has.</summary>
    /// <exception cref="TicketException">Git isn't there, or has no credential for the server.</exception>
    public async Task<string> FromGitCredentialManagerAsync(string server, CancellationToken cancellationToken = default)
    {
        var spec = await userEnvironment(
            new ProcessStartSpec(gitExecutable(), ["-c", "credential.interactive=never", "-c", "core.askPass=", "credential", "fill"]), cancellationToken)
            .ConfigureAwait(false);
        var environment = new Dictionary<string, string>(spec.Environment ?? UserEnvironment.Snapshot(),
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var name in Askpass)
        {
            environment.Remove(name);
        }
        foreach (var (name, value) in NoPrompts)
        {
            environment[name] = value;
        }
        // The credential protocol's request, ended by a blank line: the line written, then closing the input.
        var result = await RunAsync(spec with { Environment = environment }, $"protocol=https\nhost={server}\n", "Git", cancellationToken).ConfigureAwait(false);
        var password = result.StandardOutput.Split('\n').Select(l => l.TrimEnd('\r'))
            .FirstOrDefault(l => l.StartsWith("password=", StringComparison.Ordinal))?["password=".Length..];
        if (result.ExitCode != 0 || password is not { Length: > 0 })
        {
            throw new TicketException(TicketErrorKind.Refused,
                $"Git has no credential for {server}{Said(result.StandardError)}. Sign in once by cloning or pushing a repository on it, or choose another way to sign in.");
        }
        return password;
    }

    private async Task<ProcessResult> RunAsync(ProcessStartSpec spec, string? input, string program, CancellationToken cancellationToken)
    {
        try
        {
            return await ProcessRunner.RunAsync(launcher, spec, Timeout, time, cancellationToken, inputLine: input).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new TicketException(TicketErrorKind.Unreachable, $"{program} didn't answer in time.", inner: ex);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            throw new TicketException(TicketErrorKind.Refused, $"{program} couldn't be started: {ex.Message}", inner: ex);
        }
    }

    /// <summary>The first line a command wrote to its error output, after a colon; nothing when it wrote none.</summary>
    private static string Said(string error) =>
        error.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) is { } line ? $" ({(line.Length > 200 ? line[..200] + "…" : line)})" : "";
}
