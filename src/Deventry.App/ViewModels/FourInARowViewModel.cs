using Deventry.App.Games;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>Four in a Row against Joulene (DESIGN.md §18, "Games with Joulene"): the user's pieces are blue, hers rose.</summary>
public sealed class FourInARowViewModel : GameViewModel
{
    private FourInARow _board = new();

    public FourInARowViewModel(IGameHost host) : base(host, GameKind.FourInARow)
    {
        Columns = [.. Enumerable.Range(0, FourInARow.Columns).Select(c => new FourInARowColumn(this, c))];
        // A column under the pointer shows where the user's piece would land only while they can drop one.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsYourTurn))
            {
                Refresh();
            }
        };
        NewRound();
    }

    public override string Title => "Four in a Row";

    public override IReadOnlyList<GameLevelChoice> Levels { get; } =
        [new(GameLevel.Easy, "Easy"), new(GameLevel.Normal, "Normal"), new(GameLevel.Hard, "Hard")];

    /// <summary>The seven columns, left to right.</summary>
    public IReadOnlyList<FourInARowColumn> Columns { get; }

    protected override void StartRound(bool youStart)
    {
        _board = new FourInARow();
        Refresh();
        if (youStart)
        {
            Status = "Your move: pick a column.";
        }
        else
        {
            HerTurn(HerMove);
        }
    }

    /// <summary>The user drops a piece in <paramref name="column"/>, unless it's full, it's her turn or the round is over.</summary>
    public void Drop(int column)
    {
        if (IsHerTurn || !_board.CanDrop(column))
        {
            return;
        }
        _board.Drop(column, Player.You);
        Host.Moved();
        Refresh();
        if (!Ended())
        {
            HerTurn(HerMove);
        }
    }

    public override bool Press(char key)
    {
        if (key is < '1' or > '7')
        {
            return false;
        }
        Drop(key - '1');
        return true;
    }

    private void HerMove()
    {
        var threat = _board.WinningMove(Player.You);
        var column = FourInARowOpponent.Choose(_board, CurrentLevel, Host.Random);
        _board.Drop(column, Player.Joulene);
        Refresh();
        Host.Announce($"Joulene dropped a piece in column {column + 1}.");
        if (Ended())
        {
            return;
        }
        if (threat == column)
        {
            Host.CloseCall();
        }
        Status = "Your move.";
    }

    private bool Ended()
    {
        switch (_board.Outcome)
        {
            case GameOutcome.Won:
                Finish(GameOutcome.Won, "Four in a row: you win!");
                return true;
            case GameOutcome.Lost:
                Finish(GameOutcome.Lost, "Four in a row: Joulene wins!");
                return true;
            case GameOutcome.Draw:
                Finish(GameOutcome.Draw, "The board's full: a draw.");
                return true;
            default:
                return false;
        }
    }

    private void Refresh()
    {
        var winning = _board.WinningCells;
        foreach (var column in Columns)
        {
            column.Refresh(_board, winning, IsYourTurn);
        }
    }
}

/// <summary>A column of the Four in a Row board: a click drops a piece in it.</summary>
public sealed partial class FourInARowColumn : ObservableObject
{
    private readonly FourInARowViewModel _game;

    internal FourInARowColumn(FourInARowViewModel game, int index)
    {
        _game = game;
        Index = index;
        Cells = [.. Enumerable.Range(0, FourInARow.Rows).Select(_ => new FourInARowCell())];
    }

    public int Index { get; }

    /// <summary>Its cells from the top down, as they show.</summary>
    public IReadOnlyList<FourInARowCell> Cells { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name))]
    public partial int Filled { get; private set; }

    /// <summary>"Column 3, 2 of 6 filled", for screen readers.</summary>
    public string Name => Filled == FourInARow.Rows ? $"Column {Index + 1}, full" : $"Column {Index + 1}, {Filled} of {FourInARow.Rows} filled";

    [RelayCommand]
    private void Drop() => _game.Drop(Index);

    internal void Refresh(FourInARow board, IReadOnlyList<(int Column, int Row)> winning, bool yourTurn)
    {
        Filled = board.Height(Index);
        for (var row = 0; row < FourInARow.Rows; row++)
        {
            var cell = Cells[FourInARow.Rows - 1 - row];
            cell.Owner = board.At(Index, row);
            cell.IsWinning = winning.Contains((Index, row));
            cell.IsLast = board.LastMove == (Index, row);
            cell.IsDropSpot = yourTurn && row == Filled;
        }
    }
}

/// <summary>A place for a piece on the Four in a Row board.</summary>
public sealed partial class FourInARowCell : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsYours), nameof(IsHers))]
    public partial Player Owner { get; internal set; }

    /// <summary>One of the pieces in a row that won: it's ringed.</summary>
    [ObservableProperty]
    public partial bool IsWinning { get; internal set; }

    /// <summary>The piece dropped last: it has a dot.</summary>
    [ObservableProperty]
    public partial bool IsLast { get; internal set; }

    /// <summary>Where the user's piece would land in its column, which shows faintly while the column is under the pointer.</summary>
    [ObservableProperty]
    public partial bool IsDropSpot { get; internal set; }

    public bool IsYours => Owner == Player.You;

    public bool IsHers => Owner == Player.Joulene;
}
