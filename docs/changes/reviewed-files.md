---
title: Reviewed files
parent: Reviewing changes
nav_order: 3
summary: "When Claude has changed a lot of files, it helps to keep track of the ones you've read."
---

# Reviewed files

When Claude has changed a lot of files, it helps to keep track of the ones you've read. Tick a file as reviewed, and Deventry remembers, draws it faintly, and tells you when you've been through them all.

## Ticking a file

- In [Changed files](changed-files.md), tick the box at the start of the file's row. Its tip reads *Reviewed. Unticks when Claude changes the file again.*
- To tick every file in a folder at once, tick the folder's box in Changed files' tree ([The tree](changed-files.md#the-tree)). Ticked again, it unticks them all. While only some of its files are ticked, the box is partly ticked, and a folder whose files are all ticked is drawn faintly too.
- In the [diff view](the-diff-view.md), click **Reviewed**. It ticks the file and closes the window. The button shows a check when the file is already ticked.

A file has one reviewed state, so ticking it in one place ticks it in the other.

The diff view marks the version it showed you. If Claude changed the file while the window was open, the file stays unreviewed, since you haven't seen the change.

The summary at the top of Changed files counts them: *"5 files changed · 2 reviewed"*.

## What unticks a file

A file stays reviewed until Claude changes it again, with a file edit. Changes you make, that a command makes or that another tab's Claude makes don't untick it. So a file only you changed, seen with **Working tree vs HEAD** on, stays ticked until Claude changes it.

## The reviewed icon

Once every file Claude changed in the tab is ticked, the tab's row in the sidebar shows the reviewed icon in place of its mark: a green disc with a check cut out of it. Its tip reads *All changed files reviewed*, and names the mark it covers, if the tab has one. Files only you or commands changed don't count, since they don't untick either.

The icon isn't a mark, so it isn't in the tab's menu, and the tab's own mark comes back as soon as a file isn't reviewed, whether Claude changes one or you untick one. See [Names, marks and pins](../tabs/names-marks-and-pins.md).

Joulene on the composer notices too: she stamps the edge of the box, which leaves a green check mark ([Joulene on the composer](../joulene/joulene-on-the-composer.md)).

## What's kept

- Ticks are saved with the tab, so they survive closing and restoring it.
- In a tab that syncs, they're in the session's record too, so the session opened on another machine has them, for files it can find in its own copy of the folder. See [History and syncing](../history/index.md).
- **Duplicate tab** keeps them in the copy.
- **Start a new session** clears them.

## In a Files tab

A project's Files tab has a **Reviewed** button for a file Claude changed, and the file tab's menu has **Reviewed**. It ticks the file in every one of the project's open tabs that lists it, and the rows' reviewed icon follows. See [Claude's changes in the editor](../project-files/claudes-changes-in-the-editor.md#reviewed).

## See also

- [Reverting](reverting.md) puts a change back instead.
- [Changes per turn](changes-per-turn.md) has no ticks: it only shows a turn's changes.
