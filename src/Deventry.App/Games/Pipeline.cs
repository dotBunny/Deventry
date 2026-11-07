namespace Deventry.App.Games;

/// <summary>A side of a square, where a pipe opens.</summary>
public enum PipeSide
{
    Top,
    Right,
    Bottom,
    Left,
}

/// <summary>
/// What's on a square of the pipeline: nothing, a piece of pipe (named by where it opens, a corner by its two sides
/// clockwise), Joulene's tap, where the flow starts, her cup, where it has to get to, or one of her rocks.
/// </summary>
public enum PipeKind
{
    Empty,
    Horizontal,
    Vertical,
    TopRight,
    RightBottom,
    BottomLeft,
    LeftTop,
    Cross,
    Tap,
    Rock,
    Cup,
}

/// <summary>
/// The flow through a square: the side it came in by (null in the tap, where it starts), the side it goes out by (null in
/// the cup, where it ends), and how full the way between them is, from 0 to 1.
/// </summary>
public readonly record struct PipeFlow(PipeSide? From, PipeSide? To, double Fill)
{
    public bool IsFull => Fill >= 1;
}

/// <summary>
/// A level's pipeline: the fewest pipes between her tap and her cup, her rocks, the wait before her tap opens, and how long
/// each pipe takes to fill.
/// </summary>
public sealed record PipelineRules(int Distance, int Rocks, TimeSpan Wait, TimeSpan PerPipe)
{
    public int WaitSteps => (int)(Wait / Pipeline.Step);

    public int StepsPerPipe => (int)(PerPipe / Pipeline.Step);
}

/// <summary>What a stretch of the flow did.</summary>
[Flags]
public enum PipelineEvents
{
    None = 0,
    TapOpened = 1,
    /// <summary>It went on into another pipe.</summary>
    Entered = 2,
    /// <summary>It filled her cup: the round is won.</summary>
    Reached = 4,
    /// <summary>The pipe it went into was laid only just before.</summary>
    JustInTime = 8,
    /// <summary>It came to an open end, the edge, a rock or a pipe that doesn't take it: the round is lost.</summary>
    Spilled = 16,
}

/// <summary>
/// The pipeline (DESIGN.md §18, "Games with Joulene"): eight squares by eight, row by row from the top left, with
/// Joulene's tap on one, her cup on another, and her rocks on some. The user lays the pieces she hands out, the next first,
/// on any square the flow hasn't reached, in place of what's there. Once her tap opens, the flow goes from pipe to pipe:
/// into her cup, by its open side, wins; spilling first loses. A crossing takes it twice, once each way. Time is in steps
/// of <see cref="Step"/>, which the caller ticks.
/// </summary>
public sealed class Pipeline
{
    public const int Columns = 8;
    public const int Rows = 8;
    public const int Size = Columns * Rows;

    /// <summary>How many of the pieces to come show: the next, and those after it.</summary>
    public const int Shown = 5;

    /// <summary>A tick of the flow.</summary>
    public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(50);

    /// <summary>How much faster the flow goes once the user hurries it.</summary>
    public const int FastFactor = 8;

    /// <summary>How many places for her tap she tries for one with her cup far enough off; past that, the furthest she found.</summary>
    private const int Tries = 40;

    private readonly PipeKind[] _pieces = new PipeKind[Size];
    private readonly List<PipeFlow>[] _flows = [.. Enumerable.Range(0, Size).Select(_ => new List<PipeFlow>())];

    /// <summary>When each square's piece was laid, in steps since the round began.</summary>
    private readonly long[] _laidAt = new long[Size];

    private readonly List<PipeKind> _next = [];
    private long _clock;
    private int _waitLeft;

    /// <summary>The square the flow is filling, and how far along it is in steps; null until her tap opens.</summary>
    private (int Square, int Steps)? _front;

    public Pipeline(PipelineRules rules, Random random)
    {
        Rules = rules;
        _waitLeft = rules.WaitSteps;
        Place(random);
        Deal(random);
    }

    /// <summary>A pipeline with her tap and cup where a test wants them, and no rocks.</summary>
    internal Pipeline(PipelineRules rules, Random random, int tap, PipeSide tapSide, int cup, PipeSide cupSide)
    {
        Rules = rules;
        _waitLeft = rules.WaitSteps;
        (Tap, TapSide, Cup, CupSide) = (tap, tapSide, cup, cupSide);
        _pieces[Tap] = PipeKind.Tap;
        _pieces[Cup] = PipeKind.Cup;
        Distance = Shortest(Beside(Tap, TapSide)!.Value, Cup)[Beside(Cup, CupSide)!.Value] + 1;
        Deal(random);
    }

    /// <summary>
    /// Each level's pipeline: how far her cup is from her tap, how many rocks she puts in the way, how long until her tap
    /// opens and how long a pipe takes to fill.
    /// </summary>
    public static PipelineRules RulesFor(GameLevel level) => level switch
    {
        GameLevel.Easy => new(Distance: 5, Rocks: 0, Wait: TimeSpan.FromSeconds(25), PerPipe: TimeSpan.FromSeconds(4)),
        GameLevel.Hard => new(Distance: 10, Rocks: 4, Wait: TimeSpan.FromSeconds(15), PerPipe: TimeSpan.FromSeconds(2.4)),
        _ => new(Distance: 8, Rocks: 2, Wait: TimeSpan.FromSeconds(20), PerPipe: TimeSpan.FromSeconds(3)),
    };

    public PipelineRules Rules { get; }

    public int Tap { get; private set; }

    /// <summary>The side her tap opens on.</summary>
    public PipeSide TapSide { get; private set; }

    public int Cup { get; private set; }

    /// <summary>The side her cup takes the flow in by.</summary>
    public PipeSide CupSide { get; private set; }

    /// <summary>The fewest pipes that join her tap to her cup, round her rocks.</summary>
    public int Distance { get; private set; }

    /// <summary>The pieces she'll hand out, the next first.</summary>
    public IReadOnlyList<PipeKind> Next => _next;

    /// <summary>Her tap is open: the flow is on its way.</summary>
    public bool IsFlowing => _front is not null;

    /// <summary>The user hurried the flow: it runs <see cref="FastFactor"/> times as fast till the round ends.</summary>
    public bool IsFast { get; private set; }

    /// <summary>The pipes the flow has been through, a crossing once each way; her tap and cup aren't.</summary>
    public int Filled { get; private set; }

    /// <summary>The flow filled her cup.</summary>
    public bool IsReached { get; private set; }

    /// <summary>Where the flow spilled: the square it left and the side it left by.</summary>
    public (int Square, PipeSide Side)? Spill { get; private set; }

    /// <summary>The square the flow is filling, until the round ends.</summary>
    public int? Front => IsOver ? null : _front?.Square;

    public GameOutcome Outcome => IsReached ? GameOutcome.Won : Spill is not null ? GameOutcome.Lost : GameOutcome.Playing;

    public bool IsOver => Outcome != GameOutcome.Playing;

    /// <summary>How much of the wait for her tap is left, from 1 to 0.</summary>
    public double WaitLeft => Rules.WaitSteps == 0 ? 0 : (double)_waitLeft / Rules.WaitSteps;

    /// <summary>How long until her tap opens, in whole seconds, rounded up; 0 once it has.</summary>
    public int SecondsToTap => (int)Math.Ceiling(_waitLeft * Step.TotalSeconds - 1e-9);

    public PipeKind PieceAt(int square) => _pieces[square];

    /// <summary>The flow through <paramref name="square"/>: none, one, or at a crossing two.</summary>
    public IReadOnlyList<PipeFlow> FlowsAt(int square) => _flows[square];

    /// <summary>The square next to <paramref name="square"/> on <paramref name="side"/>, or null at the edge.</summary>
    public static int? Beside(int square, PipeSide side)
    {
        var (row, column) = (square / Columns, square % Columns);
        (row, column) = side switch
        {
            PipeSide.Top => (row - 1, column),
            PipeSide.Right => (row, column + 1),
            PipeSide.Bottom => (row + 1, column),
            _ => (row, column - 1),
        };
        return row is >= 0 and < Rows && column is >= 0 and < Columns ? row * Columns + column : null;
    }

    public static PipeSide Opposite(PipeSide side) => (PipeSide)(((int)side + 2) % 4);

    /// <summary>The sides a piece of pipe opens on; none for her tap and cup, which open where each faces.</summary>
    public static IReadOnlyList<PipeSide> Openings(PipeKind kind) => kind switch
    {
        PipeKind.Horizontal => [PipeSide.Left, PipeSide.Right],
        PipeKind.Vertical => [PipeSide.Top, PipeSide.Bottom],
        PipeKind.TopRight => [PipeSide.Top, PipeSide.Right],
        PipeKind.RightBottom => [PipeSide.Right, PipeSide.Bottom],
        PipeKind.BottomLeft => [PipeSide.Bottom, PipeSide.Left],
        PipeKind.LeftTop => [PipeSide.Left, PipeSide.Top],
        PipeKind.Cross => [PipeSide.Top, PipeSide.Right, PipeSide.Bottom, PipeSide.Left],
        _ => [],
    };

    /// <summary>The side the flow leaves a piece of pipe by, coming in by <paramref name="from"/>; null when it doesn't take it there.</summary>
    public static PipeSide? Through(PipeKind kind, PipeSide from)
    {
        if (kind == PipeKind.Cross)
        {
            return Opposite(from);
        }
        var openings = Openings(kind);
        return openings.Contains(from) ? openings.Single(s => s != from) : null;
    }

    /// <summary>Whether the next piece can go on <paramref name="square"/>: the round goes on, and it's neither hers nor reached by the flow.</summary>
    public bool CanLay(int square) =>
        !IsOver && square is >= 0 and < Size && _pieces[square] is not (PipeKind.Tap or PipeKind.Cup or PipeKind.Rock) && _flows[square].Count == 0;

    /// <summary>Lays the next piece on <paramref name="square"/>, in place of what's there; she hands out another. False when it can't go there.</summary>
    public bool Lay(int square, Random random)
    {
        if (!CanLay(square))
        {
            return false;
        }
        _pieces[square] = _next[0];
        _laidAt[square] = _clock;
        _next.RemoveAt(0);
        Deal(random);
        return true;
    }

    /// <summary>
    /// <b>Open now</b> before her tap opens, which opens it; <b>Faster</b> after, which hurries the flow till the round
    /// ends. False when there's nothing to hurry.
    /// </summary>
    public bool Hurry()
    {
        if (IsOver || IsFast)
        {
            return false;
        }
        if (_front is null)
        {
            _waitLeft = 0;
            OpenTap();
            return true;
        }
        IsFast = true;
        return true;
    }

    /// <summary>
    /// Moves time on by <paramref name="steps"/>: the wait for her tap, then the flow from pipe to pipe, faster once hurried.
    /// Says what happened.
    /// </summary>
    public PipelineEvents Tick(int steps = 1)
    {
        if (IsOver || steps <= 0)
        {
            return PipelineEvents.None;
        }
        var happened = PipelineEvents.None;
        if (_front is null)
        {
            var waited = Math.Min(steps, _waitLeft);
            _waitLeft -= waited;
            _clock += waited;
            steps -= waited;
            if (_waitLeft > 0)
            {
                return happened;
            }
            OpenTap();
            happened |= PipelineEvents.TapOpened;
        }
        if (IsFast)
        {
            steps *= FastFactor;
        }
        var perPipe = Rules.StepsPerPipe;
        while (steps > 0)
        {
            var (square, done) = _front!.Value;
            var take = Math.Min(steps, perPipe - done);
            done += take;
            steps -= take;
            _clock += take;
            var flows = _flows[square];
            var flow = flows[^1] with { Fill = (double)done / perPipe };
            flows[^1] = flow;
            _front = (square, done);
            if (done < perPipe)
            {
                break;
            }
            if (flow.To is not { } to)
            {
                IsReached = true;
                return happened | PipelineEvents.Reached;
            }
            if (square != Tap)
            {
                Filled++;
            }
            if (Into(square, to) is not { } next)
            {
                Spill = (square, to);
                return happened | PipelineEvents.Spilled;
            }
            happened |= PipelineEvents.Entered;
            if (_clock - _laidAt[next.Square] < perPipe / 3)
            {
                happened |= PipelineEvents.JustInTime;
            }
            _flows[next.Square].Add(new PipeFlow(next.From, next.To, 0));
            _front = (next.Square, 0);
        }
        return happened;
    }

    /// <summary>
    /// Where the flow goes leaving <paramref name="square"/> by <paramref name="side"/>: the square beside it, the side it
    /// comes in by and the side it'll leave by, none in her cup. Null when it spills there: at the edge, a rock, her tap, her
    /// cup but for its open side, or a square with no pipe open on that side. It never comes back into a pipe it's been
    /// through the same way: each side the pipe opens on leads to a pipe the flow has been through on that side already,
    /// and only a crossing takes it twice, once each way.
    /// </summary>
    private (int Square, PipeSide From, PipeSide? To)? Into(int square, PipeSide side)
    {
        if (Beside(square, side) is not { } next)
        {
            return null;
        }
        var from = Opposite(side);
        if (next == Cup)
        {
            return from == CupSide ? (next, from, null) : null;
        }
        return Through(_pieces[next], from) is { } to ? (next, from, to) : null;
    }

    private void OpenTap()
    {
        _flows[Tap].Add(new PipeFlow(null, TapSide, 0));
        _front = (Tap, 0);
    }

    /// <summary>
    /// Puts her tap off the edge, facing onto the board; her rocks two squares or more from it every way, so the flow
    /// always has room to start; and her cup where the fewest pipes that join them, round the rocks, are as many as the
    /// level asks or a couple more. A tap near the middle may have no square that far off, so she tries other places for
    /// it, and past <see cref="Tries"/> takes the furthest cup she found.
    /// </summary>
    private void Place(Random random)
    {
        (int Tap, PipeSide TapSide, int[] Rocks, int Cup, PipeSide CupSide, int Distance)? best = null;
        for (var attempt = 0; attempt < Tries; attempt++)
        {
            var tap = (1 + random.Next(Rows - 2)) * Columns + 1 + random.Next(Columns - 2);
            var tapSide = (PipeSide)random.Next(4);
            var rocks = RockPlaces(tap, random);
            var cups = Cups(tap, tapSide, rocks);
            var far = cups.Where(c => c.Distance >= Rules.Distance).ToList();
            if (far.Count > 0)
            {
                var near = far.Where(c => c.Distance <= Rules.Distance + 2).ToList();
                var pick = near.Count > 0 ? near[random.Next(near.Count)] : far.MinBy(c => c.Distance);
                best = (tap, tapSide, rocks, pick.Cup, pick.Side, pick.Distance);
                break;
            }
            if (cups.Count > 0 && cups.MaxBy(c => c.Distance) is var furthest && (best is null || furthest.Distance > best.Value.Distance))
            {
                best = (tap, tapSide, rocks, furthest.Cup, furthest.Side, furthest.Distance);
            }
        }
        var laid = best!.Value;
        (Tap, TapSide, Cup, CupSide, Distance) = (laid.Tap, laid.TapSide, laid.Cup, laid.CupSide, laid.Distance);
        _pieces[Tap] = PipeKind.Tap;
        _pieces[Cup] = PipeKind.Cup;
        foreach (var rock in laid.Rocks)
        {
            _pieces[rock] = PipeKind.Rock;
        }
    }

    /// <summary>Her rocks, the first few of a shuffle of the squares two or more from her tap every way.</summary>
    private int[] RockPlaces(int tap, Random random)
    {
        var (tapRow, tapColumn) = (tap / Columns, tap % Columns);
        var places = Enumerable.Range(0, Size)
            .Where(s => Math.Max(Math.Abs(s / Columns - tapRow), Math.Abs(s % Columns - tapColumn)) >= 2)
            .ToArray();
        for (var i = 0; i < Rules.Rocks; i++)
        {
            var pick = i + random.Next(places.Length - i);
            (places[i], places[pick]) = (places[pick], places[i]);
        }
        return places[..Rules.Rocks];
    }

    /// <summary>
    /// Every place for her cup and the side it opens on, with the fewest pipes that join her tap to it: from the square her
    /// tap faces to the one beside the cup's open side, both counted, round her rocks and the cup itself.
    /// </summary>
    private List<(int Cup, PipeSide Side, int Distance)> Cups(int tap, PipeSide tapSide, int[] rocks)
    {
        var start = Beside(tap, tapSide)!.Value;
        var blocked = new bool[Size];
        blocked[tap] = true;
        foreach (var rock in rocks)
        {
            blocked[rock] = true;
        }
        var cups = new List<(int, PipeSide, int)>();
        for (var cup = 0; cup < Size; cup++)
        {
            if (blocked[cup] || cup == start)
            {
                continue;
            }
            blocked[cup] = true;
            var distances = Shortest(start, blocked);
            blocked[cup] = false;
            foreach (var side in Enum.GetValues<PipeSide>())
            {
                if (Beside(cup, side) is { } inlet && distances[inlet] >= 0)
                {
                    cups.Add((cup, side, distances[inlet] + 1));
                }
            }
        }
        return cups;
    }

    /// <summary>How many steps each square is from <paramref name="start"/>, around her tap, rocks and <paramref name="cup"/>; -1 where it can't be reached.</summary>
    private int[] Shortest(int start, int cup)
    {
        var blocked = _pieces.Select(p => p is PipeKind.Tap or PipeKind.Rock).ToArray();
        blocked[cup] = true;
        return Shortest(start, blocked);
    }

    /// <summary>How many steps each square is from <paramref name="start"/>, around the squares <paramref name="blocked"/>; -1 where it can't be reached.</summary>
    private static int[] Shortest(int start, bool[] blocked)
    {
        var distances = Enumerable.Repeat(-1, Size).ToArray();
        distances[start] = 0;
        var queue = new Queue<int>([start]);
        while (queue.TryDequeue(out var at))
        {
            foreach (var side in Enum.GetValues<PipeSide>())
            {
                if (Beside(at, side) is { } next && !blocked[next] && distances[next] < 0)
                {
                    distances[next] = distances[at] + 1;
                    queue.Enqueue(next);
                }
            }
        }
        return distances;
    }

    /// <summary>Tops up the pieces to come: a corner six times in ten, a straight piece three, a crossing now and then.</summary>
    private void Deal(Random random)
    {
        while (_next.Count < Shown)
        {
            _next.Add(random.Next(13) switch
            {
                < 2 => PipeKind.Horizontal,
                < 4 => PipeKind.Vertical,
                < 6 => PipeKind.TopRight,
                < 8 => PipeKind.RightBottom,
                < 10 => PipeKind.BottomLeft,
                < 12 => PipeKind.LeftTop,
                _ => PipeKind.Cross,
            });
        }
    }

    /// <summary>Lays <paramref name="kind"/> on <paramref name="square"/> as if it had been laid at the start, for tests.</summary>
    internal void Set(int square, PipeKind kind) => _pieces[square] = kind;
}
