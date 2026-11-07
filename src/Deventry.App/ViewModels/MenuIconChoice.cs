using Deventry.Core.ProjectTools;

namespace Deventry.App.ViewModels;

/// <summary>
/// One choice of <b>Icon</b> in the action and link dialogs (DESIGN.md §18, "deventry.json"): one of
/// <see cref="MenuIcons"/>, or none, for the icon the entry would have anyway.
/// </summary>
/// <param name="Name">What <c>"icon"</c> is written as; null leaves it out.</param>
/// <param name="Shown">The icon drawn beside the label: for no choice, the one the entry has anyway.</param>
public sealed record MenuIconChoice(string? Name, string Label, string Shown)
{
    public string IconKey => MenuIconKeys.For(Shown);

    /// <summary>What a screen reader reads for it.</summary>
    public override string ToString() => Label;

    /// <summary>No choice, then every icon by name.</summary>
    /// <param name="usual">The icon the entry has without one, and how to say so: "Its mode's", "A link's".</param>
    public static IReadOnlyList<MenuIconChoice> All(string usual, string usualLabel) =>
        [new(null, usualLabel, usual), .. MenuIcons.Names.Select(name => new MenuIconChoice(name, name, name))];
}
