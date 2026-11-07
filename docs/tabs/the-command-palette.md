---
title: The command palette
parent: Tabs and sessions
nav_order: 10
summary: "The command palette is a box over the window that does anything Deventry has a command for, when you type a few letters of it. Find a tab finds an open tab by pieces of its name."
---

# The command palette

The command palette is a box over the window that does anything Deventry has a command for, when you type a few letters of it. It also jumps to a tab, opens a folder and goes to a Settings page.

{% include shot.html name="guide/tabs/command-palette" alt="The command palette open over the window, with a list of commands and their shortcuts." %}

## Using it

1. Press `Ctrl/Cmd+Shift+P`.
2. Type a few letters of what you want. The box says *Type a command, a tab, a folder or a setting*.
3. Choose with `Up` and `Down`, and press `Enter` to run it. Double-clicking an entry runs it too.
4. Press `Esc`, click outside it, or press the shortcut again to close it without running anything.

While the palette is open, the window's other shortcuts are left to it.

## What it lists

Before you type, the list has five kinds of entry, in this order. The kind shows beside each entry: **Command**, **Tab**, **Folder**, **Settings** or **Help**.

- **Commands.** Each shows its shortcut, so the palette teaches them.
- **Tabs**, by name with their folder. Picking one selects it.
- **Folders**: your favorites, then recent folders. Picking one opens a new tab there.
- **Settings** pages. Picking one opens Settings there.
- **Help**: **User Guide** opens this guide, **Ask on Discord** opens dotBunny's Discord, where you can ask questions, and **Report an issue** opens a new issue on GitHub with the versions filled in, all in your browser. **About Deventry** opens [its window](../settings/about.md). See [Reporting an issue](../troubleshooting/reporting-an-issue.md).

### The commands

For the whole window:

- **New tab**, **Open from History…**, **Settings**
- **Find a tab…**, while a tab is open. See [Finding a tab](#finding-a-tab).
- **Collapse the sidebar** or **Expand the sidebar**
- **Zoom in**, **Zoom out**, and **Reset zoom** while zoomed
- **Go to the next tab waiting for you**, when another tab is waiting
- **Go to file…**, which finds a file by its name in the project's Files tab. See [Finding a file by name](../project-files/finding-a-file-by-name.md).

While a project's Files tab has the focus:

- **Save the file**, while it has unsaved edits, and **Save all files**, while any file does
- **Close the file**, **Go to line…** and **Open the file in a window**, while a file is showing ([In a window of its own](../project-files/file-tabs.md#in-a-window-of-its-own))
- **Preview the file**, **Edit and preview the file** and **Edit the file**, the ones not showing, while a Markdown file is. See [Markdown files](../project-files/markdown-files.md).
- **Show only changed files** (or **Show every file in the explorer**) and **Hide the explorer** (or **Show the explorer**)
- **Close this pane** and **Close the other panes**, with tabs [side by side](split-panes.md)
- **Close project files**

For the selected tab:

- **Find in the conversation**
- **Go to the first message** and **Go to the latest message**, which jump through the conversation. See [Finding your way](../conversation/finding-your-way.md).
- **Close this pane** and **Close the other panes**, with tabs [side by side](split-panes.md)
- **Show the thread with its sub-threads**, on a thread or one of its sub-threads
- **Stop Claude**, while Claude works
- The project's main action, named for it (such as *Build*), with the project's name beside it
- **Stash the message**, while something's typed in the composer
- **Show changed files**, **Show the agent map**, **Show tasks**, **Show processes**, **Show MCP servers** (when the session has some) and **Show scratch pad**
- **Open project files**, which opens the project's Files tab. See [Opening project files](../project-files/opening-project-files.md).
- **Play tic-tac-toe with Joulene**, **Play Four in a Row with Joulene**, **Play hangman with Joulene**, **Play Minesweeper with Joulene** and **Play Pipeline with Joulene**, unless the games are turned off. See [Games with Joulene](../joulene/games-with-joulene.md).
- **Tab settings…**, **Duplicate tab** and **Export conversation…**
- **Collapse tab** (or **Expand tab**), while the tab has rows under it. See [Collapsing a tab](the-sidebar.md#collapsing-a-tab).
- **New tab in a worktree**, for a folder in a git repository
- The thread commands that apply: **Make this tab a thread**, **New sub-thread**, **New sub-thread in a worktree**, **Ask before sending to sub-threads** (or **Stop asking before sending to sub-threads**), **Stop being a thread**, **Assign to thread:** and a thread's name, **Go to thread** and **Detach from thread**
- **Restart Claude Code**, when it has stopped
- **Close tab**

## Filtering

An entry matches when the letters you typed appear in its name in order, ignoring case. A match at the start of the name or of a word, or with the letters together, comes first. An entry also matches by its kind, so typing `tab` finds tabs, a little lower down.

## Finding a tab

With many tabs open, **Find a tab** finds one by a few pieces of text and takes you to it.

1. Press `Ctrl/Cmd+K`, click the magnifier at the top of the sidebar (beside **History**), or choose **Find a tab…** in the palette.
2. Type part of the tab's name, its project's name, its folder or its git branch. Every word you type has to be there somewhere, in any order and case: `login` finds *Fix login bug*, and `orbit auth` finds *Refactor auth* in the *orbit* project. Tabs whose name has the words come first, then those whose project has them, then the rest. With nothing typed, it lists every open tab, in the sidebar's order.
3. Choose with `Up` and `Down` and press `Enter`, or click a tab.

Each tab in the list shows its status, its name, and its project with its group's color, and its branch when it has one.

Deventry selects the tab, opens its group (and its thread) if they were collapsed, scrolls the sidebar to its row and pulses the row softly for a couple of seconds so you see where it is. With [motion reduced](../settings/accessibility.md) the row is highlighted without moving, and the highlight fades. `Esc`, a click outside or the shortcut again closes the box without going anywhere.

It finds only the tabs that are open; for past sessions, use [History](../history/history.md).

## Going to the next tab waiting for you

`Ctrl/Cmd+J` selects the next tab, in sidebar order after the selected one and going round, that's waiting for you: a permission prompt, a question or a plan. Nothing happens when no other tab is waiting. The palette has the same command, **Go to the next tab waiting for you**, while one is.

## Shortcuts

| Shortcut | What it does |
|---|---|
| `Ctrl/Cmd+Shift+P` | Open or close the palette |
| `Up` and `Down` | Choose an entry |
| `Enter` | Run the chosen entry |
| `Esc` | Close the palette |
| `Ctrl/Cmd+J` | Go to the next tab waiting for you |
| `Ctrl/Cmd+K` | Find a tab |

## See also

- [Keyboard shortcuts](../reference/keyboard-shortcuts.md), which lists every shortcut.
- [The tab's menu](the-tabs-menu.md), for what a right-click offers.
