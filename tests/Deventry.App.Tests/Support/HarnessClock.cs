using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.App.Tests.Support;

/// <summary>
/// The harness's clock: a <see cref="FakeTimeProvider"/> whose <see cref="Advance"/> returns once every timer it brought
/// due has run. FakeTimeProvider runs due timers on whichever thread gets to them first, one thread at a time, and reading
/// the clock is one of the ways there: a background thread that reads it just as a test advances it runs what came due on
/// its own, while the test's Advance returns and the test looks for what they did.
/// </summary>
internal sealed class HarnessClock(DateTimeOffset start) : FakeTimeProvider(start)
{
    // How many timers' callbacks this thread is in: an Advance from one can't wait for the timers after it.
    [ThreadStatic]
    private static int _inCallback;

    /// <summary>Advances the clock, and waits for the timers that came due to have run, wherever they run.</summary>
    /// <exception cref="Xunit.Sdk.XunitException">
    /// It was advanced from under the UI lock (<see cref="InlineDispatcher"/>), where waiting for a timer another thread
    /// runs, which itself waits for the lock, would hold every test in the process for <see cref="Waiting.Timeout"/>.
    /// </exception>
    public new void Advance(TimeSpan delta)
    {
        if (_inCallback == 0 && InlineDispatcher.IsOnUiThread)
        {
            Assert.Fail("The harness's clock was advanced under the UI lock, as from a condition of TabTestHarness.Eventually: "
                + "advance it in Eventually's poll instead, outside the lock.");
        }
        base.Advance(delta);
        Settle();
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        base.CreateTimer(s =>
        {
            _inCallback++;
            try
            {
                callback(s);
            }
            finally
            {
                _inCallback--;
            }
        }, state, dueTime, period);

    /// <summary>
    /// Waits until no timer that's due is waiting or running. A timer due now runs on this thread as it's made only when no
    /// other thread is running timers, and then this thread runs every timer that's due before it's done: once one does,
    /// the clock has settled. Otherwise the thread that is running them runs it too, last of those due before it, and once
    /// it has, the clock is looked at again.
    /// </summary>
    private void Settle()
    {
        if (_inCallback > 0)
        {
            return;
        }
        var thread = Environment.CurrentManagedThreadId;
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var ranHere = false;
            using var ran = new ManualResetEventSlim();
            var probe = base.CreateTimer(_ =>
            {
                ranHere = Environment.CurrentManagedThreadId == thread;
                ran.Set();
            }, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
            try
            {
                if (ranHere)
                {
                    return;
                }
                var left = Waiting.Timeout - clock.Elapsed;
                if (left <= TimeSpan.Zero || !ran.Wait(left))
                {
                    Assert.Fail($"Timed out after {Waiting.Timeout.TotalSeconds:0.#} s waiting for the timers that came due to run.");
                }
            }
            finally
            {
                probe.Dispose();
            }
        }
    }
}
