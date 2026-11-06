namespace Deventry.App.Games;

/// <summary>The games against Joulene (DESIGN.md §18, "Games with Joulene").</summary>
public enum GameKind
{
    TicTacToe,
    ConnectFour,
    Hangman,
    Minesweeper,
    Pipeline,
}

/// <summary>
/// How well Joulene plays, or in Minesweeper how many mines she hides, and in Pipeline how far and how fast her flow
/// goes. Tic-tac-toe calls the top level Unbeatable, the others Hard.
/// </summary>
public enum GameLevel
{
    Easy,
    Normal,
    Hard,
}

/// <summary>Whose a square or a piece is.</summary>
public enum Player
{
    None,
    You,
    Joulene,
}

/// <summary>How a round stands, from the user's side.</summary>
public enum GameOutcome
{
    Playing,
    Won,
    Lost,
    Draw,
}

public static class Players
{
    /// <summary>The one who moves after <paramref name="player"/>.</summary>
    public static Player Other(this Player player) => player == Player.You ? Player.Joulene : Player.You;
}
