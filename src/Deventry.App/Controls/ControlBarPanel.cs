using Avalonia;
using Avalonia.Controls;

namespace Deventry.App.Controls;

/// <summary>
/// The composer's control bar (DESIGN.md §5): its first child at the start and its last at the end, on one line when
/// both fit, or else the last on a line of its own under the first, still at the end. The chips on the left can outgrow
/// a narrow window, and Send, on the right, must never be pushed out of view. Once they're on lines of their own, each
/// gets only the bar's width, so one that's wider still (the chips or the counts, in a narrow pane) and wraps itself
/// takes more lines rather than running past the end into whatever is beside the bar.
/// </summary>
public sealed class ControlBarPanel : Panel
{
    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<ControlBarPanel, double>(nameof(RowSpacing), 4);

    /// <summary>The space between the lines, when the bar takes two.</summary>
    public double RowSpacing
    {
        get => GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    static ControlBarPanel()
    {
        AffectsMeasure<ControlBarPanel>(RowSpacingProperty);
    }

    /// <summary>Whether the bar took two lines (or more) at its last measure.</summary>
    public bool IsWrapped { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0)
        {
            return default;
        }
        var unbounded = new Size(double.PositiveInfinity, availableSize.Height);
        foreach (var child in Children)
        {
            child.Measure(unbounded);
        }
        IsWrapped = !Fits(availableSize.Width);
        if (IsWrapped)
        {
            foreach (var child in new[] { Children[0], Children[^1] }.Where(c => c.DesiredSize.Width > availableSize.Width))
            {
                child.Measure(availableSize);
            }
        }
        var (start, end) = (Children[0].DesiredSize, Children[^1].DesiredSize);
        return !IsWrapped
            ? new Size(start.Width + end.Width, Math.Max(start.Height, end.Height))
            : new Size(Math.Max(start.Width, end.Width), start.Height + RowSpacing + end.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0)
        {
            return finalSize;
        }
        var (first, last) = (Children[0], Children[^1]);
        var (start, end) = (first.DesiredSize, last.DesiredSize);
        if (!IsWrapped)
        {
            // Each group at its own height, centered on the line, as in a grid row. Given the whole line, a WrapPanel
            // (the chips) would lay its line out at the top, above the middle of the taller group's (the counts and Send).
            var height = Math.Max(start.Height, end.Height);
            first.Arrange(new Rect(0, (height - start.Height) / 2, start.Width, start.Height));
            if (!ReferenceEquals(first, last))
            {
                last.Arrange(new Rect(finalSize.Width - end.Width, (height - end.Height) / 2, end.Width, end.Height));
            }
        }
        else
        {
            first.Arrange(new Rect(0, 0, start.Width, start.Height));
            last.Arrange(new Rect(Math.Max(0, finalSize.Width - end.Width), start.Height + RowSpacing, end.Width, end.Height));
        }
        return finalSize;
    }

    private bool Fits(double width) =>
        Children.Count < 2 || double.IsInfinity(width) || Children[0].DesiredSize.Width + Children[^1].DesiredSize.Width <= width;
}
