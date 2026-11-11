using Deventry.App.Conversation;
using Deventry.App.Services;
using Deventry.Core.Protocol;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>
/// The tab's side of the Media window (DESIGN.md §18, "Media"): the gallery its conversation fills, and what a tile's click
/// and its menu do.
/// </summary>
public sealed partial class TabViewModel
{
    /// <summary>The images, video and sound in the conversation, the user's and the agent's.</summary>
    public MediaGallery Media { get; } = new();

    /// <summary>
    /// Where the gallery looks for the files the conversation names: where a reply's inline code is looked for, which
    /// can't reach another machine.
    /// </summary>
    private void SetUpMedia()
    {
        Media.Time = _services.Time;
        Media.Dispatcher = _services.Dispatcher;
        Media.FindFile = reference => ReplyFiles.FindAsync(reference, clicked: false);
        Media.ThumbnailOf = MediaThumbnails.FromBytes;
        Media.ThumbnailOfFile = MediaThumbnails.FromFile;
        Media.ShownPath = ShownPath;
    }

    /// <summary>
    /// A tile's click: an image in the image window, as an image under a tool's card opens (DESIGN.md §18, "Opening
    /// images"); a video, a sound, or an image the window can't show in the app the OS uses for it.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task OpenMediaAsync(MediaEntry? entry)
    {
        if (entry?.Image is { } image)
        {
            _shell.ShowImage(image);
            return;
        }
        if (entry?.Path is not { } path)
        {
            return;
        }
        var shown = ShownPath(path);
        if (entry.IsImage && ViewerImages.HasExtension(path) && await Task.Run(() => ShellViewModel.ImageFile(path, shown)) is { } file)
        {
            _shell.ShowImage(file);
            return;
        }
        await OpenMediaInSystemAsync(entry);
    }

    /// <summary><b>Open in the OS's app</b>: a file's, whatever it is.</summary>
    [RelayCommand]
    private Task OpenMediaInSystemAsync(MediaEntry? entry) =>
        entry?.Path is { } path && File.Exists(path) ? _services.Platform.OpenFileAsync(path) : Task.CompletedTask;

    /// <summary><b>Show in conversation</b>: the message, call or reply it came from, in a subagent's group too.</summary>
    [RelayCommand]
    private void ShowMediaInConversation(MediaEntry? entry)
    {
        switch (entry?.Item)
        {
            case ToolUseItem tool:
                ShowToolUse(tool.ToolUseId);
                break;
            case { } item:
                ScrollTo(item);
                break;
        }
    }

    [RelayCommand]
    private Task CopyMediaPathAsync(MediaEntry? entry) =>
        entry?.Path is { } path ? _services.Platform.SetClipboardTextAsync(path) : Task.CompletedTask;

    /// <summary><b>Reveal in file manager</b>: the folder its file is in.</summary>
    [RelayCommand]
    private Task RevealMediaAsync(MediaEntry? entry) =>
        entry?.Path is { } path && Path.GetDirectoryName(path) is { } folder && Directory.Exists(folder)
            ? _services.Platform.RevealFolderAsync(folder)
            : Task.CompletedTask;
}
