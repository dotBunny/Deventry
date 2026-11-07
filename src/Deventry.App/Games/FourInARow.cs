namespace Deventry.App.Games;

/// <summary>
/// Four in a Row's board (DESIGN.md §18, "Games with Joulene"): seven columns of six, filled from the bottom (row 0).
/// Four in a row across, up or on a diagonal wins.
/// </summary>
public sealed class FourInARow
{
    public const int Columns = 7;
    public const int Rows = 6;

    /// <summary>The four ways a line can run: across, up, and the two diagonals.</summary>
    private static readonly (int Column, int Row)[] Directions = [(1, 0), (0, 1), (1, 1), (1, -1)];

    private readonly Player[] _cells = new Player[Columns * Rows];
    private readonly int[] _heights = new int[Columns];
    private List<(int Column, int Row)> _winning = [];

    public Player At(int column, int row) => _cells[column * Rows + row];

    /// <summary>How many pieces the column holds.</summary>
    public int Height(int column) => _heights[column];

    public int Moves { get; private set; }

    public Player Winner { get; private set; }

    /// <summary>The pieces in a row that won: four or more.</summary>
    public IReadOnlyList<(int Column, int Row)> WinningCells => _winning;

    /// <summary>Where the last piece landed.</summary>
    public (int Column, int Row)? LastMove { get; private set; }

    public bool IsFull => Moves == Columns * Rows;

    public bool IsOver => Winner != Player.None || IsFull;

    public GameOutcome Outcome => Winner switch
    {
        Player.You => GameOutcome.Won,
        Player.Joulene => GameOutcome.Lost,
        _ => IsFull ? GameOutcome.Draw : GameOutcome.Playing,
    };

    public bool CanDrop(int column) => column is >= 0 and < Columns && _heights[column] < Rows && !IsOver;

    /// <summary>Drops <paramref name="player"/>'s piece in <paramref name="column"/>, and returns the row it lands in.</summary>
    public int Drop(int column, Player player)
    {
        if (!CanDrop(column) || player == Player.None)
        {
            throw new InvalidOperationException($"Column {column} can't be played.");
        }
        var row = Place(column, player);
        LastMove = (column, row);
        if (Line(column, row) is { } line)
        {
            Winner = player;
            _winning = line;
        }
        return row;
    }

    /// <summary>A column that wins at once for <paramref name="player"/>, or null.</summary>
    public int? WinningMove(Player player)
    {
        if (IsOver)
        {
            return null;
        }
        for (var column = 0; column < Columns; column++)
        {
            if (_heights[column] == Rows)
            {
                continue;
            }
            var row = Place(column, player);
            var wins = WinsAt(column, row);
            Lift(column);
            if (wins)
            {
                return column;
            }
        }
        return null;
    }

    /// <summary>Puts a piece on top of the column without the rules' checks, for looking ahead; returns its row.</summary>
    internal int Place(int column, Player player)
    {
        var row = _heights[column]++;
        _cells[column * Rows + row] = player;
        Moves++;
        return row;
    }

    /// <summary>Takes the top piece off the column again.</summary>
    internal void Lift(int column)
    {
        var row = --_heights[column];
        _cells[column * Rows + row] = Player.None;
        Moves--;
    }

    /// <summary>Whether the piece at (<paramref name="column"/>, <paramref name="row"/>) makes four in a row.</summary>
    internal bool WinsAt(int column, int row)
    {
        var player = At(column, row);
        foreach (var (dc, dr) in Directions)
        {
            if (1 + Run(column, row, dc, dr, player) + Run(column, row, -dc, -dr, player) >= 4)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The longest line of four or more through the piece, or null.</summary>
    private List<(int Column, int Row)>? Line(int column, int row)
    {
        var player = At(column, row);
        foreach (var (dc, dr) in Directions)
        {
            var back = Run(column, row, -dc, -dr, player);
            var ahead = Run(column, row, dc, dr, player);
            if (1 + back + ahead >= 4)
            {
                return [.. Enumerable.Range(-back, back + ahead + 1).Select(step => (column + step * dc, row + step * dr))];
            }
        }
        return null;
    }

    /// <summary>How many of <paramref name="player"/>'s pieces follow on from the cell, one way.</summary>
    private int Run(int column, int row, int dc, int dr, Player player)
    {
        var count = 0;
        for (int c = column + dc, r = row + dr; c is >= 0 and < Columns && r is >= 0 and < Rows && At(c, r) == player; c += dc, r += dr)
        {
            count++;
        }
        return count;
    }
}
