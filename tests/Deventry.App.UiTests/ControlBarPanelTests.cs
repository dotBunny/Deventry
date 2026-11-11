using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Deventry.App.Controls;

namespace Deventry.App.UiTests;

/// <summary>
/// The composer's control bar (DESIGN.md §5): the chips at the start and the counts and Send at the end, on one line
/// when they fit, or the end's group on a second line, still at the end.
/// </summary>
public class ControlBarPanelTests
{
    private static (ControlBarPanel Bar, Border Chips, Border Send) Show(double width)
    {
        var chips = new Border { Width = 300, Height = 24 };
        var send = new Border { Width = 200, Height = 30 };
        var bar = new ControlBarPanel { RowSpacing = 4, Children = { chips, send } };
        UiText.Show(new Border { Width = width, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = bar });
        return (bar, chips, send);
    }

    [AvaloniaFact]
    public void With_room_both_groups_share_a_line_at_either_end()
    {
        var (bar, chips, send) = Show(600);

        Assert.False(bar.IsWrapped);
        Assert.Equal(30, bar.Bounds.Height);
        // The shorter group is centered on the line, as in a grid row.
        Assert.Equal(new Rect(0, 3, 300, 24), chips.Bounds);
        Assert.Equal(new Rect(400, 0, 200, 30), send.Bounds);
    }

    [AvaloniaFact]
    public void Chips_in_a_wrap_panel_are_centered_on_the_line_too()
    {
        // As in the composer: a WrapPanel has no height of its own, and given the whole line it lays its own out at the top.
        var chip = new Border { Width = 100, Height = 24 };
        var chips = new WrapPanel { ItemSpacing = 2, LineSpacing = 4, Children = { chip } };
        var send = new Border { Width = 200, Height = 30 };
        var bar = new ControlBarPanel { RowSpacing = 4, Children = { chips, send } };
        UiText.Show(new Border { Width = 600, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = bar });

        Assert.False(bar.IsWrapped);
        Assert.Equal(new Point(0, 3), chip.TranslatePoint(default, bar));
    }

    [AvaloniaFact]
    public void Without_room_the_end_group_moves_under_the_chips_still_at_the_end()
    {
        var (bar, chips, send) = Show(450);

        Assert.True(bar.IsWrapped);
        Assert.Equal(24 + 4 + 30, bar.Bounds.Height);
        Assert.Equal(new Rect(0, 0, 300, 24), chips.Bounds);
        Assert.Equal(new Rect(250, 28, 200, 30), send.Bounds);
    }

    [AvaloniaFact]
    public void Exactly_enough_room_is_still_one_line()
    {
        var (bar, _, send) = Show(500);

        Assert.False(bar.IsWrapped);
        Assert.Equal(new Rect(300, 0, 200, 30), send.Bounds);
    }

    [AvaloniaFact]
    public void An_end_group_wider_than_the_bar_on_its_own_wraps_itself_and_keeps_its_last_at_the_end()
    {
        // The counts and Send, as in a narrow pane: 3 × 150 plus spacing is wider than the bar's 350.
        var send = new Border { Width = 150, Height = 30 };
        var counts = new WrapPanel
        {
            ItemSpacing = 2,
            LineSpacing = 4,
            ItemsAlignment = WrapPanelItemsAlignment.End,
            Children = { new Border { Width = 150, Height = 30 }, new Border { Width = 150, Height = 30 }, send },
        };
        var bar = new ControlBarPanel { RowSpacing = 4, Children = { new Border { Width = 300, Height = 24 }, counts } };
        UiText.Show(new Border { Width = 350, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = bar });

        Assert.True(bar.IsWrapped);
        Assert.Equal(24 + 4 + 30 + 4 + 30, bar.Bounds.Height);
        Assert.True(counts.Bounds.Right <= 350, $"The counts run to {counts.Bounds.Right}, past the bar's 350.");
        var at = send.TranslatePoint(default, bar)!.Value;
        Assert.Equal(new Point(350 - 150, 24 + 4 + 30 + 4), at);
    }

    [AvaloniaFact]
    public void Chips_wider_than_the_bar_on_their_own_wrap_rather_than_running_past_the_end()
    {
        // The model, effort and mode, as in a narrow pane: 3 × 150 plus spacing is wider than the bar's 350.
        var chips = new WrapPanel
        {
            ItemSpacing = 2,
            LineSpacing = 4,
            Children = { new Border { Width = 150, Height = 24 }, new Border { Width = 150, Height = 24 }, new Border { Width = 150, Height = 24 } },
        };
        var send = new Border { Width = 100, Height = 30 };
        var bar = new ControlBarPanel { RowSpacing = 4, Children = { chips, send } };
        UiText.Show(new Border { Width = 350, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = bar });

        Assert.True(bar.IsWrapped);
        Assert.True(chips.Bounds.Right <= 350, $"The chips run to {chips.Bounds.Right}, past the bar's 350.");
        Assert.Equal(24 + 4 + 24, chips.Bounds.Height);
        Assert.Equal(new Rect(250, 24 + 4 + 24 + 4, 100, 30), send.Bounds);
    }
}
