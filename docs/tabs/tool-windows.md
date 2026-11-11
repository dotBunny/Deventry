---
title: Tool windows
parent: Tabs and sessions
nav_order: 8
summary: "Changed files, the agents, tasks and more are tool windows, shown and hidden from a strip of icons at the window's right, as in JetBrains Rider."
redirect_from: /tabs/the-side-panel/
---

# Tool windows

Changed files, the agent map, tasks and the rest are tool windows, as in JetBrains Rider. A strip of icons runs down the window's right edge, one for each window, and a click on one shows its window beside the strip. There's one set for the whole window. They used to be the side panel's pages, which each tab had of its own.

{% include shot.html name="guide/tabs/tool-windows" alt="The tool windows at the right: Changed files above Scratch pad, each with its title, ⋮ and − on its header, and the strip of icons beside them, the two windows' icons filled in." %}

## What they show

They show the tab you're looking at: its changed files, its agents, its tasks. With a [Files tab](../project-files/opening-project-files.md) or a log in front, they show the session tab of that group you used last, and its name shows on the window's header, with its group's color. The same happens with tabs [side by side](split-panes.md), where the windows follow the pane with the focus.

| Window | When it has an icon | More |
|---|---|---|
| **Changed files** | Always. | [Changed files](../changes/changed-files.md) |
| **Agents** | Always. A dot on its icon shows while subagents are active. | [The agent map](../plans/the-agent-map.md) |
| **Project** | When the folder holds a project Deventry has tools for. A dot on its icon shows while a job runs. | [Project tools](../project-tools/using-project-tools.md) |
| **Tasks** | Once Claude keeps a task list or a plan. Its icon's tip says how far it's got, such as *2 of 5 done*. | [The Tasks page](../plans/the-tasks-page.md) |
| **MCP** | When the session has MCP servers. A warning dot shows on its icon when one needs attention. | [MCP servers](mcp-servers.md) |
| **Media** | Once the conversation has an image, a video or a sound. Its icon's tip counts yours and Claude's. | [Media](../conversation/media.md) |
| **Scratch pad** | Always. | [The scratch pad](../conversation/the-scratch-pad.md) |
| **Processes** | When the process monitor is on for the tab. | [Processes](processes.md) |
| **Games** | While games are on (Settings → Appearance → **Games with Joulene**), game or no game. | [Games with Joulene](../joulene/games-with-joulene.md) |
| **Find** | Once you keep a search with **Open in Find window**: a tab on its header for each search. It shows them whichever tab you're in. | [Finding in files](../project-files/finding-in-files.md#keep-the-results) |

Each window scrolls and wraps its text at the width shown, so a long file name or an error is never cut off.

## The strip

The icons are in three groups, top to bottom:

- **The right side's top**: Changed files, Agents, Project, Tasks, MCP and Media at first. Their windows show at the right of the window, the whole height.
- **Under the line**: Scratch pad, Processes and Games. Their windows show at the right too, under the top group's, sharing its height.
- **At the strip's foot**: Find. Its window shows along the bottom, under the conversation.

One window of each group shows at a time, so a Changed files window and a Scratch pad window can be on screen together, one over the other, with Find along the bottom. Point at an icon for its name, and what its window holds now.

## Showing and hiding them

- **Click** an icon to show its window, in place of the one of its group that was showing, with the keyboard in it. Its icon is filled in while it shows. Click it again to hide it.
- The **−** on a window's header hides it too.
- **Files** in the composer bar shows Changed files, and the bar's **Agents** button, process summary and task progress show theirs.
- The command palette's **Show changed files**, **Show the agent map**, **Show tasks**, **Show processes**, **Show MCP servers**, **Show media** and **Show scratch pad** show that window.

## Pinned or unpinned

A window's **⋮**, or a right-click on its icon, has **View mode**:

- **Dock Pinned**, as every window starts: it stays until you hide it.
- **Dock Unpinned**: it takes its room the same way, but hides once you click outside it, or press `Esc`. For a window you only look at for a moment.

## Moving and sizing them

- **Drag** an icon along the strip to change the order, or over the line or to the foot to move it to another group. A line marks where it would go; `Esc` cancels.
- **⋮** → **Move to** → **Right top**, **Right bottom** or **Bottom** does the same.
- **Drag** the gap beside the right side's windows to make them wider, the gap between the two of them to share the height another way, or the gap over the bottom window to make it taller. Double-click a gap to put it back. The right side is at least 260 pixels wide, 340 by default, and the conversation always keeps at least 360 pixels. When the window narrows, the right side gives way first, then the sidebar.

Each window's group, place and view mode, which windows show, and the sizes are remembered.

## See also

- [The composer bar](../composer/the-composer-bar.md), where the **Files** count is.
- [The sidebar](the-sidebar.md), the window's other panel.
- [Styles and themes](../settings/styles-and-themes.md), for the islands the window is made of.
