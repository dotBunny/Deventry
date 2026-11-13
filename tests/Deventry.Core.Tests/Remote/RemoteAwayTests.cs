using Deventry.Core.Remote;

namespace Deventry.Core.Tests.Remote;

/// <summary>When the user is away, and whether a message goes (DESIGN.md §18, "When remote clients send messages").</summary>
public class RemoteAwayTests
{
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    [Fact]
    public void At_the_desk_is_Deventry_in_front_unlocked_awake_and_used_lately()
    {
        Assert.False(RemoteAway.IsAway(true, new PresenceReading(false, false, false, TimeSpan.FromMinutes(1)), FiveMinutes));
        Assert.False(RemoteAway.IsAway(true, PresenceReading.Unknown, FiveMinutes));
    }

    [Theory]
    [InlineData(false, false, false, false, 0)]
    [InlineData(true, true, false, false, 0)]
    [InlineData(true, false, true, false, 0)]
    [InlineData(true, false, false, true, 0)]
    [InlineData(true, false, false, false, 5)]
    public void Any_one_of_them_is_away(bool inFront, bool locked, bool screenSaver, bool asleep, int idleMinutes)
    {
        Assert.True(RemoteAway.IsAway(inFront, new PresenceReading(locked, screenSaver, asleep, TimeSpan.FromMinutes(idleMinutes)), FiveMinutes));
    }

    [Theory]
    [InlineData(false, true, RemoteSendWhen.Away, RemoteNotice.None)]
    [InlineData(true, true, RemoteSendWhen.Away, RemoteNotice.Sound)]
    [InlineData(true, false, RemoteSendWhen.Away, RemoteNotice.None)]
    [InlineData(true, false, RemoteSendWhen.Always, RemoteNotice.Silent)]
    [InlineData(true, true, RemoteSendWhen.Always, RemoteNotice.Sound)]
    public void A_wanted_event_is_loud_away_silent_at_the_desk_with_Always_and_not_sent_otherwise(bool wanted, bool away, RemoteSendWhen when, RemoteNotice expected)
    {
        Assert.Equal(expected, RemoteAway.Notice(wanted, away, when));
    }

    [Fact]
    public void The_minutes_are_kept_between_1_and_60()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), RemoteAway.After(0));
        Assert.Equal(TimeSpan.FromMinutes(60), RemoteAway.After(600));
    }

    [Fact]
    public void Settings_says_what_this_desktop_can_tell()
    {
        Assert.Equal(
            "Away is when Deventry isn't in front, the computer is locked, its screen saver is on, its display is asleep or nobody has used its keyboard or mouse for 5 minutes.",
            RemoteAway.Describe(PresenceSignals.Locked | PresenceSignals.ScreenSaver | PresenceSignals.DisplayAsleep | PresenceSignals.Idle, 5));
        Assert.Contains("so that's all Deventry can tell", RemoteAway.Describe(PresenceSignals.None, 5));
    }
}
