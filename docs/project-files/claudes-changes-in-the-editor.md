---
title: Claude's changes in the editor
parent: Project files
nav_order: 9
summary: "Claude's changes marked in the explorer and the editor's gutter, with a peek at what was there and Revert."
---

# Claude's changes in the editor

A Files tab marks what Claude changed where it is: a letter in the explorer, a bar in the editor's gutter beside each changed line, and the lines it replaced a click away.

## What the changes are against

A file's "before" is what it held before Claude first changed it in any of the project's open tabs. Where several tabs changed it, it's the earliest.

- With one tab, the editor and that tab's [Changed files](../changes/changed-files.md) show the same changes.
- The changes come from open tabs. Closing the last tab that changed a file takes its marks away.
- In a git repository, the explorer's **⋯** menu switches the whole Files tab from **Claude's changes** to **Against git HEAD**, as **Working tree vs HEAD** does in Changed files. That also shows changes you, commands and closed tabs made.
- A Files tab starts as Settings → Git → **New Files tabs show** says: **Claude's changes**, or **Changes against git HEAD**. From then on, each Files tab keeps its own choice.

The "before" is compared with the editor's text, unsaved edits and all, and worked out again 300 milliseconds after you stop typing. A file over 20,000 lines, or with too many changes to compare, shows none, and its tab's tip says *Too long to show what changed*.

## In the explorer

A changed file has **M** (or **A** for one Claude created) at its right, a folder holding one has a dot, and a reviewed file's letter is faint. **Show only changed files** in the explorer's **⋯** menu lists just those ([The explorer](the-explorer.md)).

## The gutter

Between the line numbers and the text:

- a green bar beside lines added;
- a bar in the accent color beside lines changed;
- a small red notch between two lines where lines were removed.

## The peek

Click a bar to open the old lines under the change, which pushes the lines below it down. Its header says who changed the file and when: *Changed by Claude in refactor auth · 14:05*. That's a link to the tab, at its last change to the file. The header also has:

- **Revert**, which puts the old lines back in the editor's text as one step. Undo takes it back. The file then has unsaved edits, and nothing is written until you [save](saving.md). It's off in a read-only file.
- Previous change and next change buttons.
- A close button.

Under the header are the lines the change replaced, in the removed color.

`F3` and `F4`, the keys for the conversation's previous and next message, go to the previous and next change in a file and open its peek.

To put back all of Claude's changes to a file at once, **Revert Claude's changes** at the end of the bar puts the whole file back as it was before Claude first changed it, in the editor, after asking ([Saving](saving.md#the-bars-save-and-revert)).

## Edit, Inline changes and Side by side

For a file with changes, the bar under the file tabs switches between three views:

- **Edit** is the editor, with its own **Wrap lines** ([Editing](editing.md#wrapping-long-lines)).
- **Inline changes** and **Side by side** are the [diff view](../changes/the-diff-view.md) inside the tab. They're read-only, with **Whole file**, **Wrap lines**, the colors, and **Revert** on each change. **Revert** there changes the editor's text, as the peek's does. Their **Wrap lines** is the diff's: the next diff you open, here or in the diff window, starts the way you left it.

A double-click on a line goes back to **Edit** at that line. Each open file keeps its own choice. A Markdown file's bar also has **Preview** and **Edit and preview** ([Markdown files](markdown-files.md)). In a pane too narrow for the bar's one row, **Whole file**, **Wrap lines** and **Reviewed** go onto a second.

## Reviewed

The bar has a **Reviewed** button, and so does the file tab's menu. Ticking it ticks the file in every one of the project's open tabs whose Changed files lists it, at its latest change, as ticking it there does ([Reviewed files](../changes/reviewed-files.md)). The rows' reviewed icon follows. It shows as ticked while every one of those tabs has the file ticked.

## Settings

| Setting | Default | Where |
|---|---|---|
| **Claude's changes** or **Against git HEAD** | **Claude's changes** | The explorer's **⋯** menu, in a repository |
| **New Files tabs show** | **Claude's changes** | Settings → **Git** |

## Shortcuts

| Key | Does |
|---|---|
| `F3` | Previous change, with its peek open |
| `F4` | Next change, with its peek open |

Both are the conversation's keys for the previous and next message, and rebindable in Settings → **Keyboard**.

## See also

- [Reverting](../changes/reverting.md) for putting a change back in a conversation's own diff view, which writes the file straight away.
