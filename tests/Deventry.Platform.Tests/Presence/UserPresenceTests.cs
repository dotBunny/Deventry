using Deventry.Core.Processes;
using Deventry.Core.Remote;
using Deventry.Platform.Presence;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.Platform.Tests.Presence;

/// <summary>
/// Whether the user is at the computer (DESIGN.md §18, "When remote clients send messages"): what each system can check
/// on its own runner, and Linux's D-Bus answers from recorded <c>gdbus</c> output.
/// </summary>
public class UserPresenceTests
{
    [Theory]
    [InlineData("(<true>,)\n", true)]
    [InlineData("(<false>,)", false)]
    [InlineData("(false,)", false)]
    [InlineData("(true,)", true)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("('nonsense',)", null)]
    public void A_gdbus_boolean_is_read(string? output, bool? value)
    {
        Assert.Equal(value, GdbusReply.Bool(output));
    }

    [Theory]
    [InlineData("(uint64 12345,)", 12345d)]
    [InlineData("(uint32 90,)", 90d)]
    [InlineData("(<uint32 7>,)", 7d)]
    [InlineData("(true,)", null)]
    public void A_gdbus_number_is_read(string output, double? value)
    {
        Assert.Equal(value, GdbusReply.Number(output));
    }

    [Fact]
    public async Task GNOME_answers_locked_the_screen_saver_and_idle_milliseconds()
    {
        var launcher = new AnsweringLauncher(arguments => arguments switch
        {
            _ when arguments.Contains("LockedHint") => "(<true>,)",
            _ when arguments.Contains("IdleHint") => "(<false>,)",
            _ when arguments.Contains("org.freedesktop.ScreenSaver.GetActive") => null,
            _ when arguments.Contains("org.gnome.ScreenSaver.GetActive") => "(false,)",
            _ when arguments.Contains("org.gnome.Mutter.IdleMonitor.GetIdletime") => "(uint64 125000,)",
            _ => null,
        });
        var presence = new LinuxUserPresence(launcher, new FakeTimeProvider());

        var reading = await presence.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new PresenceReading(true, false, false, TimeSpan.FromSeconds(125)), reading);
        Assert.Equal(PresenceSignals.Locked | PresenceSignals.ScreenSaver | PresenceSignals.Idle, presence.Signals);
        Assert.All(launcher.Calls, call => Assert.Equal("call", call[0]));
    }

    [Fact]
    public async Task KDE_answers_idle_seconds()
    {
        var launcher = new AnsweringLauncher(arguments => arguments switch
        {
            _ when arguments.Contains("org.freedesktop.ScreenSaver.GetActive") => "(true,)",
            _ when arguments.Contains("org.freedesktop.ScreenSaver.GetSessionIdleTime") => "(uint32 400,)",
            _ => null,
        });

        var reading = await new LinuxUserPresence(launcher, new FakeTimeProvider()).ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(reading.ScreenSaver);
        Assert.Equal(TimeSpan.FromSeconds(400), reading.Idle);
    }

    [Fact]
    public async Task Logind_idle_counts_as_away_when_no_desktop_gives_the_time()
    {
        var launcher = new AnsweringLauncher(arguments => arguments.Contains("IdleHint") ? "(<true>,)" : null);

        var reading = await new LinuxUserPresence(launcher, new FakeTimeProvider()).ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(RemoteAway.IsAway(true, reading, TimeSpan.FromMinutes(60)));
    }

    [Fact]
    public async Task A_desktop_that_answers_nothing_can_tell_nothing()
    {
        var presence = new LinuxUserPresence(new AnsweringLauncher(_ => null), new FakeTimeProvider());

        var reading = await presence.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PresenceReading.Unknown, reading);
        Assert.Equal(PresenceSignals.None, presence.Signals);
    }

    [Fact]
    public async Task This_system_reads_without_failing_and_listens_and_stops()
    {
        using var presence = UserPresences.CreateForCurrentOS(new AnsweringLauncher(_ => null), TimeProvider.System);

        presence.Start();
        var reading = await presence.ReadAsync(TestContext.Current.CancellationToken);
        presence.Stop();

        if (OperatingSystem.IsWindows())
        {
            // Whoever runs the tests may have used the keyboard a moment ago, or not for hours: only that it's read.
            Assert.NotNull(reading.Idle);
            Assert.True(reading.Idle >= TimeSpan.Zero);
            Assert.Equal(PresenceSignals.Locked | PresenceSignals.ScreenSaver | PresenceSignals.DisplayAsleep | PresenceSignals.Idle, presence.Signals);
        }
    }

    /// <summary>Answers each <c>gdbus</c> call at once from its arguments: the output, or a failure for null.</summary>
    private sealed class AnsweringLauncher(Func<IReadOnlyList<string>, string?> answer) : IProcessLauncher
    {
        private readonly Lock _lock = new();
        private readonly List<IReadOnlyList<string>> _calls = [];

        public IReadOnlyList<IReadOnlyList<string>> Calls
        {
            get
            {
                lock (_lock)
                {
                    return [.. _calls];
                }
            }
        }

        public IRunningProcess Start(ProcessStartSpec spec)
        {
            Assert.Equal("gdbus", spec.FileName);
            lock (_lock)
            {
                _calls.Add(spec.Arguments);
            }
            var process = new Support.FakeRunningProcess(0);
            if (answer(spec.Arguments) is { } output)
            {
                process.WriteOutput(output);
                process.Exit(0);
            }
            else
            {
                process.WriteError("Error: GDBus.Error:org.freedesktop.DBus.Error.ServiceUnknown");
                process.Exit(1);
            }
            return process;
        }
    }
}
