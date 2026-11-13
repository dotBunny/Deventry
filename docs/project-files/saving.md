---
title: Saving
parent: Project files
nav_order: 8
summary: "Deventry doesn't save for you."
---

# Saving

Deventry doesn't save for you. A file with unsaved edits has a dot on its tab until you save it, but its edits are kept safe meanwhile.

## Saving a file

- `Ctrl/Cmd+S` saves the file showing, while the Files tab has the focus. Anywhere else, `Ctrl/Cmd+S` stays the composer's **Stash** ([Earlier prompts, drafts and the stash](../composer/earlier-prompts-drafts-and-the-stash.md)).
- `Ctrl/Cmd+Alt+S` saves every open file (`Cmd+Option+S` on macOS).
- While the Files tab has the focus, the [command palette](../tabs/the-command-palette.md) has **Save the file** and **Save all files**.
- **Save**, at the end of the bar under the file tabs, saves the file too. It's dimmed until the file has unsaved edits, then filled in the accent color ([The bar's Save and Revert](#the-bars-save-and-revert)).

A save keeps the file's encoding, byte order mark and line endings. It writes to a temporary name and renames it over the file, so a crash can't leave half a file. A link is written through to its file, and on macOS and Linux the file keeps its permissions.

It writes only if the file is still what you opened or last saved. If something changed it since, it asks, as described under [When a file changes on disk](#when-a-file-changes-on-disk). If the file can't be written, a note over the editor says *Couldn't save* and why.

## The bar's Save and Revert

At the end of the bar under the file tabs, a file you can edit has **Revert** and **Save**, and a file Claude changed has **Revert Claude's changes** before them. They stay there while you edit, dimmed when there's nothing for them to do. In a narrow pane they move onto a line of their own under the rest of the bar. A file in a window of its own has them too.

- **Save** saves the file, as `Ctrl/Cmd+S` does. It's there for a file that's gone from disk too, to write it back.
- **Revert** throws your unsaved edits away and reads the file from disk again. It asks first, in a banner under the bar: *Discard your edits to Player.cpp?*, with **Revert** and **Cancel**. Changed your mind? `Ctrl/Cmd+Z` brings the edits back.
- **Revert Claude's changes** puts the whole file back as it was before Claude first changed it, in any of the project's tabs. It asks first in the same way. It changes only the editor's text, as the peek's **Revert** does for one change ([Claude's changes in the editor](claudes-changes-in-the-editor.md)): nothing is written until you save, and `Ctrl/Cmd+Z` takes it back. It's dimmed once the text is that again. A file Claude created doesn't have it, since there was no file before; Changed files' **Revert file** deletes one ([Reverting](../changes/reverting.md)).

## Unsaved edits are kept

A file's unsaved text is copied to Deventry's data folder a second after you stop typing ([Where Deventry keeps things](../privacy/where-deventry-keeps-things.md)). So quitting Deventry, a crash, or a restart into a new build asks nothing and loses nothing: the file comes back with its edits and its dot. Saving a file, or choosing **Don't save**, removes the copy, and copies no open file has used for 30 days are removed at launch.

## Closing files with unsaved edits

- **Closing a file** asks first, in place: *Save PlayerController.cpp?*, with **Save**, **Don't save** and **Cancel**.
- **Closing the Files tab, or its group,** with unsaved files asks once, naming them, with **Save all**, **Don't save** and **Cancel**.

## When a file changes on disk

An open file is watched. It's also looked at again when a tab of the project changes it with a tool, when one finishes a turn, and when Deventry comes to the front. The change may be Claude's, a `git checkout`, or your other editor.

- **With no unsaved edits**, the file is read again where it was. The cursor, selection, scroll position and folds stay on their lines as near as they can, and Undo takes the change back as one step.
- **With unsaved edits**, a banner over the editor, in the caution colors, says *Claude changed PlayerController.cpp in refactor auth while you were editing it.* When none of the project's tabs changed it, it says *PlayerController.cpp changed on disk while you were editing it.* It offers:
  - **Compare** shows the file on disk against your edits, side by side.
  - **Keep mine** keeps your edits. Saving then writes over the file on disk without asking again.
  - **Use theirs** reads the file again. Undo can take that back.
- **A file that's gone**, deleted or renamed, gets a banner saying so: *PlayerController.cpp is gone from disk. Save to write it back.* The text stays. **Keep editing** closes the banner, and saving writes the file back.

## Settings

None. Saving works the same everywhere.

## Shortcuts

| Key | Does |
|---|---|
| `Ctrl/Cmd+S` | Save the file showing (while a Files tab has the focus) |
| `Ctrl/Cmd+Alt+S` | Save every open file (macOS: `Cmd+Option+S`) |

## See also

- [Claude's changes in the editor](claudes-changes-in-the-editor.md): **Revert** there changes the text and leaves it unsaved.
