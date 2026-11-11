using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Deventry.App.Controls;

/// <summary>
/// Centers a control on a text block's first line (<see cref="CenterOnProperty"/>): a box, an arrow or an icon at the
/// start of a row whose text can wrap or go on to more lines, as Changed files' rows do (DESIGN.md §8, "The tree"). The
/// control sits at the row's top, level with the line it goes with, rather than with the middle of the whole row, and
/// the fonts, the code size and the density don't need a margin tuned for each. After each layout pass it moves the
/// control by the difference between the two middles, on whole screen pixels, with a render transform, so its layout,
/// and its row's height, stay as they were, as <see cref="Baseline"/> does for text.
/// </summary>
public static class FirstLine
{
    /// <summary>The text block whose first line this control is centered on.</summary>
    public static readonly AttachedProperty<TextBlock?> CenterOnProperty =
        AvaloniaProperty.RegisterAttached<Control, TextBlock?>("CenterOn", typeof(FirstLine));

    static FirstLine()
    {
        CenterOnProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            control.LayoutUpdated -= OnLayoutUpdated;
            if (e.GetNewValue<TextBlock?>() is not null)
            {
                control.LayoutUpdated += OnLayoutUpdated;
            }
            else
            {
                control.RenderTransform = null;
            }
        });
    }

    public static TextBlock? GetCenterOn(Control element) => element.GetValue(CenterOnProperty);

    public static void SetCenterOn(Control element, TextBlock? value) => element.SetValue(CenterOnProperty, value);

    /// <summary>How far down the control is moved, in pixels: up when it's negative.</summary>
    internal static double ShiftOf(Control control) => control.RenderTransform is TranslateTransform shift ? shift.Y : 0;

    /// <summary>The middle of a text block's first line, in <paramref name="relativeTo"/>'s coordinates.</summary>
    internal static double? MiddleIn(TextBlock text, Visual relativeTo) =>
        text.TextLayout.TextLines is [var first, ..]
            ? text.TranslatePoint(new Point(0, text.Padding.Top + first.Height / 2), relativeTo)?.Y
            : null;

    private static void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (sender is not Control control || GetCenterOn(control) is not { } text || !control.IsEffectivelyVisible || !text.IsEffectivelyVisible
            || TopLevel.GetTopLevel(control) is not { } top || MiddleIn(text, control) is not { } theirs)
        {
            return;
        }
        // Their middle is measured where this control is drawn, already moved by the shift it has now.
        var current = ShiftOf(control);
        var shift = current + theirs - control.Bounds.Height / 2;
        var scale = top.RenderScaling * (control.TransformToVisual(top)?.M22 is > 0 and var zoom ? zoom : 1);
        shift = Math.Round(shift * scale) / scale;
        if (Math.Abs(shift - current) > 0.01)
        {
            control.RenderTransform = shift == 0 ? null : new TranslateTransform(0, shift);
        }
    }
}
