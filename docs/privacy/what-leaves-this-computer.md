---
title: What leaves this computer
parent: Privacy and your data
nav_order: 2
summary: "Deventry has no sign-in of its own and sends no analytics."
---

# What leaves this computer

Deventry has no sign-in of its own and sends no analytics. A few things in it can reach beyond your computer, and each is listed here with what it sends, when, and how to turn it off.

What Claude Code sends is Claude Code's. Each tab runs the real `claude`, which sends your prompts and the files it reads to the model, as it does in a terminal, and has its own update checks and settings for telemetry. Deventry doesn't see or change that. Anthropic's [data usage](https://code.claude.com/docs/en/data-usage) page covers it.

## What Deventry itself sends

| What | Where to | When | Turn it off |
|---|---|---|---|
| The update check | GitHub, for Deventry's releases | At launch and every 6 hours. It sends nothing but the request, and doesn't sign in. | Settings → General → **Check for Deventry updates automatically** |
| Downloading an update | GitHub | Only when you choose to download. | Don't download |
| Claude's status | status.claude.com | At launch, then every 5 minutes, and sooner when a tab's request fails on Anthropic's side. A plain request, with Deventry's name and version. | Settings → General → **Show Claude's service status** |
| A web image in a reply | The image's own address | Only when you click **Load image**. | Don't click it |
| A source build's check for new commits | The git remote its checkout tracks | At launch and every hour, instead of the update check. It's `git fetch`, with your git and its credentials. | Settings → General → **Check for Deventry updates automatically** |

A copy from the Microsoft Store doesn't check GitHub's releases: the Store updates it. A source build doesn't either: it fetches from its checkout's remote instead. See [Deventry's updates](../updates/deventrys-updates.md), [Running from source](../getting-started/running-from-source.md#when-there-are-new-commits), [Claude is having problems](../troubleshooting/claude-is-having-problems.md) and [Images](../conversation/images.md).

Deventry also starts other programs for you, such as `git`, `p4`, diff tools and, to check for Claude Code updates, `claude` itself and Homebrew or WinGet, with the same environment `claude` has. They talk to whatever they'd talk to in your terminal, such as your git remote, your Perforce server or a package source.

## Through the session library

Deventry copies things into the [session library](../history/the-session-library.md) only for what you've chosen. The library is a folder. If you've put it in a folder that Google Drive, Dropbox, OneDrive, iCloud Drive or Box keeps in sync, that service uploads it, and Deventry asks you to confirm before it uses such a folder.

- **A tab that syncs** copies its session: a record of the tab, and a copy of Claude Code's transcript, which holds your code, command output and anything else Claude read. A tab that doesn't sync never writes to the library. See [Syncing a tab](../history/syncing-a-tab.md).
- **Sharing usage** (Settings → Usage → **Share usage with my other machines**, off by default) writes your plan usage readings from the last 8 days, and the turns of tabs that sync: the time, model, token counts, estimated cost, project and tab names. Never prompts or replies. Your email isn't written either: the files name your account by a hash. See [Sharing usage across machines](../usage/sharing-usage-across-machines.md).
- **Settings sync** (Settings → Sessions → **Sync Deventry's settings through the library**, off by default) writes the settings that sync. A Perforce password or an API key is never among them. See [Settings sync](../history/settings-sync.md).
- **Scratch pads** of projects that have a git remote are kept in the library, so your other machines see them. A pad for a folder with no remote stays on this machine. See [The scratch pad](../conversation/the-scratch-pad.md).

Claude Code's sign-in and settings are never copied to the library.

If you leave the library in its default folder, which no sync client watches, none of this goes anywhere else.

## Through the Claude app

While a tab is connected to the [Claude app](../away/the-claude-app.md), its transcript is stored on Anthropic's servers. It's off for every tab until you turn it on, tab by tab. Your files and the commands stay on your computer.

## Through a Telegram bot

While you use a Telegram bot with [Remote clients](../away/remote-clients.md), Deventry talks to Telegram (api.telegram.org) to read your messages to the bot and send its own. Telegram can read bot chats, which aren't end-to-end encrypted, so it sees what the bot sends you: your tabs' and projects' names, Claude's replies, and the commands and files Claude asks about. Settings → Remote clients → All clients → **What they say** → **Just what happened** keeps the messages to a tab's name and what happened. It's off until you turn it on, and the bot's token stays in your computer's credential store.

## What stays here

Your usage history, drafts, the stash, unsaved edits and logs stay in Deventry's data folder ([Where Deventry keeps things](where-deventry-keeps-things.md)). Your Perforce passwords, API keys and a Telegram bot's token are in your operating system's credential store ([Passwords and keys](passwords-and-keys.md)). Deventry's logs have sign-in codes and values named like tokens, keys and passwords taken out, but the optional protocol log holds your prompts and file contents, and is only written while you've turned it on.

## See also

- [Where Deventry keeps things](where-deventry-keeps-things.md).
- [Passwords and keys](passwords-and-keys.md).
