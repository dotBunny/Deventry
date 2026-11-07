using Deventry.Core.ProjectTools;

namespace Deventry.App.ViewModels;

/// <summary>
/// The shape the app draws for each of <see cref="MenuIcons"/>: the key of its geometry in <c>App.axaml</c>, drawn as a
/// menu's icon is (<c>Path.menuicon</c>, DESIGN.md §18, "Project tools").
/// </summary>
public static class MenuIconKeys
{
    public static string For(string? icon) => icon switch
    {
        MenuIcons.Play => "IconSettingsActions",
        MenuIcons.Build => "IconMenuBuild",
        MenuIcons.Test => "IconMenuTest",
        MenuIcons.Terminal => "IconToolBash",
        MenuIcons.File => "IconToolRead",
        MenuIcons.Files => "IconFiles",
        MenuIcons.Folder => "IconToolFolder",
        MenuIcons.Log => "IconMenuLog",
        MenuIcons.Link => "IconOpenWindow",
        MenuIcons.Web => "IconToolWeb",
        MenuIcons.Docs => "IconGuide",
        MenuIcons.Refresh => "IconRestart",
        MenuIcons.Upload => "IconMenuUpload",
        MenuIcons.Download => "IconMenuDownload",
        MenuIcons.Package => "IconProjectUnity",
        MenuIcons.Trash => "IconMenuTrash",
        MenuIcons.Stop => "IconMenuStop",
        MenuIcons.Settings => "IconSettings",
        MenuIcons.Bug => "IconBug",
        MenuIcons.Branch => "IconBranch",
        MenuIcons.Check => "IconCheck",
        _ => "IconToolDefault",
    };
}
