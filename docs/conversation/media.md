---
title: Media
parent: The conversation
nav_order: 12
summary: "Every image, video and sound in a tab's conversation, yours and Claude's, in one window."
---

# Media

{% include unreleased.html %}

The **Media** window gathers every image, video and sound in a tab's conversation into a gallery, so a screenshot from an hour ago, or the chart a script made, is a click away instead of a scroll back. It has two sections: **User**, for what you brought, and **Agent**, for what Claude saw or made.

{% include shot.html name="guide/conversation/media" alt="The Media tool window: User, with 2 tiles, an attached screenshot of a game and a video, wave-clear.mp4, shown by its icon; and Agent, with 3 tiles, a sound, burst.wav, shown by its icon, and two frame-time charts Claude read, one flat and one with four spikes." %}

## Showing it

**Media** is a [tool window](../tabs/tool-windows.md), with its icon on the strip at the window's right, after **MCP**, once the tab's conversation has an image, a video or a sound. Point at the icon to see how many each section holds (*Media · User 2 · Agent 5*), and click it to show the window. The command palette's **Show media** shows it too.

Like the other tool windows, it shows the tab you're looking at.

## What's in it

**User** has:

- the images you attach to a message ([Attachments and large pastes](../composer/attachments-and-large-pastes.md)), each called *Attached image*;
- the image, video and sound files your messages name: with `@` (`@shots/bug.png`), as a path (`shots/bug.png`, `C:\Captures\run.mp4`, `~/Desktop/clip.mov`) or just a name (`bug.png`), in quotes or backticks or not.

**Agent** has what Claude, or any of its subagents, saw or made:

- the images a tool handed Claude ([Images](images.md)): an image it read, a PDF's pages, a notebook's figures, a screenshot from a browser tool;
- an image, video or sound file it read, wrote or edited, such as an SVG it wrote;
- the files a command it ran names, once the command has finished, such as the clip in `ffmpeg -i in.mp4 out.gif` or the chart in `python plot.py --out=chart.png`;
- the files its replies name, such as *The chart is in `out/chart.png`.*

A file counts once it's there: a name that doesn't match a file isn't shown. Files are looked for the way a file Claude names in a reply is ([Files and links in replies](files-and-links-in-replies.md)): in the tab's folder and its extra folders, among the files the session worked on, and in the project. Deventry never looks for a file on another machine (a network path), or a web address.

It knows these kinds of file by their names:

- **Images:** PNG, JPEG, GIF, WebP and BMP, which Deventry shows itself, and SVG, TIFF, ICO, HEIC, AVIF, PSD, TGA and EXR, which open in another app.
- **Video:** MP4, M4V, MOV, WebM, MKV, AVI, WMV, MPEG and OGV.
- **Sound:** MP3, WAV, OGG, FLAC, M4A, AAC, Opus and AIFF.

## The tiles

Each section shows its tiles newest first, with what one message brought kept in its own order. A tile shows the image, or for a video, a sound, or an image Deventry doesn't show itself, an icon for its kind, with the name underneath. Point at a tile to see its name, where it came from and when (*Read · 14:02*, *Named in a reply · 14:05*), and where its file is.

A file has one tile in its section, wherever it was seen last: when Claude makes the same chart again, its tile moves to the front, showing the chart as it is now. The same screenshot twice shows once.

To fold a section away, click its header (its name and count); click again to open it.

## Opening one

Click a tile to open it:

- **An image** opens in Deventry's image window, with zoom, **Copy image** and **Save as…**, as described in [Images](images.md). An image Claude read shows what Claude saw.
- **A video or a sound**, or an image Deventry doesn't show itself, opens in the app your OS uses for it. Deventry doesn't play video or sound itself.

Right-click a tile for more:

- **Open**, as a click does.
- **Open in the OS's app**, for a file.
- **Show in conversation** goes to the message, tool call or reply it came from.
- **Reveal in file manager** and **Copy path**, for a file.

## What it keeps

The gallery is built from the conversation, so a restored tab has its gallery back, and going back to an earlier message with [Rewind and branch](rewind-and-branch.md) leaves out what came after it. It doesn't outlive the tab.

## See also

- [Images](images.md), for the images under tool calls and in replies, and the image window.
- [Attachments and large pastes](../composer/attachments-and-large-pastes.md), for attaching images to a message.
- [Tool windows](../tabs/tool-windows.md), for showing, moving and pinning the window.
