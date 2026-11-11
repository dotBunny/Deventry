---
title: Permission modes
parent: Permissions and prompts
nav_order: 2
summary: "A permission mode decides how much Claude Code asks about before it acts."
---

# Permission modes

A permission mode decides how much Claude Code asks about before it acts. The modes are Claude Code's own ([its permissions docs](https://code.claude.com/docs/en/permissions) describe them); Deventry puts a picker for them on every tab.

## Changing a tab's mode

The mode is on the left of the [composer bar](../composer/the-composer-bar.md), for example **Auto ▾**. Click it and pick one:

| Mode | What it means |
|---|---|
| **Manual** | Ask before edits and commands. |
| **Accept edits** | Edit files without asking; still ask before commands. |
| **Plan** | Look around and plan; don't change anything. |
| **Auto** | Don't ask; a safety check reviews actions and blocks risky ones. |
| **Bypass permissions** | Skip permission prompts; Claude Code's own deny and ask rules still apply. |

Picking a mode changes it for this session only. Once the tab is running, the picker shows the mode Claude Code reports, including changes Claude Code makes itself, such as leaving Plan mode when you [approve a plan](approving-a-plan.md). Before a tab starts, as a restored tab waits for its first message, it shows the mode the tab will start in ([below](#the-mode-a-tab-starts-in)), and a tab that will start in Bypass permissions has its red border already.

## When Auto isn't offered

Auto is in the picker only while the tab's model supports it (Claude Code leaves it out for Haiku, for example) and no settings file Claude Code reads turns it off. A plan's **Approve, auto mode** button follows the same rule.

## Bypass permissions

Choosing it asks first, in a **Bypass permissions?** box: *For the rest of this session, Claude will run commands and change files without asking.* Choose **Cancel** or **Bypass permissions**. While a tab is in this mode it's hard to miss: a red border on its row and its composer, the mode's name in red, and a line above the composer saying Claude won't ask before running commands or changing files.

{: .note }
Claude Code only lets a session switch into Bypass permissions when it was started in that mode. If the tab wasn't, the switch is refused and a note in the conversation says how to fix it: set the mode in the tab's **Tab settings…**, or Settings → Tabs, then restart the tab.

## The mode a tab starts in

A tab starts in Auto, as a Claude Code session in a terminal does, unless something chooses otherwise. In order:

1. The tab's own **Permission mode** in [Tab settings](../tabs/tab-settings.md). Its first choice is named for what you'd get otherwise, such as **Default (Auto)**.
2. Settings → Tabs → New tabs start with → **Permission mode**.
3. **Claude Code's default**, the choice that setting starts at, shown as **Claude Code's default (Auto)** once it's known. Deventry reads the settings files Claude Code would: managed settings, the project's `.claude/settings.local.json` and `.claude/settings.json`, then your own `settings.json`. A file that sets `permissions.defaultMode` decides the mode. With none, the tab starts in Auto. If a file turns Auto off, or the model can't use it, the tab starts in Manual.

As in a terminal, a project's own files can't make a tab start in Bypass permissions.

A tab that comes back after a restart, or after signing in, isn't given a mode. Claude Code brings back Plan mode for a session that ended in it, and a tab that comes back in Manual is quietly switched to Auto if it can be. The mode you picked during the last run isn't restored.

Settings that come only from an organization's managed settings through MDM, the Windows registry or the claude.ai console aren't read, and neither is `--settings` among Settings → Advanced's extra arguments. A tab can still start in Auto while those say otherwise, but Claude Code enforces `disableAutoMode` from any of them.

## Settings

- Settings → Tabs → New tabs start with → **Permission mode**: **Claude Code's default**, or one of the modes above.
- A tab's **Tab settings…** → **Permission mode**: the same choices, for this tab only.

## See also

- [Permission cards](permission-cards.md): what happens when a mode does ask.
- [How Deventry uses Claude Code](../getting-started/how-deventry-uses-claude-code.md): why your Claude Code settings apply in a tab.
