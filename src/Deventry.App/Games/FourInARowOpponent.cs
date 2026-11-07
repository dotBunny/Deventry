namespace Deventry.App.Games;

/// <summary>
/// Joulene's Four in a Row (DESIGN.md §18, "Games with Joulene"): an alpha-beta search one, four or six moves deep
/// (Easy, Normal, Hard), scoring the board by the lines of four each side could still make. On every level she takes a
/// win in front of her; on Easy she also plays anywhere a third of the time.
/// </summary>
public static class FourInARowOpponent
{
    private const int Win = 1_000_000;

    /// <summary>The middle columns first, which makes the search's cut-offs come sooner.</summary>
    private static readonly int[] Order = [3, 2, 4, 1, 5, 0, 6];

    public static int Depth(GameLevel level) => level switch
    {
        GameLevel.Easy => 1,
        GameLevel.Normal => 4,
        _ => 6,
    };

    public static int Choose(FourInARow board, GameLevel level, Random random)
    {
        var free = Enumerable.Range(0, FourInARow.Columns).Where(board.CanDrop).ToArray();
        if (free.Length == 0)
        {
            throw new InvalidOperationException("There's no move to make.");
        }
        if (board.WinningMove(Player.Joulene) is { } win)
        {
            return win;
        }
        if (level == GameLevel.Easy && random.Next(3) == 0)
        {
            return free[random.Next(free.Length)];
        }
        return Best(board, Depth(level), random);
    }

    /// <summary>Her best column looking <paramref name="depth"/> moves ahead, one of the equally good ones at random.</summary>
    public static int Best(FourInARow board, int depth, Random random)
    {
        var best = int.MinValue;
        var choices = new List<int>();
        foreach (var column in Order)
        {
            if (board.Height(column) == FourInARow.Rows)
            {
                continue;
            }
            var row = board.Place(column, Player.Joulene);
            // Each column is searched in full, so equal scores really are equal.
            var score = board.WinsAt(column, row) ? Win : -Search(board, Player.You, depth - 1, -int.MaxValue, int.MaxValue, 1);
            board.Lift(column);
            if (score > best)
            {
                best = score;
                choices.Clear();
            }
            if (score == best)
            {
                choices.Add(column);
            }
        }
        return choices[random.Next(choices.Count)];
    }

    /// <summary>How the board stands for <paramref name="toMove"/>, looking <paramref name="depth"/> moves ahead.</summary>
    private static int Search(FourInARow board, Player toMove, int depth, int alpha, int beta, int ply)
    {
        if (board.IsFull)
        {
            return 0;
        }
        if (depth == 0)
        {
            return Evaluate(board, toMove);
        }
        var best = -int.MaxValue;
        foreach (var column in Order)
        {
            if (board.Height(column) == FourInARow.Rows)
            {
                continue;
            }
            var row = board.Place(column, toMove);
            // A win sooner counts for more.
            var score = board.WinsAt(column, row) ? Win - ply : -Search(board, toMove.Other(), depth - 1, -beta, -alpha, ply + 1);
            board.Lift(column);
            best = Math.Max(best, score);
            alpha = Math.Max(alpha, score);
            if (alpha >= beta)
            {
                break;
            }
        }
        return best;
    }

    /// <summary>
    /// The lines of four <paramref name="player"/> could still make, less the other side's: three with the fourth free
    /// count most, then two with two free; and pieces in the middle column, which most lines pass through.
    /// </summary>
    internal static int Evaluate(FourInARow board, Player player)
    {
        var score = 0;
        for (var row = 0; row < FourInARow.Rows; row++)
        {
            score += board.At(3, row) == player ? 3 : board.At(3, row) == player.Other() ? -3 : 0;
        }
        foreach (var (dc, dr) in new[] { (1, 0), (0, 1), (1, 1), (1, -1) })
        {
            for (var column = 0; column < FourInARow.Columns; column++)
            {
                for (var row = 0; row < FourInARow.Rows; row++)
                {
                    int endColumn = column + 3 * dc, endRow = row + 3 * dr;
                    if (endColumn is < 0 or >= FourInARow.Columns || endRow is < 0 or >= FourInARow.Rows)
                    {
                        continue;
                    }
                    int mine = 0, theirs = 0;
                    for (var step = 0; step < 4; step++)
                    {
                        var at = board.At(column + step * dc, row + step * dr);
                        mine += at == player ? 1 : 0;
                        theirs += at == player.Other() ? 1 : 0;
                    }
                    score += Window(mine, theirs) - Window(theirs, mine);
                }
            }
        }
        return score;
    }

    private static int Window(int mine, int theirs) => theirs > 0 ? 0 : mine switch
    {
        3 => 5,
        2 => 2,
        _ => 0,
    };
}
