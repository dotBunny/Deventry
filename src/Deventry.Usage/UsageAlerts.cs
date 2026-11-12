using System.Globalization;

namespace Deventry.Usage;

public enum UsageAlertKind
{
    ThresholdCrossed,
    ProjectedToHitLimit,
    LimitReset,

    /// <summary>Usage credits started paying for Claude (DESIGN.md §6, "Usage credits"). An OS notification only: the notice under the header says it already.</summary>
    UsingCredits,
}

/// <summary>Something worth an OS notification (DESIGN.md §6, "Alerts"; §10).</summary>
/// <param name="Threshold">The level crossed, for <see cref="UsageAlertKind.ThresholdCrossed"/>.</param>
/// <param name="Reading">The limit that reset, as it read then, for <see cref="UsageAlertKind.LimitReset"/>.</param>
public sealed record UsageAlert(UsageAlertKind Kind, string Title, string Message, double? Threshold = null, LimitReading? Reading = null)
{
    /// <summary>
    /// Whether the usage credits notice says more than this alert does, so it gives way to the notice: a threshold or
    /// projection alert about the session, which credits paying means is used up.
    /// </summary>
    public bool GivesWayToCredits => Kind is UsageAlertKind.ThresholdCrossed or UsageAlertKind.ProjectedToHitLimit;
}

/// <summary>
/// Decides when usage is worth an alert (DESIGN.md §6, "Alerts"). Feed it every snapshot; it remembers what it has
/// already said.
/// <list type="bullet">
/// <item>The session crossing the warning and then the critical threshold, upward, once each per session window.</item>
/// <item>The projection first saying the limit will be hit before the reset, once per session window.</item>
/// <item>Any limit resetting: its reset time moves forward and its percentage drops.</item>
/// <item>Usage credits starting to pay (<see cref="CreditUse.From"/>), once each time they do.</item>
/// </list>
/// A session window is identified by its reset time. The first session reading after a restart seeds the state
/// without alerting, so levels that were already crossed before the restart stay quiet; so does the first reading that
/// says anything about usage credits.
/// </summary>
/// <param name="clock">For when a limit resets, in its time zone, in a credits alert.</param>
public sealed class UsageAlerts(TimeProvider clock)
{
    /// <summary>
    /// Sources report the same reset a little differently (<c>/usage</c> shows minutes only), so reset times this
    /// close are the same window. A real reset moves it by hours.
    /// </summary>
    public static readonly TimeSpan SameWindowTolerance = TimeSpan.FromMinutes(10);

    private readonly Lock _lock = new();
    private readonly Dictionary<(LimitKind, string), LimitReading> _last = [];
    private bool _sessionSeeded;
    private DateTimeOffset? _sessionWindow;
    private bool _warnFired;
    private bool _criticalFired;
    private bool _projectionFired;
    private bool _creditsSeeded;
    private bool _usingCredits;

    public IReadOnlyList<UsageAlert> Observe(UsageSnapshot snapshot, BurnProjection? sessionProjection, double warn, double critical)
    {
        var alerts = new List<UsageAlert>();
        lock (_lock)
        {
            foreach (var reading in snapshot.Limits)
            {
                var key = (reading.Kind, reading.Label.ToUpperInvariant());
                if (_last.TryGetValue(key, out var previous) && IsNewWindow(previous.ResetsAt, reading.ResetsAt) && reading.Percent < previous.Percent)
                {
                    alerts.Add(ResetAlert(reading));
                }
                _last[key] = reading;
            }

            if (snapshot.Credits is not null || snapshot.CreditsReport is not null)
            {
                var use = CreditUse.From(snapshot, snapshot.AsOf);
                if (use is not null && _creditsSeeded && !_usingCredits)
                {
                    alerts.Add(new UsageAlert(UsageAlertKind.UsingCredits, CreditUse.Title, use.Describe(clock)));
                }
                _creditsSeeded = true;
                _usingCredits = use is not null;
            }

            if (snapshot.Session is not { } session)
            {
                return alerts;
            }
            if (!_sessionSeeded)
            {
                _sessionSeeded = true;
                _sessionWindow = session.ResetsAt;
                _warnFired = session.Percent >= warn;
                _criticalFired = session.Percent >= critical;
            }
            else
            {
                if (IsNewWindow(_sessionWindow, session.ResetsAt))
                {
                    _warnFired = _criticalFired = _projectionFired = false;
                }
                if (_sessionWindow is null || IsNewWindow(_sessionWindow, session.ResetsAt))
                {
                    _sessionWindow = session.ResetsAt;
                }

                if (session.Percent >= critical && !_criticalFired)
                {
                    // Jumping straight past both thresholds is one alert, not two.
                    _criticalFired = _warnFired = true;
                    alerts.Add(ThresholdAlert(session, critical, snapshot.AsOf));
                }
                else if (session.Percent >= warn && !_warnFired)
                {
                    _warnFired = true;
                    alerts.Add(ThresholdAlert(session, warn, snapshot.AsOf));
                }
            }

            if (sessionProjection is { HitsLimitBeforeReset: true } && session.Percent < 100 && !_projectionFired)
            {
                _projectionFired = true;
                alerts.Add(ProjectionAlert(sessionProjection));
            }
        }
        return alerts;
    }

    private static bool IsNewWindow(DateTimeOffset? previous, DateTimeOffset? current) =>
        previous is { } before && current is { } after && after - before > SameWindowTolerance;

    /// <summary>
    /// An alert brought up to date, so the line under the header says what the header says, or null once it no longer
    /// holds:
    /// <list type="bullet">
    /// <item>A threshold alert follows the session's reading, until the window it was about resets: its reset time
    /// passes, or a new window's reading is below the threshold.</item>
    /// <item>A projection alert follows <paramref name="projection"/>, until it no longer hits the limit before the reset.</item>
    /// <item>A reset alert follows the reading of the limit that reset.</item>
    /// </list>
    /// Without the reading it follows, an alert stays as it is.
    /// </summary>
    public static UsageAlert? Refresh(UsageAlert alert, UsageSnapshot snapshot, BurnProjection? projection, DateTimeOffset now) =>
        alert switch
        {
            { Kind: UsageAlertKind.ThresholdCrossed, Threshold: { } threshold } when snapshot.Session is { } session =>
                session.Percent >= threshold && (session.ResetsAt is null || session.ResetsAt > now) ? ThresholdAlert(session, threshold, now) : null,
            { Kind: UsageAlertKind.ProjectedToHitLimit } when snapshot.Session is { } session && projection is not null =>
                projection.HitsLimitBeforeReset && session.Percent < 100 ? ProjectionAlert(projection) : null,
            { Kind: UsageAlertKind.LimitReset, Reading: { } reset } when snapshot.Limits.FirstOrDefault(l => l.Kind == reset.Kind && l.Label.Equals(reset.Label, StringComparison.OrdinalIgnoreCase)) is { } reading =>
                ResetAlert(reading),
            _ => alert,
        };

    private static UsageAlert ProjectionAlert(BurnProjection projection) =>
        new(UsageAlertKind.ProjectedToHitLimit, "On pace to hit the session limit", projection.Describe());

    private static UsageAlert ThresholdAlert(LimitReading session, double threshold, DateTimeOffset now)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"Session usage passed {threshold:0.#}%");
        // Rounded as the meter rounds it.
        var used = string.Create(CultureInfo.InvariantCulture, $"You've used {session.Percent:0}% of your session limit.");
        var message = session.ResetsAt is { } resetsAt && resetsAt > now
            ? $"{used} It resets in {BurnRate.FormatCountdown(resetsAt - now)}."
            : used;
        return new UsageAlert(UsageAlertKind.ThresholdCrossed, title, message, threshold);
    }

    private static UsageAlert ResetAlert(LimitReading reading)
    {
        var name = reading.Kind switch
        {
            LimitKind.Session => "Session",
            LimitKind.WeeklyAll => "Weekly",
            _ => $"{reading.Label} weekly",
        };
        return new UsageAlert(
            UsageAlertKind.LimitReset,
            $"{name} limit reset",
            string.Create(CultureInfo.InvariantCulture, $"{name} usage is now {reading.Percent:0}%."),
            Reading: reading);
    }
}
