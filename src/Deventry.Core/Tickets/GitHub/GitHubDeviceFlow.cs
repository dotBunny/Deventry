using System.Globalization;
using System.Text.Json.Nodes;
using Deventry.Core.Json;

namespace Deventry.Core.Tickets.GitHub;

/// <summary>The code a user enters at GitHub to sign Deventry in, and how long it lasts.</summary>
/// <param name="UserCode">What the user types at GitHub: <c>WDJB-MJHT</c>.</param>
/// <param name="VerificationUri">Where they type it: <c>https://github.com/login/device</c>.</param>
/// <param name="DeviceCode">What Deventry asks for the token with; never shown.</param>
public sealed record GitHubDeviceCode(string UserCode, string VerificationUri, string DeviceCode, DateTimeOffset ExpiresAt, TimeSpan Interval);

/// <summary>
/// <b>Sign in with GitHub</b> (DESIGN.md §18, "Tickets", "Signing in"): GitHub's device flow, for github.com. Deventry
/// asks for a code, the user enters it at GitHub, and Deventry asks for the token every few seconds until they have.
/// It needs an app dotBunny registers with GitHub; until its client id is built in (<see cref="DeventryClientId"/>),
/// the choice isn't offered.
/// </summary>
public sealed class GitHubDeviceFlow(HttpClient http, TimeProvider time, string clientId, string userAgent, string baseUrl = GitHubDeviceFlow.GitHubUrl)
{
    public const string GitHubUrl = "https://github.com";

    /// <summary>
    /// The client id of the app dotBunny registers with GitHub for Deventry, with the device flow turned on. None yet:
    /// whether it's a GitHub App or an OAuth app isn't decided (DESIGN.md §18, "Tickets").
    /// </summary>
    public static string? DeventryClientId { get; } = null;

    /// <summary>Deventry has an app to sign in with.</summary>
    public static bool IsAvailable => DeventryClientId is not null;

    /// <summary>What an OAuth app asks for: issues of private repositories need <c>repo</c>. A GitHub App's rights are its own.</summary>
    public const string Scope = "repo";

    /// <summary>A new code to enter at GitHub.</summary>
    public async Task<GitHubDeviceCode> StartAsync(CancellationToken cancellationToken = default)
    {
        var answer = await PostAsync("/login/device/code", new Dictionary<string, string> { ["client_id"] = clientId, ["scope"] = Scope }, cancellationToken)
            .ConfigureAwait(false);
        if (Str(answer, "device_code") is not { Length: > 0 } device || Str(answer, "user_code") is not { Length: > 0 } user
            || Str(answer, "verification_uri") is not { Length: > 0 } uri)
        {
            throw new TicketException(TicketErrorKind.Other, $"GitHub didn't give a code to sign in with{Reason(answer)}.");
        }
        var expires = Number(answer, "expires_in") ?? 900;
        var interval = Number(answer, "interval") ?? 5;
        return new GitHubDeviceCode(user, uri, device, time.GetUtcNow().AddSeconds(expires), TimeSpan.FromSeconds(Math.Max(1, interval)));
    }

    /// <summary>
    /// Waits for the user to enter <paramref name="code"/> at GitHub, asking every few seconds as GitHub says, and gives
    /// back the token. Throws when they refuse it or it runs out.
    /// </summary>
    public async Task<string> WaitForTokenAsync(GitHubDeviceCode code, CancellationToken cancellationToken = default)
    {
        var interval = code.Interval;
        while (true)
        {
            await Task.Delay(interval, time, cancellationToken).ConfigureAwait(false);
            if (time.GetUtcNow() >= code.ExpiresAt)
            {
                throw new TicketException(TicketErrorKind.Refused, "The code ran out before it was entered at GitHub. Start again.");
            }
            var answer = await PostAsync("/login/oauth/access_token", new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["device_code"] = code.DeviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            }, cancellationToken).ConfigureAwait(false);
            if (Str(answer, "access_token") is { Length: > 0 } token)
            {
                return token;
            }
            switch (Str(answer, "error"))
            {
                case "authorization_pending":
                    break;
                case "slow_down":
                    interval = Number(answer, "interval") is { } slower ? TimeSpan.FromSeconds(slower) : interval + TimeSpan.FromSeconds(5);
                    break;
                case "expired_token":
                    throw new TicketException(TicketErrorKind.Refused, "The code ran out before it was entered at GitHub. Start again.");
                case "access_denied":
                    throw new TicketException(TicketErrorKind.Refused, "Signing in was cancelled at GitHub.");
                default:
                    throw new TicketException(TicketErrorKind.Other, $"GitHub didn't sign Deventry in{Reason(answer)}.");
            }
        }
    }

    private async Task<JsonNode?> PostAsync(string path, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(TicketHttp.RequestTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + path) { Content = new FormUrlEncodedContent(form) };
        request.Headers.UserAgent.ParseAdd(userAgent);
        request.Headers.Accept.ParseAdd("application/json");
        try
        {
            using var response = await http.SendAsync(request, linked.Token).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            try
            {
                return JsonTree.Parse(text);
            }
            catch (System.Text.Json.JsonException)
            {
                throw new TicketException(TicketErrorKind.Other, $"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new TicketException(TicketErrorKind.Unreachable, "GitHub didn't answer in time.", inner: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new TicketException(TicketErrorKind.Unreachable, $"Couldn't reach GitHub: {ex.Message}", inner: ex);
        }
    }

    private static string Reason(JsonNode? answer) => Str(answer, "error_description") is { Length: > 0 } said ? $": {said}" : "";

    private static string? Str(JsonNode? node, string name) =>
        node is JsonObject obj && obj[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int? Number(JsonNode? node, string name) =>
        node is JsonObject obj && obj[name] is JsonValue value
            ? value.TryGetValue<int>(out var n) ? n : int.TryParse(Str(node, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null
            : null;
}
