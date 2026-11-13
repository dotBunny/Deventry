namespace Deventry.Core.Tickets.GitHub;

/// <summary>
/// A GitHub repository from a git remote's address (DESIGN.md §18, "Tickets", "Found from git"), in any of the forms
/// git takes: <c>https://github.com/owner/name.git</c>, <c>git@github.com:owner/name.git</c>,
/// <c>ssh://git@github.com/owner/name</c>. Its owner and name keep their case, as GitHub shows them.
/// </summary>
public static class GitHubRemote
{
    /// <summary>The remote's server, as a host (<c>github.com</c>), and its repository (<c>owner/name</c>); null when it isn't one.</summary>
    public static (string Server, string Repository)? Parse(string? url)
    {
        var text = url?.Trim() ?? "";
        if (text.Length == 0)
        {
            return null;
        }
        string host;
        string path;
        var scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme > 0)
        {
            if (text[..scheme].ToLowerInvariant() is not ("https" or "http" or "ssh" or "git"))
            {
                return null;
            }
            var rest = text[(scheme + 3)..];
            var slash = rest.IndexOf('/', StringComparison.Ordinal);
            if (slash <= 0)
            {
                return null;
            }
            host = HostOf(rest[..slash], keepPort: text[..scheme].StartsWith("http", StringComparison.OrdinalIgnoreCase));
            path = rest[(slash + 1)..];
        }
        else
        {
            // scp-like: [user@]host:owner/name, the colon before any slash.
            var colon = text.IndexOf(':', StringComparison.Ordinal);
            var firstSlash = text.IndexOf('/', StringComparison.Ordinal);
            if (colon <= 0 || (firstSlash >= 0 && firstSlash < colon) || (colon == 1 && char.IsAsciiLetter(text[0])))
            {
                return null;
            }
            host = HostOf(text[..colon], keepPort: false);
            path = text[(colon + 1)..];
        }
        if (host.Length == 0)
        {
            return null;
        }
        var parts = path.Trim('/').Split('/');
        if (parts.Length != 2)
        {
            return null;
        }
        var owner = parts[0];
        var name = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        var repository = $"{owner}/{name}";
        return IsRepository(repository) ? (host, repository) : null;
    }

    /// <summary><c>owner/name</c>, each part of GitHub's letters, digits, <c>-</c>, <c>_</c> and <c>.</c>.</summary>
    public static bool IsRepository(string? text)
    {
        var parts = text?.Trim().Split('/') ?? [];
        return parts.Length == 2 && parts.All(p => p.Length > 0 && p is not ("." or "..") && p.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'));
    }

    /// <summary>A remote's authority as a host: no user, no port but an address's own, and GitHub's SSH host as GitHub's.</summary>
    private static string HostOf(string authority, bool keepPort)
    {
        var at = authority.LastIndexOf('@');
        var host = (at < 0 ? authority : authority[(at + 1)..]).ToLowerInvariant();
        var colon = host.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0 && !keepPort)
        {
            host = host[..colon];
        }
        else if (colon >= 0 && host[(colon + 1)..] is "443" or "80")
        {
            host = host[..colon];
        }
        return host == "ssh.github.com" ? "github.com" : host;
    }
}
