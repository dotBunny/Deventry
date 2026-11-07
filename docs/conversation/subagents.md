---
title: Subagents
parent: The conversation
nav_order: 4
summary: "Claude can hand part of its work to a subagent: a helper that runs inside the same session with its own instructions and its own tool calls, and hands a report back."
---

# Subagents

Claude can hand part of its work to a subagent: a helper that runs inside the same session with its own instructions and its own tool calls, and hands a report back. Claude Code decides when to start one ([Claude Code's subagents documentation](https://code.claude.com/docs/en/sub-agents) explains them). This page is about how they look in the conversation. The [agent map](../plans/the-agent-map.md) shows all of a tab's subagents at once.

## A subagent's card

A subagent is a card of its own, in the quiet neutral color. Its line shows a status dot (running, done or failed), the subagent's type (*Explore*, *general-purpose* and so on), a short description of its task and the time. Under it is its result line: the first line of its report once it has finished, or how it ended.

Click the line to see what the subagent did: the tab shows its work in place of the conversation ([Subagents under their tab](#subagents-under-their-tab)). The agent map's details show the whole prompt and the whole report, each with **Copy**.

With Settings → Conversation → **Show subagents' work** set to **In the conversation**, clicking the line opens the card instead. Inside is the start of the prompt Claude gave the subagent, then everything that subagent did: its own **Tool calls** cards, its replies and its thinking, as the main conversation shows them. A subagent can start subagents of its own, which nest inside it.

## Subagents that run in the background

A subagent Claude doesn't wait for runs in the background. Its card says it is running until the subagent finishes, and the **Agents** button in the composer bar counts it. The tab's row in the sidebar keeps showing that work is going on after the turn is over, and its count of tasks still running has the subagents in it. With Settings → Conversation → Tab rows → **Show running subagents** on, a chip at the right of its second line says how many subagents are still running instead, such as **2 running**, during the turn too. Click it to see them: their rows come out from under a collapsed tab, or, with their work shown in the conversation, the Agents window opens.

To stop a subagent, use the **■** on its row in [the agent map](../plans/the-agent-map.md). The running tasks chip leaves subagents out ([The working line and running tasks](the-working-line-and-running-tasks.md)).

## Subagents under their tab

Each subagent Claude starts has a row of its own under its tab in the sidebar, the way a thread's sub-threads sit under it. To open subagents' cards in the conversation instead, with no rows, set Settings → Conversation → **Show subagents' work** to **In the conversation**. It applies at once, to every tab.

A subagent's row has the agents icon and its task, how long it has run, and under them its type and what it's doing, in the accent color, or how it ended, in green once it's done. A subagent another one started sits under it, further in. Its status mark is the one [the agent map](../plans/the-agent-map.md) uses: a pulsing dot while it runs, `!` while it waits on you, and a dot, ✕ or ■ once it's done, failed or stopped.

To see what a subagent did:

1. Click its row. The tab shows the subagent's work in place of the conversation: its own tool calls, replies and thinking, then what it handed back to Claude.
2. Over it are its task, its type, its model, how long it ran, and **The prompt it was given**, which opens the prompt Claude wrote for it.
3. To go back, click **← ‹the tab's name›** over the work, **Back to the conversation** under it, the tab's own row, or press `Esc`. The conversation is where you left it.

In the conversation, each subagent is a one-line card, and clicking it shows its work the same way. So does clicking a subagent on the Agents page.

### You can't write to a subagent

Claude Code doesn't let you type to a subagent, so where the message box was there's a bar instead. Only Claude can reach a subagent. **Message it through Claude** takes you back to the conversation with the subagent named in the message box: write what you want it to know, and Claude passes it on. The button is there while the subagent runs, and afterwards for most kinds of subagent. Explore and Plan subagents can't be resumed once they've finished, and neither can one you stopped.

While a subagent runs, **Stop subagent…** in the bar, or the **■** on its row, stops it, as on [the agent map](../plans/the-agent-map.md).

### Rows from earlier turns

A subagent's row stays until you dismiss it. Once it has finished, and you've sent a message since it started, it's dimmed: it's no longer part of the work under way. Its tip says *From an earlier turn*. A subagent still running in the background stays bright.

- **×** on a finished row, or **Dismiss** in its right-click menu, takes it off the sidebar. Its card stays in the conversation, and it stays on the Agents page.
- **Dismiss earlier subagents**, in a row's right-click menu, takes every dimmed row off at once.

A tab that's restored when Deventry starts brings back rows only for its last turn's subagents.

## Discouraging or turning off subagents

Settings → Tabs → **Subagents** says whether Claude hands parts of its work to subagents:

- **Claude decides** (the default): Claude uses them when it judges they help.
- **Discourage them**: Claude is asked to do the work itself, in the conversation, and to use a subagent only when you ask for one.
- **Don't allow them**: Claude is told it can't, and Deventry turns down any subagent Claude starts anyway. Its card says *Not started: subagents are turned off for this tab*, and Claude carries on by itself.

It applies at once, in tabs already open too. Claude hears about the change with your next message, and the message's card says so under it, such as *Told Claude to avoid subagents*. One tab can have its own, from its [Tab settings…](../tabs/tab-settings.md).

Workflows, and skills that run in a subagent of their own, aren't affected.

## Questions from a subagent

When a subagent needs permission, the card that asks says who is asking: *Asked by the Explore subagent: Find the auth code*. You answer it like any other ([Permission cards](../permissions/permission-cards.md)). The subagent's row says it's waiting, and the question is under its work too, where you can answer it.

## In a restored tab

A restored tab shows each subagent's finished work, tool calls included, but no live status or running clock. A subagent with no recorded result reads *Didn't finish*.

## Finding things in a subagent

[Find](finding-your-way.md#find) looks inside subagents' work, and a match there shows that subagent's work in the tab (or opens its card, with **In the conversation**). While the tab shows a subagent's work, Find looks through that. Text you select inside a subagent can be added to the [scratch pad](the-scratch-pad.md), like any other.

## Settings

- Settings → Conversation → **Show subagents' work**: **Under the tab in the sidebar** (the default) or **In the conversation**. See [Subagents under their tab](#subagents-under-their-tab).
- Settings → Tabs → **Subagents**: **Claude decides** (the default), **Discourage them** or **Don't allow them**, which a tab's **Tab settings…** can change for it. See [Discouraging or turning off subagents](#discouraging-or-turning-off-subagents).
- **Summarize subagents' progress** (Settings → Claude Code, off by default) is explained with [the agent map](../plans/the-agent-map.md).

## Shortcuts

- `Esc` goes back from a subagent's work to the conversation.

## See also

- [The agent map](../plans/the-agent-map.md) for the tree of every agent and stopping one.
- [Threads](../threads/index.md) for work spread over several tabs, which is different: each sub-thread is a whole tab you can watch and steer.
