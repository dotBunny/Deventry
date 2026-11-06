using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.Views;

namespace Deventry.App.UiTests;

/// <summary>The command palette, the new tab picker and prompt recall, by keyboard (DESIGN.md §4, §5).</summary>
public class CommandPaletteUiTests
{
    private static readonly RawInputModifiers Primary = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        UiText.Settle(window);
    }

    [AvaloniaFact]
    public async Task The_palette_opens_from_its_shortcut_filters_as_you_type_and_Enter_runs_the_choice()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell }, 1100, 800);

        Press(window, PhysicalKey.P, Primary | RawInputModifiers.Shift);
        Assert.True(h.Shell.IsPaletteOpen);
        var box = window.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "QueryBox");
        Assert.True(box.IsFocused);

        // "find in" is Find in files… too (DESIGN.md §18, "Finding in files").
        window.KeyTextInput("find in the");
        UiText.Settle(window);
        Assert.Equal("Find in the conversation", h.Shell.Palette!.Selected!.Label);
        Press(window, PhysicalKey.Enter);

        Assert.False(h.Shell.IsPaletteOpen);
        Assert.True(tab.Find.IsOpen);

        Press(window, PhysicalKey.P, Primary | RawInputModifiers.Shift);
        Press(window, PhysicalKey.Escape);
        Assert.False(h.Shell.IsPaletteOpen);
    }

    [AvaloniaFact]
    public async Task Find_a_tab_opens_from_its_shortcut_and_Enter_goes_to_the_tab_found_its_row_picked_out()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var first = await h.OpenTabAsync();
        first.State.UserName = "Fix login bug";
        var second = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell }, 1100, 800);
        Assert.Same(second, h.Shell.SelectedTab);
        // The sidebar's magnifier opens it too (DESIGN.md §4, "Find a tab").
        Assert.NotNull(window.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Name == "FindTabButton" && b.IsEffectivelyVisible));

        Press(window, PhysicalKey.K, Primary);
        Assert.True(h.Shell.IsTabSearchOpen);
        var box = window.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "QueryBox");
        Assert.True(box.IsFocused);

        window.KeyTextInput("login");
        UiText.Settle(window);
        Assert.Equal("Fix login bug", h.Shell.TabSearch!.Selected!.Name);
        Press(window, PhysicalKey.Enter);

        Assert.False(h.Shell.IsTabSearchOpen);
        Assert.Same(first, h.Shell.SelectedTab);
        var glow = window.GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("attentionglow") && ReferenceEquals(b.DataContext, first) && b.IsEffectivelyVisible);
        Assert.Contains("attention", glow.Classes);

        Press(window, PhysicalKey.K, Primary);
        Press(window, PhysicalKey.Escape);
        Assert.False(h.Shell.IsTabSearchOpen);
    }

    [AvaloniaFact]
    public async Task The_new_tab_picker_opens_from_its_shortcut_with_its_search_box_taking_the_keyboard()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell }, 1100, 800);

        Press(window, PhysicalKey.T, Primary);
        Assert.True(h.Shell.IsPickerOpen);
        var box = window.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "SearchBox" && b.IsEffectivelyVisible);
        Assert.True(box.IsFocused);

        window.KeyTextInput("star");
        UiText.Settle(window);
        Assert.Equal("star", h.Shell.Picker!.Search);
    }

    [AvaloniaFact]
    public async Task Up_in_an_empty_composer_brings_back_the_last_prompt()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell }, 1100, 800);
        tab.ComposerText = "run the tests";
        await tab.SendCommand.ExecuteAsync(null);
        h.Transport.EmitTurn();
        await UiText.SettleUntilAsync(window, () => tab.Status == ViewModels.TabStatus.Idle && tab.IsSettled, "the turn");
        var composer = window.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "Composer");
        composer.Focus();
        UiText.Settle(window);

        Press(window, PhysicalKey.ArrowUp);
        Assert.Equal("run the tests", composer.Text);
        Assert.Equal(composer.Text!.Length, composer.CaretIndex);

        Press(window, PhysicalKey.ArrowDown);
        Assert.Equal("", composer.Text ?? "");
    }
}
