using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Deventry.App.Games;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.App.ViewModels.ToolWindows;
using Deventry.App.Views;

namespace Deventry.App.UiTests;

/// <summary>
/// Games with Joulene rendered (DESIGN.md §18): the Games tool window with each board and the game's menu in its
/// header, the keyboard while the page has focus, and the ways in: her menu and the usage limit bar.
/// </summary>
public class GamesUiTests
{
    [AvaloniaFact]
    public async Task The_Games_page_is_made_only_while_it_shows_and_plays_tic_tac_toe()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        h.Services.Random = new Random(4);
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        Assert.Empty(window.GetVisualDescendants().OfType<GamesView>());
        // Its icon is on the strip, game or no game (issue #96).
        Assert.Contains("Games", PageTabs(window));

        tab.PlayGame(GameKind.TicTacToe);
        UiText.Settle(window);

        Assert.Contains("Games", PageTabs(window));
        var page = window.GetVisualDescendants().OfType<GamesView>().Single();
        var squares = Squares(page);
        Assert.Equal(9, squares.Count);
        Assert.Equal("Top left, empty", AutomationProperties.GetName(squares[0]));

        Click(window, squares[4]);
        h.Time.Advance(GameViewModel.LongestThought);
        UiText.Settle(window);

        Assert.Equal("Middle, yours", AutomationProperties.GetName(Squares(page)[4]));
        Assert.Single(Squares(page), s => AutomationProperties.GetName(s)!.EndsWith(", Joulene's", StringComparison.Ordinal));
        var shown = UiText.Describe(page);

        // Another window of its group, and the game's controls go; it's still open, and shows again in its window.
        h.ShowToolWindow(ToolWindowKinds.ScratchPad);
        UiText.Settle(window);
        Assert.Empty(window.GetVisualDescendants().OfType<GamesView>());
        h.ShowToolWindow(ToolWindowKinds.Games);
        UiText.Settle(window);
        page = window.GetVisualDescendants().OfType<GamesView>().Single();
        Assert.Single(Squares(page), s => AutomationProperties.GetName(s)!.EndsWith(", yours", StringComparison.Ordinal));

        // Done playing: the page and its tab go.
        // Done playing: the game ends, and the window lists the games, its icon still there.
        Click(window, page.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Done playing"));
        UiText.Settle(window);
        Assert.False(h.Services.Games.IsOpen);
        Assert.Contains("Games", PageTabs(window));
        Assert.True(window.GetVisualDescendants().OfType<GamesView>().Single().GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "GameChoices").IsEffectivelyVisible);
        window.Close();

        await Verify(shown);
    }

    [AvaloniaFact]
    public async Task Hangman_takes_letters_typed_on_the_page_and_none_reach_the_composer()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        h.Services.Random = new Random(9);
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        tab.PlayGame(GameKind.Hangman);
        UiText.Settle(window);
        h.Time.Advance(GameViewModel.LongestThought);
        UiText.Settle(window);
        var page = window.GetVisualDescendants().OfType<GamesView>().Single();
        var game = (HangmanViewModel)h.Services.Games.Game!;
        // Started just now, the page took the keyboard.
        await UiText.SettleUntilAsync(window, () => page.IsKeyboardFocusWithin, "the page to take the keyboard");

        var hit = game.Word![0];
        var miss = Enumerable.Range('A', 26).Select(c => (char)c).First(c => !game.Word.Contains(c));
        window.KeyPressQwerty(Physical(hit), RawInputModifiers.None);
        window.KeyPressQwerty(Physical(miss), RawInputModifiers.None);
        UiText.Settle(window);

        Assert.Equal(hit, game.Pattern[0]);
        Assert.Equal(1, game.Misses);
        Assert.Equal("", tab.ComposerText ?? "");
        Assert.Equal($"{miss}, not in the word", AutomationProperties.GetName(Letter(page, miss)));
        Assert.Equal("The gallows, 1 of 6 misses drawn", AutomationProperties.GetName(page.GetVisualDescendants().OfType<Canvas>().Single()));
        // The word on its lines, a letter each, the one guessed on its line; the keys guessed sink.
        var lines = page.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("wordslot")).ToList();
        Assert.Equal(game.Word.Length, lines.Count);
        Assert.Equal(hit.ToString(), lines[0].GetVisualDescendants().OfType<TextBlock>().Single().Text);
        Assert.Contains("guessed", Letter(page, miss).Classes);
        Assert.DoesNotContain("guessed", Letter(page, Enumerable.Range('A', 26).Select(c => (char)c).First(c => c != hit && c != miss)).Classes);
        var shown = UiText.Describe(page, (game.Word, "WORD"));
        window.Close();

        await Verify(shown);

        static PhysicalKey Physical(char letter) => PhysicalKey.A + (letter - 'A');

        static Button Letter(GamesView page, char letter) =>
            page.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("gameletter") && (b.DataContext as HangmanLetter)?.Letter == letter);
    }

    [AvaloniaFact]
    public async Task Four_in_a_row_drops_a_piece_in_the_column_clicked()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        h.Services.Random = new Random(6);
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        tab.PlayGame(GameKind.FourInARow);
        UiText.Settle(window);
        var page = window.GetVisualDescendants().OfType<GamesView>().Single();
        var columns = page.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("gamecolumn")).ToList();
        Assert.Equal(7, columns.Count);
        Assert.Equal("Column 3, 0 of 6 filled", AutomationProperties.GetName(columns[2]));

        Click(window, columns[2]);
        h.Time.Advance(GameViewModel.LongestThought);
        UiText.Settle(window);

        Assert.Equal("Column 3, 1 of 6 filled", AutomationProperties.GetName(columns[2]));
        var pieces = Pieces(columns[2]);
        Assert.Contains("yours", pieces[^1].Classes);
        Assert.Single(Pieces(page), e => e.Classes.Contains("hers"));
        // Her piece, dropped last, has its dot; the next place up in the column the user's would land in.
        Assert.Single(page.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>(), e => e.Classes.Contains("lastdot") && e.IsVisible);
        Assert.Contains("drop", pieces[^2].Classes);
        Assert.Equal(7, Pieces(page).Count(e => e.Classes.Contains("drop")));
        var shown = UiText.Describe(page);
        window.Close();

        await Verify(shown);
    }

    [AvaloniaFact]
    public async Task The_windows_header_names_the_game_and_its_menu_switches_to_another()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        tab.PlayGame(GameKind.TicTacToe);
        UiText.Settle(window);

        var choice = GameChoice(window);
        Assert.Equal("Game: Tic-tac-toe", AutomationProperties.GetName(choice));
        Assert.Contains("Tic-tac-toe", choice.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        // It's in the header, beside the window's title, and the page under it has no row of games.
        var header = choice.GetVisualAncestors().OfType<Grid>().First(g => g.Classes.Contains("toolheader"));
        Assert.Contains(header.GetVisualDescendants().OfType<TextBlock>(), t => t.Classes.Contains("tooltitle") && t.Text == "Games");
        var page = window.GetVisualDescendants().OfType<GamesView>().Single();
        Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(), b => b.Content is "Four in a Row" or TextBlock { Text: "Four in a Row" });

        var menu = Assert.IsType<MenuFlyout>(choice.Flyout);
        var items = menu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(["Tic-tac-toe", "Four in a Row", "Hangman", "Minesweeper", "Pipeline"], items.Select(i => i.Header as string));
        menu.ShowAt(choice);
        await UiText.SettleUntilAsync(window, () => items[0].IsEffectivelyVisible, "the open menu");
        Assert.Equal([true, false, false, false, false], items.Select(i => i.IsChecked));
        // Each draws its game's name: the header's templates for what its windows show don't reach the menu's entries.
        Assert.All(items, i => Assert.Contains(i.Header as string, i.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)));

        items[3].Command!.Execute(items[3].CommandParameter);
        menu.Hide();
        UiText.Settle(window);

        Assert.IsType<MinesweeperViewModel>(h.Services.Games.Game);
        Assert.Equal("Game: Minesweeper", AutomationProperties.GetName(GameChoice(window)));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Minesweeper_opens_with_a_click_flags_with_a_right_click_or_F_and_the_arrows_move_between_squares()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        h.Services.Random = new Random(5);
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        tab.PlayGame(GameKind.Minesweeper);
        UiText.Settle(window);
        var page = window.GetVisualDescendants().OfType<GamesView>().Single();
        var game = (MinesweeperViewModel)h.Services.Games.Game!;
        var squares = MineSquares(page);
        Assert.Equal(81, squares.Count);
        // Nine to a row, in the right side at its narrowest.
        Point At(int square) => squares[square].TranslatePoint(default, page)!.Value;
        Assert.Equal(At(0).Y, At(8).Y);
        Assert.True(At(9).Y > At(0).Y);
        Assert.True(At(8).X + squares[8].Bounds.Width <= ToolDockViewModel.MinRightWidth - 16);

        Click(window, squares[40]);

        Assert.True(game.Squares[40].IsOpen);
        Assert.Contains("open", squares[40].Classes);
        var shut = game.Squares.Where(s => s.State == MinesweeperSquareState.Shut).Select(s => s.Index).ToList();

        // A right-click flags a shut square.
        var center = squares[shut[0]].TranslatePoint(new Point(squares[shut[0]].Bounds.Width / 2, squares[shut[0]].Bounds.Height / 2), window)!.Value;
        window.MouseMove(center);
        window.MouseDown(center, MouseButton.Right);
        window.MouseUp(center, MouseButton.Right);
        UiText.Settle(window);
        Assert.EndsWith(", flagged", AutomationProperties.GetName(MineSquares(page)[shut[0]]));

        // F flags the one with the focus, and again takes the flag away.
        var square = MineSquares(page)[shut[1]];
        square.Focus();
        UiText.Settle(window);
        window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.None);
        UiText.Settle(window);
        Assert.True(game.Squares[shut[1]].ShowsFlag);
        window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.None);
        UiText.Settle(window);
        Assert.False(game.Squares[shut[1]].ShowsFlag);
        Assert.Equal("", tab.ComposerText ?? "");

        // The arrow keys move the focus from square to square.
        MineSquares(page)[0].Focus();
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        UiText.Settle(window);
        Assert.Same(MineSquares(page)[10], window.FocusManager!.GetFocusedElement());
        // Tab leaves the field, rather than going through its 81 squares.
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        UiText.Settle(window);
        var next = Assert.IsAssignableFrom<Control>(window.FocusManager.GetFocusedElement());
        Assert.DoesNotContain("minesquare", next.Classes);
        var shown = UiText.Describe(page);
        window.Close();

        await Verify(shown);
    }

    [AvaloniaFact]
    public async Task Pipeline_lays_the_next_piece_where_clicked_or_on_Enter_and_the_arrows_move_between_squares()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        h.Services.Random = new Random(5);
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        tab.PlayGame(GameKind.Pipeline);
        UiText.Settle(window);
        var page = window.GetVisualDescendants().OfType<GamesView>().Single();
        var game = (PipelineViewModel)h.Services.Games.Game!;
        var squares = PipeSquares(page);
        Assert.Equal(64, squares.Count);
        // Eight to a row, touching, in the right side at its narrowest.
        Point At(int square) => squares[square].TranslatePoint(default, page)!.Value;
        Assert.Equal(At(0).Y, At(7).Y);
        Assert.Equal(At(0).X + squares[0].Bounds.Width, At(1).X);
        Assert.True(At(8).Y > At(0).Y);
        Assert.True(At(7).X + squares[7].Bounds.Width <= ToolDockViewModel.MinRightWidth - 16);
        var queue = page.GetVisualDescendants().OfType<ItemsControl>().Single(i => AutomationProperties.GetName(i)?.StartsWith("Next: ", StringComparison.Ordinal) == true);
        Assert.Equal(5, queue.GetVisualDescendants().OfType<PipeTile>().Count());
        Assert.Contains("next", queue.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("pipepiece")).Classes);
        var tap = Tile(squares[game.Board.Tap]);
        Assert.Equal(PipeKind.Tap, tap.Piece);
        Assert.Equal(1, tap.Countdown);

        // A click lays the next piece.
        var empty = game.Squares.Where(s => s.Piece == PipeKind.Empty).Select(s => s.Index).ToList();
        var next = game.Next[0].Piece;
        Click(window, squares[empty[0]]);
        Assert.Equal(next, Tile(squares[empty[0]]).Piece);
        Assert.Equal(next, game.Squares[empty[0]].Piece);

        // The arrow keys move the focus from square to square; the one they reach shows the piece Enter lays there.
        var target = empty.First(s => s != empty[0] && s / Pipeline.Columns > 0 && s % Pipeline.Columns > 0);
        next = game.Next[0].Piece;
        PipeSquares(page)[target - Pipeline.Columns - 1].Focus();
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        UiText.Settle(window);
        Assert.Same(PipeSquares(page)[target], window.FocusManager!.GetFocusedElement());
        Assert.True(Tile(squares[target]).IsHot);
        Assert.Equal(next, Tile(squares[target]).Ghost);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        UiText.Settle(window);
        Assert.Equal(next, game.Squares[target].Piece);
        Assert.Equal("", tab.ComposerText ?? "");

        // Tab leaves the board, rather than going through its 64 squares.
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        UiText.Settle(window);
        var focused = Assert.IsAssignableFrom<Control>(window.FocusManager.GetFocusedElement());
        Assert.DoesNotContain("pipesquare", focused.Classes);

        // Her tap opens on the clock, and the flow shows in it.
        h.Time.Advance(TimeSpan.FromSeconds(21));
        UiText.Settle(window);
        Assert.True(game.Board.IsFlowing);
        Assert.NotEmpty(tap.Flows!);
        var shown = UiText.Describe(page);
        window.Close();

        await Verify(shown);

        static PipeTile Tile(Button square) => square.GetVisualDescendants().OfType<PipeTile>().Single();
    }

    [AvaloniaFact]
    public async Task Her_menu_offers_the_games_while_theyre_on()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        var layer = window.GetVisualDescendants().OfType<MascotLayer>().Single(l => !l.IsFront);
        var menu = layer.Figure.ContextMenu!;
        Assert.Equal(["Play tic-tac-toe", "Play Four in a Row", "Play hangman", "Play Minesweeper", "Play Pipeline", "Hide Joulene", "Joulene settings…"],
            menu.Items.OfType<MenuItem>().Where(i => i.IsVisible).Select(i => i.Header as string));

        menu.Items.OfType<MenuItem>().Single(i => i.Header as string == "Play Four in a Row").Command!.Execute(GameKind.FourInARow);
        UiText.Settle(window);
        Assert.IsType<FourInARowViewModel>(h.Services.Games.Game);
        Assert.True(h.IsShowing(ToolWindowKinds.Games));

        h.Services.Settings.Appearance.GamesWithJoulene = false;
        h.Services.SaveSettings();
        UiText.Settle(window);
        Assert.Equal(["Hide Joulene", "Joulene settings…"], menu.Items.OfType<MenuItem>().Where(i => i.IsVisible).Select(i => i.Header as string));
        Assert.DoesNotContain(menu.Items.OfType<Separator>(), s => s.IsVisible);
        Assert.Empty(window.GetVisualDescendants().OfType<GamesView>());
        window.Close();
    }

    /// <summary>The tool windows' icons on the strip, by name.</summary>
    /// <summary>
    /// With no game open, the Games window lists the four, each with its score so far, and a click on one starts it there
    /// (issue #96); its header has its title alone.
    /// </summary>
    [AvaloniaFact]
    public async Task With_no_game_open_the_Games_window_lists_the_games_to_start_one()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        await h.OpenTabAsync();
        h.Services.State.Games.Scores[nameof(GameKind.FourInARow)] = new Core.Settings.GameScore { Wins = 2, Losses = 1 };
        var window = UiText.Show(new ShellView { DataContext = h.Shell });

        h.ShowToolWindow(ToolWindowKinds.Games);
        UiText.Settle(window);

        var page = window.GetVisualDescendants().OfType<GamesView>().Single();
        var choices = page.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("gamechoice")).ToList();
        Assert.Equal(["Play Tic-tac-toe", "Play Four in a Row: You 2 · Joulene 1 · Draws 0", "Play Hangman", "Play Minesweeper", "Play Pipeline"],
            choices.Select(b => AutomationProperties.GetName(b)));
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("toolchoice") && b.IsEffectivelyVisible);
        var shown = UiText.Describe(page);

        Click(window, choices[3]);
        UiText.Settle(window);

        Assert.IsType<MinesweeperViewModel>(h.Services.Games.Game);
        Assert.False(page.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "GameChoices").IsEffectivelyVisible);
        Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("toolchoice") && b.IsEffectivelyVisible);
        window.Close();
        await Verify(shown);
    }

    private static List<string?> PageTabs(Window window) =>
        [.. window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("stripicon") && b.IsEffectivelyVisible).Select(AutomationProperties.GetName)];

    private static List<Button> Squares(Control page) => [.. page.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("gamesquare"))];

    private static List<Button> MineSquares(Control page) => [.. page.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("minesquare"))];

    private static List<Button> PipeSquares(Control page) => [.. page.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("pipesquare"))];

    /// <summary>Four in a Row's holes, each with its piece or none, from the top of each column.</summary>
    private static List<Avalonia.Controls.Shapes.Ellipse> Pieces(Control under) =>
        [.. under.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Where(e => e.Classes.Contains("gamepiece"))];

    /// <summary>The game's menu in the Games window's header.</summary>
    private static Button GameChoice(Window window) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("toolchoice") && b.IsEffectivelyVisible);

    private static void Click(Window window, Control target)
    {
        var center = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseMove(center);
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        UiText.Settle(window);
    }
}
