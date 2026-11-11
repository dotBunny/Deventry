---
title: The working line and running tasks
parent: The conversation
nav_order: 14
summary: "While Claude works, a line above the composer says so."
---

# The working line and running tasks

While Claude works, a line above the composer says so. After a turn, Claude Code can keep work going in the background, and a chip in the composer bar counts it.

## The working line

The line works like Claude Code's terminal spinner: *✻ Noodling… 42s · 3.1k tokens · Esc to stop*.

- **The glyph** twinkles through · ✢ ✳ ✶ ✻ ✽ and back, in the accent color.
- **The verb** is picked at random and changes every 8 seconds, never to the same one twice in a row.
- **After the verb** come how long the turn has run, the tokens it has used so far, and the Stop shortcut as you have it set.

### What Claude is doing

While one of Claude's own tools runs, the verb gives way to what the tool is doing, and comes back afterward:

- *Running dotnet test…*, *Reading TabView.axaml…*, *Editing App.cs…*, *Searching for TODO…*, *Fetching code.claude.com…*, *Using create_issue (github)…*
- When several run at once, the newest: *Reading b.cs and 2 more…*. A fan-out says *Running 3 agents…*.
- A subagent's own calls show on the [agent map](../plans/the-agent-map.md), not here.
- Hover the line to see the running calls in full, one per line.

### When it shows

From the start of a turn to its end. It hides while a permission prompt, question or plan is waiting for you, and the turn's time keeps counting meanwhile. It only animates while its tab is on screen, whether selected or one of the [tabs side by side](../tabs/split-panes.md). A tab working in the background keeps counting its turn without waking the window, and catches up when you look.

### Your own verbs

Claude Code's documented `spinnerVerbs` setting changes the list of verbs: `"append"` adds yours, and `"replace"` shows only yours (an empty list keeps the built-in ones). Deventry reads it from the tab's `.claude/settings.local.json`, then `.claude/settings.json`, then your `settings.json` in Claude Code's folder. The first file that sets it wins. See [Claude Code's settings documentation](https://code.claude.com/docs/en/settings).

## Running tasks

Claude Code keeps some work going after a turn ends, and counts it (*1 running task*). Deventry does too. This is what counts:

- a shell command started with `run_in_background`, or moved to the background later;
- a Monitor watch;
- a remote agent or a workflow.

A command or subagent running in the foreground is part of the turn, and Claude Code's own housekeeping isn't counted. Background subagents are counted by the **Agents** button, not here ([The agent map](../plans/the-agent-map.md)).

### The chip

While tasks run, the composer bar shows a pulsing dot and *1 running task* or *3 running tasks*, next to **Agents**. It is hidden when there are none. Click it for the list, oldest first. Each task has:

- an icon for its kind, its description or command, and its kind under it (*Shell command*, *Monitor*, *Remote agent*, *Workflow*, *Task*);
- how long it has been running, ticking each second while the list is open;
- **Stop**, after a confirmation. The task stays in the list until Claude Code says it ended, and Claude is told it was stopped.

Click a task to go to the card of the call that started it, opened. Hover it for the command in full.

When the tab's `claude` exits or restarts, its tasks go with it and the count clears.

### Elsewhere

After the turn is over, the tab's row in the sidebar counts the background work still going, subagents with it, and its tooltip tells them apart (*1 agent and 2 tasks still running*). With Settings → Conversation → Tab rows → **Show running subagents** on, subagents have a chip of their own on the row, such as **2 running**, so the count leaves them out. The tab's info card has a *Running tasks* row. The [Processes](../tabs/processes.md) page's **Stop** finds a process's task the same way.

## Settings

| Setting | Default | Where |
|---|---|---|
| **Show fun words** | On | Settings → Conversation → While Claude works |
| **Show what Claude is doing** | On | Settings → Conversation → While Claude works |
| **Motion** | Follow the system | Settings → Appearance |

With **Show fun words** off, the line says *Working…*. With **Show what Claude is doing** off, the verb stays and the tool's name doesn't replace it. **Motion** can stop the twinkling glyph and the pulsing dot ([Accessibility](../settings/accessibility.md)).

## Shortcuts

| Action | Shortcut |
|---|---|
| Stop Claude | `Esc` |

## See also

- [Sending and stopping](../composer/sending-and-stopping.md) for **Stop**.
- [The composer bar](../composer/the-composer-bar.md) for the other counts beside the chip.
- [Tool calls](tool-calls.md) for the full detail of each call.
