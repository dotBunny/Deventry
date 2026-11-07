using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Deventry.App.Games;

namespace Deventry.App.Views;

/// <summary>
/// A square of the pipeline, drawn (DESIGN.md §18, "Games with Joulene"): a slot in the board, its piece of pipe along the
/// square's middle, a straight one across it and a corner as a quarter circle round the corner its sides share, and the
/// flow in it as far as it's got from the side it came in by. The squares touch, so pipes side by side join. Joulene's tap,
/// where the flow starts, is a ring in her colour whose rim runs down as her countdown does, then fills as the flow
/// starts, with a spout to the side it faces and an arrow out of it; her cup, where it has to get to, is a basin in gold,
/// with an arrow into its open side, which fills when it does. Both their squares are tinted, so the two ends stand out.
/// A rock is a pebble. Under the pointer, a square the next piece can go on shows it faintly.
/// </summary>
public sealed class PipeTile : Control
{
    public static readonly StyledProperty<PipeKind> PieceProperty = AvaloniaProperty.Register<PipeTile, PipeKind>(nameof(Piece));

    public static readonly StyledProperty<PipeSide> OpeningProperty = AvaloniaProperty.Register<PipeTile, PipeSide>(nameof(Opening));

    public static readonly StyledProperty<IReadOnlyList<PipeFlow>?> FlowsProperty =
        AvaloniaProperty.Register<PipeTile, IReadOnlyList<PipeFlow>?>(nameof(Flows));

    public static readonly StyledProperty<PipeSide?> SpillProperty = AvaloniaProperty.Register<PipeTile, PipeSide?>(nameof(Spill));

    public static readonly StyledProperty<double> CountdownProperty = AvaloniaProperty.Register<PipeTile, double>(nameof(Countdown));

    public static readonly StyledProperty<PipeKind> GhostProperty = AvaloniaProperty.Register<PipeTile, PipeKind>(nameof(Ghost));

    public static readonly StyledProperty<bool> IsHotProperty = AvaloniaProperty.Register<PipeTile, bool>(nameof(IsHot));

    public static readonly StyledProperty<bool> ShowsSlotProperty = AvaloniaProperty.Register<PipeTile, bool>(nameof(ShowsSlot), true);

    public static readonly StyledProperty<IBrush?> SlotBrushProperty = AvaloniaProperty.Register<PipeTile, IBrush?>(nameof(SlotBrush));

    public static readonly StyledProperty<IBrush?> SlotHotBrushProperty = AvaloniaProperty.Register<PipeTile, IBrush?>(nameof(SlotHotBrush));

    public static readonly StyledProperty<IBrush?> PipeBrushProperty = AvaloniaProperty.Register<PipeTile, IBrush?>(nameof(PipeBrush));

    public static readonly StyledProperty<IBrush?> FlowBrushProperty = AvaloniaProperty.Register<PipeTile, IBrush?>(nameof(FlowBrush));

    public static readonly StyledProperty<IBrush?> RockBrushProperty = AvaloniaProperty.Register<PipeTile, IBrush?>(nameof(RockBrush));

    public static readonly StyledProperty<IBrush?> GoalBrushProperty = AvaloniaProperty.Register<PipeTile, IBrush?>(nameof(GoalBrush));

    /// <summary>How strongly a laid pipe shows, so the flow in it stands out.</summary>
    private const double PipeOpacity = 0.5;

    static PipeTile()
    {
        AffectsRender<PipeTile>(PieceProperty, OpeningProperty, FlowsProperty, SpillProperty, CountdownProperty, GhostProperty, IsHotProperty,
            ShowsSlotProperty, SlotBrushProperty, SlotHotBrushProperty, PipeBrushProperty, FlowBrushProperty, RockBrushProperty, GoalBrushProperty);
    }

    public PipeKind Piece
    {
        get => GetValue(PieceProperty);
        set => SetValue(PieceProperty, value);
    }

    /// <summary>The side Joulene's tap faces, on her tap.</summary>
    public PipeSide Opening
    {
        get => GetValue(OpeningProperty);
        set => SetValue(OpeningProperty, value);
    }

    public IReadOnlyList<PipeFlow>? Flows
    {
        get => GetValue(FlowsProperty);
        set => SetValue(FlowsProperty, value);
    }

    /// <summary>The side the flow spilled out by: a splash there.</summary>
    public PipeSide? Spill
    {
        get => GetValue(SpillProperty);
        set => SetValue(SpillProperty, value);
    }

    /// <summary>On her tap, how much of her countdown is left, from 1 to 0: the ring's rim.</summary>
    public double Countdown
    {
        get => GetValue(CountdownProperty);
        set => SetValue(CountdownProperty, value);
    }

    /// <summary>The piece a click would lay, shown faintly while <see cref="IsHot"/>.</summary>
    public PipeKind Ghost
    {
        get => GetValue(GhostProperty);
        set => SetValue(GhostProperty, value);
    }

    /// <summary>The pointer is over it.</summary>
    public bool IsHot
    {
        get => GetValue(IsHotProperty);
        set => SetValue(IsHotProperty, value);
    }

    /// <summary>Draws its slot in the board; not for the pieces to come, which have their own.</summary>
    public bool ShowsSlot
    {
        get => GetValue(ShowsSlotProperty);
        set => SetValue(ShowsSlotProperty, value);
    }

    public IBrush? SlotBrush
    {
        get => GetValue(SlotBrushProperty);
        set => SetValue(SlotBrushProperty, value);
    }

    /// <summary>Its slot under the pointer, or with the keyboard's focus.</summary>
    public IBrush? SlotHotBrush
    {
        get => GetValue(SlotHotBrushProperty);
        set => SetValue(SlotHotBrushProperty, value);
    }

    /// <summary>The pipes' colour, which they're drawn in faintly, so the flow shows in them.</summary>
    public IBrush? PipeBrush
    {
        get => GetValue(PipeBrushProperty);
        set => SetValue(PipeBrushProperty, value);
    }

    /// <summary>The flow's colour, and her tap's.</summary>
    public IBrush? FlowBrush
    {
        get => GetValue(FlowBrushProperty);
        set => SetValue(FlowBrushProperty, value);
    }

    public IBrush? RockBrush
    {
        get => GetValue(RockBrushProperty);
        set => SetValue(RockBrushProperty, value);
    }

    /// <summary>Her cup's colour: where the flow has to get to.</summary>
    public IBrush? GoalBrush
    {
        get => GetValue(GoalBrushProperty);
        set => SetValue(GoalBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
        {
            return;
        }
        using var _ = context.PushTransform(Matrix.CreateTranslation((Bounds.Width - size) / 2, (Bounds.Height - size) / 2));
        var flows = Flows ?? [];
        var ghost = IsHot && Ghost != PipeKind.Empty && flows.Count == 0 && Piece is not (PipeKind.Tap or PipeKind.Cup or PipeKind.Rock);
        if (ShowsSlot)
        {
            var slot = new Rect(1, 1, size - 2, size - 2);
            context.DrawRectangle(IsHot ? SlotHotBrush : SlotBrush, null, slot, size * 0.14, size * 0.14);
            // Her tap's square and her cup's, tinted in their colours.
            if (Piece is PipeKind.Tap or PipeKind.Cup)
            {
                using (context.PushOpacity(0.2))
                {
                    context.DrawRectangle(Piece == PipeKind.Tap ? FlowBrush : GoalBrush, null, slot, size * 0.14, size * 0.14);
                }
            }
        }
        switch (Piece)
        {
            case PipeKind.Rock:
                DrawRock(context, size);
                return;
            case PipeKind.Tap:
                DrawTap(context, size, flows);
                break;
            case PipeKind.Cup:
                DrawCup(context, size, flows);
                break;
            default:
                // The piece a click would put in its place shows over it, which fades.
                using (context.PushOpacity(ghost ? 0.25 : 1))
                {
                    DrawPipe(context, size, Piece);
                }
                break;
        }
        if (ghost)
        {
            using (context.PushOpacity(0.6))
            {
                DrawPipe(context, size, Ghost);
            }
        }
        foreach (var flow in flows)
        {
            if (flow is { From: { } from, To: { } to, Fill: > 0 })
            {
                context.DrawGeometry(null, FlowPen(size), Path(size, from, to, flow.Fill));
            }
        }
        if (Spill is { } spill)
        {
            DrawSplash(context, size, spill);
        }
    }

    private Pen PipePen(double size) => new(PipeBrush, size * 0.42, lineCap: PenLineCap.Flat);

    private Pen FlowPen(double size) => new(FlowBrush, size * 0.24, lineCap: PenLineCap.Flat);

    private void DrawPipe(DrawingContext context, double size, PipeKind piece)
    {
        var openings = Pipeline.Openings(piece);
        if (openings.Count == 0)
        {
            return;
        }
        using var _ = context.PushOpacity(PipeOpacity);
        var pen = PipePen(size);
        if (piece == PipeKind.Cross)
        {
            context.DrawGeometry(null, pen, Path(size, PipeSide.Left, PipeSide.Right, 1));
            context.DrawGeometry(null, pen, Path(size, PipeSide.Top, PipeSide.Bottom, 1));
            return;
        }
        context.DrawGeometry(null, pen, Path(size, openings[0], openings[1], 1));
    }

    /// <summary>
    /// Her tap: a spout to the side it faces, filling as the flow leaves it, and a ring round the middle whose rim runs
    /// down with her countdown, full once it's open.
    /// </summary>
    private void DrawTap(DrawingContext context, double size, IReadOnlyList<PipeFlow> flows)
    {
        var center = new Point(size / 2, size / 2);
        var edge = Mid(size, Opening);
        // Her spout is hers, not one of the user's pipes.
        using (context.PushOpacity(0.35))
        {
            context.DrawLine(new Pen(FlowBrush, size * 0.42, lineCap: PenLineCap.Flat), center, edge);
        }
        DrawArrow(context, size, Opening, outward: true, FlowBrush);
        var radius = size * 0.25;
        var rim = new Pen(FlowBrush, size * 0.1, lineCap: PenLineCap.Round);
        if (flows is [var flow, ..])
        {
            if (flow.Fill > 0)
            {
                context.DrawLine(FlowPen(size), center, center + (edge - center) * Math.Min(flow.Fill, 1));
            }
            context.DrawEllipse(FlowBrush, rim, center, radius, radius);
            return;
        }
        // Closed: the rim's track, and as much of it as her countdown has left, from the top round clockwise.
        using (context.PushOpacity(0.3))
        {
            context.DrawEllipse(null, rim, center, radius, radius);
        }
        var left = Math.Clamp(Countdown, 0, 1);
        if (left >= 1)
        {
            context.DrawEllipse(null, rim, center, radius, radius);
        }
        else if (left > 0)
        {
            var end = -Math.PI / 2 + left * 2 * Math.PI;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(center + new Vector(0, -radius), false);
                g.ArcTo(center + new Vector(Math.Cos(end), Math.Sin(end)) * radius, new Size(radius, radius), 0, left > 0.5, SweepDirection.Clockwise);
                g.EndFigure(false);
            }
            context.DrawGeometry(null, rim, geometry);
        }
    }

    /// <summary>
    /// Her cup: a basin in the middle, with an inlet to its open side and an arrow into it. The flow runs down the inlet,
    /// then fills the basin.
    /// </summary>
    private void DrawCup(DrawingContext context, double size, IReadOnlyList<PipeFlow> flows)
    {
        var basin = new Rect(size * 0.24, size * 0.24, size * 0.52, size * 0.52);
        var edge = Mid(size, Opening);
        var mouth = new Point(size / 2, size / 2) + (edge - new Point(size / 2, size / 2)) * (0.24 / 0.5);
        using (context.PushOpacity(0.35))
        {
            context.DrawLine(new Pen(GoalBrush, size * 0.42, lineCap: PenLineCap.Flat), edge, mouth);
        }
        var fill = flows is [var flow, ..] ? flow.Fill : 0;
        if (fill > 0)
        {
            // The first half of its time down the inlet, the second filling the basin.
            context.DrawLine(FlowPen(size), edge, edge + (mouth - edge) * Math.Min(fill * 2, 1));
            if (fill > 0.5)
            {
                using (context.PushOpacity(Math.Min((fill - 0.5) * 2, 1)))
                {
                    context.DrawRectangle(FlowBrush, null, basin, size * 0.1, size * 0.1);
                }
            }
        }
        context.DrawRectangle(null, new Pen(GoalBrush, size * 0.09), basin, size * 0.1, size * 0.1);
        if (fill == 0)
        {
            DrawArrow(context, size, Opening, outward: false, GoalBrush);
        }
    }

    /// <summary>A chevron on the way through <paramref name="side"/>: out of her tap's spout, or into her cup's inlet.</summary>
    private static void DrawArrow(DrawingContext context, double size, PipeSide side, bool outward, IBrush? brush)
    {
        var direction = outward ? Outward(side) : -Outward(side);
        var across = new Vector(direction.Y, direction.X);
        // Between the middle and the edge, nearer the edge.
        var tip = new Point(size / 2, size / 2) + Outward(side) * (size * (outward ? 0.47 : 0.33));
        var back = tip - direction * (size * 0.12);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(back + across * (size * 0.12), false);
            g.LineTo(tip);
            g.LineTo(back - across * (size * 0.12));
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(brush, size * 0.07, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
    }

    private void DrawRock(DrawingContext context, double size)
    {
        // A pebble: wider than tall, a little lopsided.
        Point[] points = [new(0.22, 0.64), new(0.3, 0.4), new(0.5, 0.28), new(0.72, 0.36), new(0.8, 0.6), new(0.64, 0.74), new(0.38, 0.76)];
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(points[0] * size, true);
            foreach (var point in points.Skip(1))
            {
                g.LineTo(point * size);
            }
            g.EndFigure(true);
        }
        context.DrawGeometry(RockBrush, new Pen(RockBrush, size * 0.08, lineJoin: PenLineJoin.Round), geometry);
    }

    /// <summary>Drops thrown out of the side the flow left by.</summary>
    private void DrawSplash(DrawingContext context, double size, PipeSide side)
    {
        var edge = Mid(size, side);
        var outward = Outward(side);
        var across = new Vector(outward.Y, outward.X);
        var drop = size * 0.09;
        foreach (var (back, aside, scale) in new[] { (0.06, 0.0, 1.3), (0.16, 0.26, 0.9), (0.16, -0.26, 0.9), (0.28, 0.14, 0.7), (0.28, -0.16, 0.6) })
        {
            var at = edge - outward * (back * size) + across * (aside * size);
            context.DrawEllipse(FlowBrush, null, at, drop * scale, drop * scale);
        }
    }

    /// <summary>The middle of the pipe from <paramref name="from"/> to <paramref name="to"/>, as far as <paramref name="fill"/> of the way.</summary>
    private static StreamGeometry Path(double size, PipeSide from, PipeSide to, double fill)
    {
        var start = Mid(size, from);
        var end = Mid(size, to);
        fill = Math.Min(fill, 1);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(start, false);
            if (to == Pipeline.Opposite(from))
            {
                g.LineTo(start + (end - start) * fill);
            }
            else
            {
                // A quarter circle round the corner the two sides share, as far as the fill.
                var corner = Corner(size, from, to);
                var radius = size / 2;
                var (a, b) = (Angle(start - corner), Angle(end - corner));
                if (b - a > Math.PI)
                {
                    b -= 2 * Math.PI;
                }
                else if (a - b > Math.PI)
                {
                    b += 2 * Math.PI;
                }
                var at = a + (b - a) * fill;
                g.ArcTo(corner + new Vector(Math.Cos(at), Math.Sin(at)) * radius, new Size(radius, radius), 0, false,
                    b > a ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
            }
            g.EndFigure(false);
        }
        return geometry;
    }

    private static double Angle(Vector v) => Math.Atan2(v.Y, v.X);

    private static Point Mid(double size, PipeSide side) => side switch
    {
        PipeSide.Top => new Point(size / 2, 0),
        PipeSide.Right => new Point(size, size / 2),
        PipeSide.Bottom => new Point(size / 2, size),
        _ => new Point(0, size / 2),
    };

    private static Vector Outward(PipeSide side) => side switch
    {
        PipeSide.Top => new Vector(0, -1),
        PipeSide.Right => new Vector(1, 0),
        PipeSide.Bottom => new Vector(0, 1),
        _ => new Vector(-1, 0),
    };

    /// <summary>The corner of the square two sides at right angles share.</summary>
    private static Point Corner(double size, PipeSide a, PipeSide b)
    {
        var x = a == PipeSide.Right || b == PipeSide.Right ? size : 0;
        var y = a == PipeSide.Bottom || b == PipeSide.Bottom ? size : 0;
        return new Point(x, y);
    }
}
