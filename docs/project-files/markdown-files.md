---
title: Markdown files
parent: Project files
nav_order: 7
summary: "Read a Markdown file rendered, in the editor's place or beside it."
---

# Markdown files

A Markdown file, such as a README or a plan Claude wrote, can be read rendered as well as edited as text: its headings, lists, tables, code blocks and images, in the editor's place or beside it.

{% include shot.html name="guide/project-files/markdown-preview" alt="A README open in a Files tab with Edit and preview picked: its text in the editor on the left, and on the right the same file rendered, with its headings, a numbered list, a table of controls and a code block." %}

## Edit, Preview, and Edit and preview

A file whose extension is `.md`, `.markdown` or another of Markdown's (the chip says *Markdown*) always has the bar under the file tabs, with three choices:

- **Edit** is the [editor](editing.md), as for any file, with **Wrap lines** after the choices ([Wrapping long lines](editing.md#wrapping-long-lines)), as in **Edit and preview**.
- **Preview** shows the file rendered, in the editor's place.
- **Edit and preview** puts the editor on the left and the preview on the right. Drag the line between them to give either side more room; double-click it to share the width evenly again.

When Claude has changed the file, **Inline changes** and **Side by side** follow as a second part of the bar ([Claude's changes in the editor](claudes-changes-in-the-editor.md)). The five are one choice: picking one leaves whichever was showing.

A Markdown file opens in **Edit**. Each open file keeps its own choice, also after Deventry restarts.

The [command palette](../tabs/the-command-palette.md) has **Preview the file**, **Edit and preview the file** and **Edit the file** while a Markdown file shows.

## What the preview shows

The preview shows the editor's text, unsaved edits and all, as Claude's replies show Markdown: the same fonts, headings, lists and task lists, tables, quotes, and code blocks colored as in a reply, with **Wrap** and **Copy**. You can select and copy its text, as in a reply.

- As you type, the preview catches up a moment later.
- A paragraph whose lines are wrapped in the file reads as one paragraph, as in VS Code and on GitHub. End a line with two spaces or a `\` to break it there.
- Front matter, the block of settings between `---` lines at the top of a page for a site like this one, isn't shown.
- HTML in the file isn't shown, and Mermaid diagrams and math show as code.
- A file over 1 MB isn't previewed: it would take Deventry several seconds to draw. The preview says so.

## Scrolling together

In **Edit and preview**, scrolling either side scrolls the other to the same place, so the line at the top of the editor is at the top of the preview. Switching between **Edit** and **Preview** keeps your place too.

## Finding in the preview

Press `Ctrl/Cmd+F` in **Preview**, or in **Edit and preview** with the keyboard in the preview, and a find box shows over it, as in the editor. Type what to look for: every match is marked, the one it's on outlined, and it says where it is, such as *3 of 12*. **Aa** matches case, **W** whole words, and **.\*** takes a regular expression. `Enter` goes to the next match and `Shift+Enter` to the one before, scrolling it into view; `Esc` closes the box. With the keyboard in the editor, `Ctrl/Cmd+F` finds in the editor instead.

A match is within a line of the preview: it doesn't run on from one paragraph, or one line of a code block, into the next.

## Links and images

- **A link to a heading** in the file, such as `[Saving](#saving)`, scrolls the preview to it.
- **A web link** opens in your browser. A link to an image on the web opens in Deventry's image viewer, as Settings → **Files and diffs** → **Open images in** says.
- **A link to a file** is read from the Markdown file's own folder, as GitHub reads it. It opens as a file named in Claude's replies does ([Opening files from the conversation](opening-files-from-the-conversation.md)): text in a file tab here, at the line if the link names one (`#L42`); an image in the image viewer; a folder in the explorer; a program in its folder, without running it. If the file isn't there, the preview says so over the file: *Couldn't find specs/shots.md.*
- **Images** are read from the Markdown file's own folder too. One in the project's folder shows at once. One from the web, or from outside the project's folder, waits for you to click **Load image** or **Show image**, as in Claude's replies, since a file can be written to send data out through an image's address ([Images](../conversation/images.md)). Click an image to see it full size.

## See also

- [Editing](editing.md) for the editor's keys, which work in **Edit and preview**'s editor.
