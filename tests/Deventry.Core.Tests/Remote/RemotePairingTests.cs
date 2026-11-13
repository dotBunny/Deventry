using Deventry.Core.Remote;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.Core.Tests.Remote;

/// <summary>Pairing a remote client with the user's own account (DESIGN.md §18, "Pairing").</summary>
public class RemotePairingTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 12, 14, 0, 0, TimeSpan.Zero));

    private RemotePairing Pairing(params RemoteAccount[] accounts) => new("telegram", _clock, accounts, () => "7KQ2M9XD");

    [Fact]
    public void A_code_is_8_characters_without_look_alikes_and_shown_in_halves()
    {
        var code = new RemotePairing("telegram", _clock, []).Start();

        Assert.Equal(RemotePairing.CodeLength, code.Code.Length);
        Assert.All(code.Code, c => Assert.Contains(c, RemotePairing.Alphabet));
        Assert.DoesNotContain('0', RemotePairing.Alphabet);
        Assert.DoesNotContain('O', RemotePairing.Alphabet);
        Assert.DoesNotContain('1', RemotePairing.Alphabet);
        Assert.DoesNotContain('I', RemotePairing.Alphabet);
        Assert.DoesNotContain('L', RemotePairing.Alphabet);
        Assert.Equal($"{code.Code[..4]}-{code.Code[4..]}", code.Display);
    }

    [Fact]
    public void The_right_code_pairs_the_account_once_and_the_code_stops_working()
    {
        var pairing = Pairing();
        var changes = 0;
        pairing.Changed += () => changes++;
        pairing.Start();

        Assert.Equal(PairingOutcome.Paired, pairing.TryPair("7kq2-m9xd", 11, "Matt", "reapazor"));

        var account = Assert.Single(pairing.Accounts);
        Assert.Equal(11, account.UserId);
        Assert.Equal("Matt (@reapazor)", account.Display);
        Assert.Equal(_clock.GetUtcNow(), account.PairedAt);
        Assert.True(pairing.IsPaired(11));
        Assert.Null(pairing.Current);
        Assert.Equal(PairingOutcome.Refused, pairing.TryPair("7KQ2M9XD", 12, "Someone", null));
        Assert.Equal(2, changes);
    }

    [Fact]
    public void A_code_expires_after_5_minutes()
    {
        var pairing = Pairing();
        pairing.Start();

        _clock.Advance(RemotePairing.CodeLifetime);

        Assert.Null(pairing.Current);
        Assert.Equal(PairingOutcome.Refused, pairing.TryPair("7KQ2M9XD", 11, "Matt", null));
        Assert.Empty(pairing.Accounts);
    }

    [Fact]
    public void Without_a_pairing_under_way_every_code_is_refused()
    {
        Assert.Equal(PairingOutcome.Refused, Pairing().TryPair("7KQ2M9XD", 11, "Matt", null));
    }

    [Fact]
    public void Five_wrong_codes_within_10_minutes_end_the_pairing()
    {
        var pairing = Pairing();
        pairing.Start();

        for (var i = 0; i < RemotePairing.WrongCodeLimit - 1; i++)
        {
            Assert.Equal(PairingOutcome.Refused, pairing.TryPair("AAAAAAAA", 99, "Stranger", null));
        }
        Assert.Equal(PairingOutcome.Ended, pairing.TryPair("AAAAAAAA", 99, "Stranger", null));

        Assert.Null(pairing.Current);
        Assert.Equal(PairingOutcome.Refused, pairing.TryPair("7KQ2M9XD", 11, "Matt", null));
    }

    [Fact]
    public void Wrong_codes_more_than_10_minutes_apart_do_not_end_it()
    {
        var pairing = Pairing();
        pairing.Start();
        for (var i = 0; i < RemotePairing.WrongCodeLimit - 1; i++)
        {
            pairing.TryPair("AAAAAAAA", 99, "Stranger", null);
        }

        // The code itself lives 5 minutes: a new one keeps the count, as Start clears it.
        _clock.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(PairingOutcome.Paired, pairing.TryPair("7KQ2M9XD", 11, "Matt", null));
    }

    [Fact]
    public void An_account_already_paired_is_told_so_whatever_it_sends()
    {
        var pairing = Pairing(new RemoteAccount { Client = "telegram", UserId = 11, Name = "Matt" });

        Assert.Equal(PairingOutcome.AlreadyPaired, pairing.TryPair("nonsense", 11, "Matt", null));
    }

    [Fact]
    public void Unpairing_stops_answering_the_account()
    {
        var pairing = Pairing(new RemoteAccount { Client = "telegram", UserId = 11, Name = "Matt" });

        Assert.True(pairing.Unpair(11));

        Assert.False(pairing.IsPaired(11));
        Assert.False(pairing.Unpair(11));
    }

    [Fact]
    public void Only_the_clients_own_accounts_count()
    {
        var pairing = Pairing(new RemoteAccount { Client = "discord", UserId = 11, Name = "Matt" });

        Assert.False(pairing.IsPaired(11));
    }

    [Fact]
    public void Cancel_stops_the_code_working()
    {
        var pairing = Pairing();
        pairing.Start();

        pairing.Cancel();

        Assert.Equal(PairingOutcome.Refused, pairing.TryPair("7KQ2M9XD", 11, "Matt", null));
    }
}
