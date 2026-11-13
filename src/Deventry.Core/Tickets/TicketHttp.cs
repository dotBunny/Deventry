using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Deventry.Core.Json;

namespace Deventry.Core.Tickets;

/// <summary>A tracker's answer: its status, its headers Deventry reads, and its JSON (null for none, or a <c>304</c>).</summary>
public sealed record TicketResponse(HttpStatusCode Status, HttpResponseHeaders Headers, JsonNode? Json);

/// <summary>
/// The requests both trackers make (DESIGN.md §18, "Tickets", "Requests"): each with its deadline on the injected
/// clock, its token in a header and never in an address, and what went wrong as a <see cref="TicketException"/> whose
/// words never hold the token.
/// </summary>
public sealed class TicketHttp(HttpClient http, TimeProvider time, string trackerName, string? token, string userAgent)
{
    /// <summary>A request's deadline.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public string TrackerName => trackerName;

    public string? Token => token;

    /// <summary>
    /// Sends a request, and gives back its answer when it succeeded or was <c>304 Not Modified</c>; throws why not
    /// otherwise. <paramref name="addHeaders"/> adds the tracker's own, such as GitHub's <c>If-None-Match</c>.
    /// </summary>
    /// <param name="describeError">Reads the tracker's reason from an error's JSON; null when it gives none.</param>
    public async Task<TicketResponse> SendAsync(HttpMethod method, string url, JsonNode? body, Action<HttpRequestHeaders>? addHeaders,
        Func<JsonNode?, string?> describeError, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(RequestTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        using var request = new HttpRequestMessage(method, url);
        request.Headers.UserAgent.ParseAdd(userAgent);
        request.Headers.Accept.ParseAdd("application/json");
        if (token is { Length: > 0 })
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        addHeaders?.Invoke(request.Headers);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }
        HttpStatusCode status;
        HttpResponseHeaders headers;
        string text;
        try
        {
            using var response = await http.SendAsync(request, linked.Token).ConfigureAwait(false);
            status = response.StatusCode;
            headers = response.Headers;
            text = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new TicketException(TicketErrorKind.Unreachable, $"{trackerName} didn't answer in time.", inner: Scrubbed(ex));
        }
        catch (HttpRequestException ex)
        {
            throw new TicketException(TicketErrorKind.Unreachable, $"Couldn't reach {trackerName}: {Scrub(ex.Message)}", inner: Scrubbed(ex));
        }
        JsonNode? json;
        try
        {
            json = text.Length == 0 ? null : JsonTree.Parse(text);
        }
        catch (JsonException)
        {
            json = null;
        }
        if ((int)status is >= 200 and < 300 || status == HttpStatusCode.NotModified)
        {
            return new TicketResponse(status, headers, status == HttpStatusCode.NotModified ? null : json);
        }
        var reason = describeError(json) is { Length: > 0 } said ? Scrub(said) : $"{(int)status} {Scrub(ReasonOf(status))}";
        throw status switch
        {
            HttpStatusCode.Unauthorized => new TicketException(TicketErrorKind.Refused, $"{trackerName} didn't accept the token: {reason}"),
            HttpStatusCode.NotFound => new TicketException(TicketErrorKind.NotFound, $"{trackerName} didn't find it: {reason}"),
            HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests when RateLimitReset(headers) is { } until =>
                new TicketException(TicketErrorKind.RateLimited, $"{trackerName}'s limit is used up: {reason}", until),
            HttpStatusCode.Forbidden => new TicketException(TicketErrorKind.Forbidden, $"{trackerName} refused: {reason}"),
            _ => new TicketException(TicketErrorKind.Other, $"{trackerName} answered {reason}"),
        };
    }

    /// <summary>
    /// When a used-up limit resets: <c>Retry-After</c>'s seconds, or with <c>X-RateLimit-Remaining</c> at 0,
    /// <c>X-RateLimit-Reset</c>'s time; null when the answer isn't about a limit.
    /// </summary>
    public DateTimeOffset? RateLimitReset(HttpResponseHeaders headers)
    {
        if (headers.RetryAfter is { } retry)
        {
            return retry.Delta is { } delta ? time.GetUtcNow() + delta : retry.Date;
        }
        if (Header(headers, "X-RateLimit-Remaining") == "0"
            && long.TryParse(Header(headers, "X-RateLimit-Reset"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        return null;
    }

    /// <summary>A header's first value; null when it isn't there.</summary>
    public static string? Header(HttpResponseHeaders headers, string name) =>
        headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static string ReasonOf(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Unauthorized",
        HttpStatusCode.Forbidden => "Forbidden",
        HttpStatusCode.NotFound => "Not Found",
        HttpStatusCode.TooManyRequests => "Too Many Requests",
        HttpStatusCode.UnprocessableEntity => "Unprocessable Entity",
        HttpStatusCode.Gone => "Gone",
        _ => status.ToString(),
    };

    /// <summary>Takes the token out of anything that may have quoted it.</summary>
    public string Scrub(string text) => token is { Length: > 0 } secret ? text.Replace(secret, "<token>", StringComparison.Ordinal) : text;

    private Exception Scrubbed(Exception ex) => token is { Length: > 0 } secret && ex.Message.Contains(secret, StringComparison.Ordinal)
        ? new HttpRequestException(Scrub(ex.Message))
        : ex;
}
