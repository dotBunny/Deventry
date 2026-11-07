using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Deventry.App.Views;

/// <summary>
/// A line struck through the squares that won a game (DESIGN.md §18, "Games with Joulene"), from the middle of one
/// square of a grid to the middle of another and a little past each, over the board it's laid on. It takes no clicks.
/// </summary>
public sealed class GameStrike : Control
{
    public static readonly StyledProperty<int> ColumnsProperty = AvaloniaProperty.Register<GameStrike, int>(nameof(Columns), 3);

    public static readonly StyledProperty<int> RowsProperty = AvaloniaProperty.Register<GameStrike, int>(nameof(Rows), 3);

    public static readonly StyledProperty<int> FromProperty = AvaloniaProperty.Register<GameStrike, int>(nameof(From), -1);

    public static readonly StyledProperty<int> ToProperty = AvaloniaProperty.Register<GameStrike, int>(nameof(To), -1);

    public static readonly StyledProperty<IBrush?> BrushProperty = AvaloniaProperty.Register<GameStrike, IBrush?>(nameof(Brush));

    public static readonly StyledProperty<double> ThicknessProperty = AvaloniaProperty.Register<GameStrike, double>(nameof(Thickness), 6);

    static GameStrike()
    {
        AffectsRender<GameStrike>(ColumnsProperty, RowsProperty, FromProperty, ToProperty, BrushProperty, ThicknessProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<GameStrike>(false);
    }

    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public int Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <summary>The square it starts at, row by row from the top left; -1 for no line.</summary>
    public int From
    {
        get => GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    /// <summary>The square it ends at.</summary>
    public int To
    {
        get => GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public IBrush? Brush
    {
        get => GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (From < 0 || To < 0 || From == To || Columns <= 0 || Rows <= 0)
        {
            return;
        }
        var (width, height) = (Bounds.Width / Columns, Bounds.Height / Rows);
        Point Middle(int square) => new((square % Columns + 0.5) * width, (square / Columns + 0.5) * height);
        var (start, end) = (Middle(From), Middle(To));
        Vector along = end - start;
        var past = along / along.Length * (Math.Min(width, height) * 0.3);
        context.DrawLine(new Pen(Brush, Thickness, lineCap: PenLineCap.Round), start - past, end + past);
    }
}
