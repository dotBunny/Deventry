using Deventry.App.Games;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>
/// Pipeline against Joulene (DESIGN.md §18, "Games with Joulene"): she hands out pieces of pipe and the user lays them
/// from her tap; once it opens, her flow follows them until it spills, and it has to have gone through as many pipes as
/// the level asks by then. A click lays the next piece; <b>Open now</b> opens her tap early, and <b>Faster</b> hurries
/// the flow. The flow runs by the injected clock, and only while the user can see the board.
/// </summary>
public sealed partial class PipelineViewModel : GameViewModel
{
    private readonly UiTicker _flow;

    // StartRound lays it out, from the constructor on.
    private Pipeline _board = null!;

    /// <summary>She's wiped her brow this round: the flow reached a pipe laid just in time.</summary>
    private bool _closeCall;

    public PipelineViewModel(IGameHost host) : base(host, GameKind.Pipeline)
    {
        _flow = new UiTicker(host.Time, host.Dispatcher, Pipeline.Step, Tick);
        Squares = [.. Enumerable.Range(0, Pipeline.Size).Select(i => new PipeSquare(this, i))];
        Next = [.. Enumerable.Range(0, Pipeline.Shown).Select(i => new PipePiece(i == 0))];
        NewRound();
    }

    public override string Title => "Pipeline";

    public override IReadOnlyList<GameLevelChoice> Levels { get; } =
        [new(GameLevel.Easy, "Easy"), new(GameLevel.Normal, "Normal"), new(GameLevel.Hard, "Hard")];

    protected override bool HasDraws => false;

    /// <summary>The board's squares, row by row from the top left.</summary>
    public IReadOnlyList<PipeSquare> Squares { get; }

    /// <summary>The pieces she'll hand out, the next first.</summary>
    public IReadOnlyList<PipePiece> Next { get; }

    /// <summary>"Next: corner, top to right, then …", for screen readers.</summary>
    [ObservableProperty]
    public partial string NextName { get; private set; } = "";

    /// <summary><b>Open now</b> before her tap opens, <b>Faster</b> after; null once there's nothing to hurry.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanHurry), nameof(HurryTip))]
    public partial string? HurryText { get; private set; }

    public bool CanHurry => HurryText is not null;

    public string HurryTip => _board.IsFlowing ? "Let the flow run fast till it spills" : "Joulene opens her tap now, without waiting";

    /// <summary>The board, for tests.</summary>
    internal Pipeline Board => _board;

    /// <summary>The flow is running: her tap's countdown or the flow itself, on the clock.</summary>
    internal bool IsRunning => _flow.IsRunning;

    protected override void StartRound(bool youStart)
    {
        // Her tap and rocks are where she puts them; who starts doesn't apply.
        _board = new Pipeline(Pipeline.RulesFor(CurrentLevel), Host.Random);
        _closeCall = false;
        Refresh();
        Status = Progress();
        Run();
    }

    /// <summary>Another level is another board: the round in progress dropped uncounted.</summary>
    protected override void OnLevelChanged() => NewRound();

    /// <summary>Out of sight, her countdown and the flow wait where they are; back, they go on.</summary>
    protected override void OnWatchedChanged()
    {
        Run();
        if (!_board.IsOver)
        {
            Status = IsWatched ? Progress() : "Paused till you come back.";
        }
    }

    /// <summary>The user lays the next piece on <paramref name="square"/>. Nothing happens where the flow has been, on her tap or rocks, or once the round is over.</summary>
    public void Lay(int square)
    {
        if (!_board.Lay(square, Host.Random))
        {
            return;
        }
        Host.Moved();
        Refresh();
        Host.Announce($"{Squares[square].Name}. Next, {Describe(_board.Next[0], PipeSide.Top)}.");
    }

    /// <summary><b>Open now</b>, or <b>Faster</b>.</summary>
    [RelayCommand]
    private void Hurry()
    {
        var opening = !_board.IsFlowing;
        if (!_board.Hurry())
        {
            return;
        }
        Host.Moved();
        if (opening)
        {
            Host.Announce("Joulene opened her tap.");
        }
        Refresh();
        Status = Progress();
    }

    /// <summary>The number keys and letters don't play Pipeline: its squares take the keyboard themselves.</summary>
    public override bool Press(char key) => false;

    private void Tick()
    {
        var happened = _board.Tick();
        Refresh();
        if (happened.HasFlag(PipelineEvents.TapOpened))
        {
            Host.Announce("Joulene opened her tap.");
        }
        if (happened.HasFlag(PipelineEvents.JustInTime) && !_closeCall)
        {
            _closeCall = true;
            Host.CloseCall();
        }
        if (happened.HasFlag(PipelineEvents.ReachedGoal) && !_board.IsOver)
        {
            Host.Announce($"That's {_board.Goal} pipes: you've beaten her. See how far it goes.");
        }
        switch (_board.Outcome)
        {
            case GameOutcome.Won:
                Run();
                Finish(GameOutcome.Won, $"It spilled after {_board.Filled} pipes: you win!");
                return;
            case GameOutcome.Lost:
                Run();
                Finish(GameOutcome.Lost, $"It spilled after {_board.Filled} of {_board.Goal} pipes: Joulene wins.");
                return;
        }
        Status = Progress();
    }

    /// <summary>Her countdown, or how far the flow has gone.</summary>
    private string Progress()
    {
        if (!_board.IsFlowing)
        {
            var seconds = _board.SecondsToTap;
            return $"Joulene's tap opens in {seconds} {(seconds == 1 ? "second" : "seconds")}. Lay {_board.Goal} pipes from it.";
        }
        return _board.Filled < _board.Goal
            ? $"The flow is through {_board.Filled} of {_board.Goal} pipes."
            : $"The flow is through {_board.Filled} pipes: that beats her {_board.Goal}.";
    }

    /// <summary>Plays <paramref name="board"/> from here, for tests: her tap and the pipes where a test wants them.</summary>
    internal void Use(Pipeline board)
    {
        _board = board;
        _closeCall = false;
        Refresh();
        Status = Progress();
        Run();
    }

    /// <summary>The clock runs while the user can see the board and the round goes on.</summary>
    private void Run() => _flow.Run(IsWatched && !_board.IsOver);

    /// <summary>The squares, the pieces to come and what hurrying does, as the board has them; only what changed is told.</summary>
    private void Refresh()
    {
        foreach (var square in Squares)
        {
            var at = square.Index;
            square.Piece = _board.PieceAt(at);
            square.Opening = _board.TapSide;
            var flows = _board.FlowsAt(at);
            if (!square.Flows.SequenceEqual(flows))
            {
                square.Flows = [.. flows];
            }
            square.Spill = _board.Spill is { } spill && spill.Square == at ? spill.Side : null;
            square.Countdown = at == _board.Tap ? _board.WaitLeft : 0;
        }
        for (var i = 0; i < Next.Count; i++)
        {
            Next[i].Piece = _board.Next[i];
        }
        NextName = $"Next: {string.Join(", then ", _board.Next.Select(p => Describe(p, PipeSide.Top)))}";
        HurryText = _board.IsOver || _board.IsFast ? null : _board.IsFlowing ? "Faster" : "Open now";
    }

    /// <summary>What a piece is, for screen readers: "corner, top to right".</summary>
    public static string Describe(PipeKind piece, PipeSide opening) => piece switch
    {
        PipeKind.Horizontal => "straight, left to right",
        PipeKind.Vertical => "straight, top to bottom",
        PipeKind.TopRight => "corner, top to right",
        PipeKind.RightBottom => "corner, right to bottom",
        PipeKind.BottomLeft => "corner, bottom to left",
        PipeKind.LeftTop => "corner, left to top",
        PipeKind.Cross => "crossing",
        PipeKind.Tap => $"Joulene's tap, facing {opening switch { PipeSide.Top => "up", PipeSide.Bottom => "down", _ => opening.ToString().ToLowerInvariant() }}",
        PipeKind.Rock => "a rock",
        _ => "empty",
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _flow.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>A square of the pipeline: a click lays the next piece on it.</summary>
public sealed partial class PipeSquare(PipelineViewModel game, int index) : ObservableObject
{
    public int Index => index;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(What), nameof(Name))]
    public partial PipeKind Piece { get; internal set; }

    /// <summary>The side her tap opens on, which only her tap's square draws.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(What), nameof(Name))]
    public partial PipeSide Opening { get; internal set; }

    /// <summary>The flow through it: none, one, or at a crossing two.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFlow), nameof(What), nameof(Name))]
    public partial IReadOnlyList<PipeFlow> Flows { get; internal set; } = [];

    /// <summary>The side the flow spilled out of it by, which ended the round.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(What), nameof(Name))]
    public partial PipeSide? Spill { get; internal set; }

    /// <summary>On her tap, how much of the wait for it is left, from 1 to 0, which its ring shows.</summary>
    [ObservableProperty]
    public partial double Countdown { get; internal set; }

    public bool HasFlow => Flows.Count > 0;

    /// <summary>What's on it: "corner, top to right, full".</summary>
    public string What
    {
        get
        {
            var what = PipelineViewModel.Describe(Piece, Opening);
            if (Spill is not null)
            {
                return $"{what}, spilled";
            }
            return Flows.Count == 0 ? what : Flows.All(f => f.IsFull) ? $"{what}, full" : $"{what}, filling";
        }
    }

    /// <summary>"Row 1, column 3, corner, top to right", for screen readers.</summary>
    public string Name => $"Row {index / Pipeline.Columns + 1}, column {index % Pipeline.Columns + 1}, {What}";

    [RelayCommand]
    private void Lay() => game.Lay(index);
}

/// <summary>One of the pieces to come: the next, which a click lays, shows larger.</summary>
public sealed partial class PipePiece(bool isNext) : ObservableObject
{
    public bool IsNext => isNext;

    [ObservableProperty]
    public partial PipeKind Piece { get; internal set; }
}
