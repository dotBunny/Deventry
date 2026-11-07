using Deventry.App.Games;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>
/// Hangman against Joulene (DESIGN.md §18, "Games with Joulene"): she thinks of a word, mostly from programming,
/// and the user guesses its letters. Six misses draw the figure on the gallows, and lose.
/// </summary>
public sealed partial class HangmanViewModel : GameViewModel
{
    private Hangman? _round;

    public HangmanViewModel(IGameHost host) : base(host, GameKind.Hangman)
    {
        Letters = [.. Enumerable.Range('A', 26).Select(c => new HangmanLetter(this, (char)c))];
        NewRound();
    }

    public override string Title => "Hangman";

    protected override bool HasDraws => false;

    /// <summary>The letters A to Z, to guess with.</summary>
    public IReadOnlyList<HangmanLetter> Letters { get; }

    /// <summary>The word as far as it's guessed, spaced out: "C _ M P _ L _ R". Blank while she thinks of one.</summary>
    [ObservableProperty]
    public partial string Pattern { get; private set; } = "";

    /// <summary>What she says about the word: "A programming word, 8 letters".</summary>
    [ObservableProperty]
    public partial string? Hint { get; private set; }

    /// <summary>The word a letter at a time, each on its line: blank till it's guessed, and once she's won, the rest in her colour.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<HangmanSlot> Slots { get; private set; } = [];

    /// <summary>The word, for screen readers: "8 letters: C, blank, M, P, blank, L, blank, R".</summary>
    [ObservableProperty]
    public partial string? PatternName { get; private set; }

    /// <summary>How many guesses missed: the figure has that many parts.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsHead), nameof(ShowsBody), nameof(ShowsLeftArm), nameof(ShowsRightArm), nameof(ShowsLeftLeg), nameof(ShowsRightLeg),
        nameof(FigureName))]
    public partial int Misses { get; private set; }

    public bool ShowsHead => Misses >= 1;

    public bool ShowsBody => Misses >= 2;

    public bool ShowsLeftArm => Misses >= 3;

    public bool ShowsRightArm => Misses >= 4;

    public bool ShowsLeftLeg => Misses >= 5;

    public bool ShowsRightLeg => Misses >= 6;

    /// <summary>The drawing, for screen readers.</summary>
    public string FigureName => Misses == 0 ? "The gallows, empty" : $"The gallows, {Misses} of {Hangman.MaxMisses} misses drawn";

    /// <summary>The word being guessed, for tests.</summary>
    internal string? Word => _round?.Word;

    protected override void StartRound(bool youStart)
    {
        // She always thinks of the word; who starts doesn't apply.
        _round = null;
        Misses = 0;
        Pattern = "";
        Hint = null;
        PatternName = null;
        Slots = [];
        RefreshLetters();
        HerTurn(ThinkOfWord, "Joulene is thinking of a word…");
    }

    private void ThinkOfWord()
    {
        _round = new Hangman(Host.NextWord());
        var kind = _round.Category == HangmanCategory.Programming ? "A programming word" : "An everyday word";
        Hint = $"{kind}, {_round.Word.Length} letters";
        Refresh();
        Status = "Guess a letter.";
        Host.Announce($"{Hint}.");
    }

    /// <summary>The user guesses <paramref name="letter"/>, unless it's been guessed or the round is over.</summary>
    public void Guess(char letter)
    {
        if (IsHerTurn || _round is not { IsOver: false } round || round.Guess(letter) is not { } found)
        {
            return;
        }
        letter = char.ToUpperInvariant(letter);
        Host.Moved();
        Refresh();
        if (round.IsSolved)
        {
            Finish(GameOutcome.Won, $"You got it! It was {round.Word}.");
            return;
        }
        if (round.IsLost)
        {
            Pattern = Spaced(round.Word);
            PatternName = $"{round.Word.Length} letters: {string.Join(", ", round.Word.ToCharArray())}";
            Slots = [.. round.Word.Zip(round.Pattern, (letter, shown) => new HangmanSlot(letter.ToString(), Missed: shown == '_'))];
            Finish(GameOutcome.Lost, $"Joulene wins! It was {round.Word}.");
            return;
        }
        var left = round.MissesLeft == 1 ? "One miss left" : $"{round.MissesLeft} misses left";
        if (found > 0)
        {
            Status = found == 1 ? $"Yes, there's {Article(letter)} {letter}." : $"Yes, there are {found} {letter}'s.";
        }
        else
        {
            Status = $"No {letter}. {left}.";
            if (round.MissesLeft == 1)
            {
                Host.CloseCall();
            }
        }
        Host.Announce(Status);
    }

    public override bool Press(char key)
    {
        if (!char.IsAsciiLetter(key))
        {
            return false;
        }
        Guess(key);
        return true;
    }

    private void Refresh()
    {
        if (_round is not { } round)
        {
            return;
        }
        Misses = round.Misses;
        Pattern = Spaced(round.Pattern);
        Slots = [.. round.Pattern.Select(c => new HangmanSlot(c == '_' ? "" : c.ToString(), Missed: false))];
        PatternName = $"{round.Word.Length} letters: {string.Join(", ", round.Pattern.Select(c => c == '_' ? "blank" : c.ToString()))}";
        RefreshLetters();
    }

    private void RefreshLetters()
    {
        foreach (var letter in Letters)
        {
            letter.State = _round is not { } round || !round.HasGuessed(letter.Letter) ? HangmanLetterState.NotGuessed
                : round.Word.Contains(letter.Letter) ? HangmanLetterState.Found : HangmanLetterState.Missed;
        }
    }

    private static string Spaced(string text) => string.Join(' ', text.ToCharArray());

    /// <summary>"an" before a letter whose name starts with a vowel sound (an F, an R), "a" otherwise.</summary>
    private static string Article(char letter) => "AEFHILMNORSX".Contains(letter) ? "an" : "a";
}

/// <summary>A letter of the word: blank till it's guessed; <paramref name="Missed"/>, one she showed once she'd won.</summary>
public sealed record HangmanSlot(string Letter, bool Missed);

public enum HangmanLetterState
{
    NotGuessed,
    Found,
    Missed,
}

/// <summary>A letter to guess with.</summary>
public sealed partial class HangmanLetter(HangmanViewModel game, char letter) : ObservableObject
{
    public char Letter => letter;

    public string Text => letter.ToString();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGuessed), nameof(IsFound), nameof(IsMissed), nameof(Name))]
    public partial HangmanLetterState State { get; internal set; }

    public bool IsGuessed => State != HangmanLetterState.NotGuessed;

    public bool IsFound => State == HangmanLetterState.Found;

    public bool IsMissed => State == HangmanLetterState.Missed;

    /// <summary>"Letter E", or what guessing it found.</summary>
    public string Name => State switch
    {
        HangmanLetterState.Found => $"{letter}, in the word",
        HangmanLetterState.Missed => $"{letter}, not in the word",
        _ => $"Letter {letter}",
    };

    [RelayCommand]
    private void Guess() => game.Guess(letter);
}
