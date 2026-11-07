using Deventry.App.Games;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>
/// Tic-tac-toe against Joulene (DESIGN.md §18, "Games with Joulene"): the user is ✕, she's ○. The three in a row that
/// win are struck through.
/// </summary>
public sealed partial class TicTacToeViewModel : GameViewModel
{
    private static readonly string[] SquareNames =
        ["top left", "top middle", "top right", "middle left", "middle", "middle right", "bottom left", "bottom middle", "bottom right"];

    private TicTacToe _board = new();

    public TicTacToeViewModel(IGameHost host) : base(host, GameKind.TicTacToe)
    {
        Squares = [.. Enumerable.Range(0, TicTacToe.Size).Select(i => new TicTacToeSquare(this, i))];
        // An empty square shows a faint ✕ under the pointer only while the user can play it.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsYourTurn))
            {
                foreach (var square in Squares)
                {
                    square.OnTurnChanged();
                }
            }
        };
        NewRound();
    }

    public override string Title => "Tic-tac-toe";

    public override IReadOnlyList<GameLevelChoice> Levels { get; } =
        [new(GameLevel.Easy, "Easy"), new(GameLevel.Normal, "Normal"), new(GameLevel.Hard, "Unbeatable")];

    /// <summary>The nine squares, row by row from the top left.</summary>
    public IReadOnlyList<TicTacToeSquare> Squares { get; }

    /// <summary>The first square of the three in a row that won, which the strike starts at; -1 till someone has.</summary>
    [ObservableProperty]
    public partial int StrikeFrom { get; private set; } = -1;

    /// <summary>The last of them, which the strike ends at.</summary>
    [ObservableProperty]
    public partial int StrikeTo { get; private set; } = -1;


    /// <summary>"the top left", for her moves and the squares' names.</summary>
    public static string SquareName(int square) => SquareNames[square];

    protected override void StartRound(bool youStart)
    {
        _board = new TicTacToe();
        Refresh();
        if (youStart)
        {
            Status = "Your move. You're ✕.";
        }
        else
        {
            HerTurn(HerMove);
        }
    }

    /// <summary>The user takes <paramref name="square"/>, unless it's taken, it's her turn or the round is over.</summary>
    public void Play(int square)
    {
        if (IsHerTurn || !_board.CanPlay(square))
        {
            return;
        }
        _board.Play(square, Player.You);
        Host.Moved();
        Refresh();
        if (!Ended())
        {
            HerTurn(HerMove);
        }
    }

    public override bool Press(char key)
    {
        if (key is < '1' or > '9')
        {
            return false;
        }
        Play(key - '1');
        return true;
    }

    private void HerMove()
    {
        var threat = _board.WinningMove(Player.You);
        var square = TicTacToeOpponent.Choose(_board, CurrentLevel, Host.Random);
        _board.Play(square, Player.Joulene);
        Refresh();
        Host.Announce($"Joulene took the {SquareName(square)}.");
        if (Ended())
        {
            return;
        }
        if (threat == square)
        {
            Host.CloseCall();
        }
        Status = "Your move.";
    }

    /// <summary>Ends the round if someone won or the board is full.</summary>
    private bool Ended()
    {
        switch (_board.Outcome)
        {
            case GameOutcome.Won:
                Finish(GameOutcome.Won, "You win!");
                return true;
            case GameOutcome.Lost:
                Finish(GameOutcome.Lost, "Joulene wins!");
                return true;
            case GameOutcome.Draw:
                Finish(GameOutcome.Draw, "A draw.");
                return true;
            default:
                return false;
        }
    }

    private void Refresh()
    {
        var line = _board.WinningLine ?? [];
        foreach (var square in Squares)
        {
            square.Owner = _board[square.Index];
            square.IsWinning = line.Contains(square.Index);
        }
        (StrikeFrom, StrikeTo) = line.Length > 0 ? (line[0], line[^1]) : (-1, -1);
    }
}

/// <summary>A square of the tic-tac-toe board.</summary>
public sealed partial class TicTacToeSquare(TicTacToeViewModel game, int index) : ObservableObject
{
    public int Index => index;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsYours), nameof(IsHers), nameof(Name), nameof(CanPlay))]
    public partial Player Owner { get; internal set; }

    /// <summary>It's empty and the user can play: under the pointer it shows a faint ✕.</summary>
    public bool CanPlay => Owner == Player.None && game.IsYourTurn;

    internal void OnTurnChanged() => OnPropertyChanged(nameof(CanPlay));

    /// <summary>One of the three in a row that won.</summary>
    [ObservableProperty]
    public partial bool IsWinning { get; internal set; }

    public bool IsYours => Owner == Player.You;

    public bool IsHers => Owner == Player.Joulene;

    /// <summary>"Top left, empty", for screen readers.</summary>
    public string Name
    {
        get
        {
            var where = TicTacToeViewModel.SquareName(index);
            var whose = Owner switch
            {
                Player.You => "yours",
                Player.Joulene => "Joulene's",
                _ => "empty",
            };
            return $"{char.ToUpperInvariant(where[0])}{where[1..]}, {whose}";
        }
    }

    [RelayCommand]
    private void Play() => game.Play(index);
}
