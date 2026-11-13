using System.Security.Cryptography;

namespace Deventry.Core.Remote;

/// <summary>An account paired with a remote client: only these are answered (DESIGN.md §18, "Pairing").</summary>
public sealed class RemoteAccount
{
    /// <summary>The client it's paired with: <c>telegram</c>.</summary>
    public string Client { get; set; } = "";

    /// <summary>Its id in the client, which doesn't change: a Telegram user id.</summary>
    public long UserId { get; set; }

    /// <summary>Its name when it paired, which can change.</summary>
    public string Name { get; set; } = "";

    /// <summary>Its <c>@username</c> when it paired, without the @; null when it has none.</summary>
    public string? Username { get; set; }

    public DateTimeOffset PairedAt { get; set; }

    /// <summary>"Matt (@reapazor)".</summary>
    public string Display => Username is { Length: > 0 } username ? $"{Name} (@{username})" : Name;
}

/// <summary>A pairing code, while it can be used.</summary>
/// <param name="Code">The 8 characters, as sent: <c>7KQ2M9XD</c>.</param>
public sealed record PairingCode(string Code, DateTimeOffset ExpiresAt)
{
    /// <summary>As Settings shows it, in two halves: <c>7KQ2-M9XD</c>.</summary>
    public string Display => $"{Code[..4]}-{Code[4..]}";
}

/// <summary>What a code sent to the bot did.</summary>
public enum PairingOutcome
{
    /// <summary>The account is paired now.</summary>
    Paired,

    /// <summary>It was already paired.</summary>
    AlreadyPaired,

    /// <summary>Not the code, or none is waiting, or it expired: the sender gets no answer.</summary>
    Refused,

    /// <summary>The fifth wrong code in 10 minutes: the pairing ended, and Settings starts a new one.</summary>
    Ended,
}

/// <summary>
/// Pairing a remote client with the user's own account (DESIGN.md §18, "Pairing"): a code of 8 characters without
/// look-alikes, which works once, for 5 minutes; five wrong codes within 10 minutes end it. Keeps the paired accounts,
/// which the App saves in the state when <see cref="Changed"/> says. Safe from any thread: the client checks codes on
/// its own, and Settings starts and cancels them on the UI thread.
/// </summary>
public sealed class RemotePairing
{
    /// <summary>The code's letters and digits: none that look alike (0 and O, 1, I and L).</summary>
    public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    public const int CodeLength = 8;

    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);

    /// <summary>How many wrong codes end a pairing, within <see cref="WrongCodeWindow"/>.</summary>
    public const int WrongCodeLimit = 5;

    public static readonly TimeSpan WrongCodeWindow = TimeSpan.FromMinutes(10);

    private readonly Lock _lock = new();
    private readonly TimeProvider _time;
    private readonly Func<string> _newCode;
    private readonly List<RemoteAccount> _accounts;
    private readonly List<DateTimeOffset> _wrong = [];
    private PairingCode? _code;

    /// <param name="client">The client it pairs: <c>telegram</c>.</param>
    /// <param name="accounts">The accounts paired before, from the state.</param>
    /// <param name="newCode">Makes a code; by default, from a cryptographic random source. Tests give their own.</param>
    public RemotePairing(string client, TimeProvider time, IEnumerable<RemoteAccount> accounts, Func<string>? newCode = null)
    {
        Client = client;
        _time = time;
        _newCode = newCode ?? (() => RandomNumberGenerator.GetString(Alphabet, CodeLength));
        _accounts = accounts.Where(a => a.Client == client).ToList();
    }

    public string Client { get; }

    /// <summary>Raised on whichever thread changed it: the code, or the accounts.</summary>
    public event Action? Changed;

    /// <summary>The code waiting to be sent, or null when there's none or it expired.</summary>
    public PairingCode? Current
    {
        get
        {
            lock (_lock)
            {
                return _code is { } code && code.ExpiresAt > _time.GetUtcNow() ? code : null;
            }
        }
    }

    public IReadOnlyList<RemoteAccount> Accounts
    {
        get
        {
            lock (_lock)
            {
                return [.. _accounts];
            }
        }
    }

    public bool IsPaired(long userId)
    {
        lock (_lock)
        {
            return _accounts.Any(a => a.UserId == userId);
        }
    }

    /// <summary><b>Pair a phone…</b>: a new code, replacing any before it.</summary>
    public PairingCode Start()
    {
        PairingCode code;
        lock (_lock)
        {
            var text = Normalize(_newCode());
            if (text.Length != CodeLength || text.Any(c => !Alphabet.Contains(c)))
            {
                throw new InvalidOperationException("A pairing code must be 8 characters of the pairing alphabet.");
            }
            _code = code = new PairingCode(text, _time.GetUtcNow() + CodeLifetime);
            _wrong.Clear();
        }
        Changed?.Invoke();
        return code;
    }

    /// <summary><b>Cancel</b>: the code stops working.</summary>
    public void Cancel()
    {
        lock (_lock)
        {
            if (_code is null)
            {
                return;
            }
            _code = null;
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// An account sent a code. The right one, in time, pairs it and ends the pairing; a wrong one counts toward
    /// <see cref="WrongCodeLimit"/>, and an account already paired is told so whatever it sent.
    /// </summary>
    public PairingOutcome TryPair(string? sent, long userId, string name, string? username)
    {
        var outcome = Check(sent, userId, name, username);
        if (outcome is PairingOutcome.Paired or PairingOutcome.Ended)
        {
            Changed?.Invoke();
        }
        return outcome;
    }

    private PairingOutcome Check(string? sent, long userId, string name, string? username)
    {
        lock (_lock)
        {
            if (_accounts.Any(a => a.UserId == userId))
            {
                return PairingOutcome.AlreadyPaired;
            }
            var now = _time.GetUtcNow();
            if (_code is not { } code || code.ExpiresAt <= now)
            {
                return PairingOutcome.Refused;
            }
            if (sent is not null && Normalize(sent) is { Length: CodeLength } typed
                && CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(typed), System.Text.Encoding.UTF8.GetBytes(code.Code)))
            {
                _code = null;
                _wrong.Clear();
                _accounts.Add(new RemoteAccount { Client = Client, UserId = userId, Name = name, Username = username, PairedAt = now });
                return PairingOutcome.Paired;
            }
            _wrong.RemoveAll(at => now - at >= WrongCodeWindow);
            _wrong.Add(now);
            if (_wrong.Count < WrongCodeLimit)
            {
                return PairingOutcome.Refused;
            }
            _code = null;
            _wrong.Clear();
            return PairingOutcome.Ended;
        }
    }

    /// <summary><b>Unpair…</b>: the account is answered no more. Returns whether it was paired.</summary>
    public bool Unpair(long userId)
    {
        lock (_lock)
        {
            if (_accounts.RemoveAll(a => a.UserId == userId) == 0)
            {
                return false;
            }
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>A code as typed: any case, with the hyphen and spaces Settings shows it with.</summary>
    public static string Normalize(string code) =>
        new([.. code.Where(c => !char.IsWhiteSpace(c) && c != '-').Select(char.ToUpperInvariant)]);
}
