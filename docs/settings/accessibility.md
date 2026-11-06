---
title: Accessibility
parent: Settings
nav_order: 5
summary: "Deventry works with screen readers, scales to a size you can read, and can hold still."
---

# Accessibility

Deventry works with screen readers, scales to a size you can read, and can hold still. This page covers what a screen reader hears, **Zoom** and **Motion**, and the one place a screen reader can't read yet.

## Screen readers

- **Every control has a name** a screen reader can say: a button with text says its text, and a button with only an icon has a name of its own, such as "Copy version details".
- **What a notification would say is read out too**, whether or not the notification is shown, so you don't need notifications on to hear it. A prompt that's waiting ("work: Allow this command? npm test"), a turn that finished ("work: Claude finished.") and an error are announced. They go to a polite live region, so they wait for what the screen reader is saying and never cut in on it.
- **Some actions announce themselves.** Each zoom change says "Zoom 125%", and adding to the scratch pad says "Added to the scratch pad."
- **The games** have a name on every square, column and letter ("Top left, empty", "Column 3, 2 of 6 filled", "Letter E"), and Deventry's moves and every result are read out ("Deventry took the middle.", "You win!").
- **Joulene on the composer** is decoration, and screen readers pass her by. What she says in her bubbles is said elsewhere too, by the badge and the announcements for waiting tabs.

### What the editor can't do yet

The text in a [Project Files](../project-files/index.md) editor can't be read by a screen reader yet. The editor Deventry uses has no support for it, and neither does the toolkit under it. The explorer, the file tabs, the chip and the banners around the text do have names. Until the editor can be read, every file's menu has **Open in the OS's app**, which opens the file in an editor that can be.

## Zoom

Zoom makes everything in the main window bigger or smaller, in steps of 80%, 90%, 100%, 110%, 125%, 150%, 175% and 200%. Menus, popups and tooltips, and the other windows, keep their size. The image window zooms its picture on its own: see [Images](../conversation/images.md#zooming-in).

To change it, any of these:

- Press `Ctrl/Cmd +` to zoom in, `Ctrl/Cmd −` to zoom out, or `Ctrl/Cmd+0` to go back to 100%. The number pad's `+`, `−` and `0` work too.
- Run **Zoom in**, **Zoom out** or **Reset zoom** from the [command palette](../tabs/the-command-palette.md). **Reset zoom** is there only while you're zoomed.
- Pick a size in Settings → Appearance → **Zoom**.

Each machine keeps its own zoom, since it suits that machine's screen, so it doesn't sync.

## Motion

Settings → Appearance → **Motion** says whether Deventry's animations run. It has three choices:

- **Follow the system** (the default) reduces motion when your computer asks for it, or when Claude Code's own `prefersReducedMotion` setting does, so a choice you made for Claude Code in a terminal holds here too. The computer's setting is *Show animations in Windows* on Windows, *Reduce motion* on macOS and the animations switch in GNOME on Linux. Deventry reads them at launch and when its window comes back to the front, at most once a minute.
- **Reduce motion** always reduces it.
- **Don't reduce** always animates.

Reduced, these stop moving:

- The busy dots on a tab stop pulsing.
- The working line's glyph holds still. Its words still change.
- The taskbar or Dock icon shows the first frame of its animation instead of moving, so it still says what your tabs are doing.
- A tab found with [Find a tab](../tabs/the-command-palette.md#finding-a-tab) is highlighted without pulsing, and the highlight fades.
- Joulene on the composer stands still, in the first pose of what she'd be doing. She doesn't react to clicks, events or the pointer, and can't be carried.

## Settings

| Setting | Where | Default | Syncs |
|---|---|---|---|
| **Zoom** | Settings → Appearance | 100% | No |
| **Motion** | Settings → Appearance | Follow the system | Yes |

## Shortcuts

| Action | Shortcut |
|---|---|
| Zoom in | `Ctrl/Cmd +` |
| Zoom out | `Ctrl/Cmd −` |
| Back to 100% | `Ctrl/Cmd+0` |

You can change all three in Settings → [Keyboard](keyboard.md).

## See also

- [Styles and themes](styles-and-themes.md) for the theme, density and fonts.
- [The Dock and taskbar icon](../away/the-dock-and-taskbar-icon.md) for the icon's animation.
