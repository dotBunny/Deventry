---
title: Remote clients
parent: Away from your desk
nav_order: 4
summary: "Your tabs from your phone, through a Telegram or Discord bot that Deventry runs itself: told when a tab needs you or finishes, its prompts answered with a tap, and messages sent by replying."
---

# Remote clients

{% include unreleased.html %}

Deventry can talk to you through a bot of your own, in Telegram or Discord, while you're away from the computer. The bot tells you when a tab needs you or finishes, shows what each tab is doing, and lets you answer its prompts with a tap, send it a message by replying, or stop it. Claude, the tools and the files stay on your computer: the bot only carries what Deventry already shows.

Deventry talks to the bot itself, so listing your tabs, reading what they're doing and answering prompts cost no tokens. Only a message you send a tab does, as typing it at the desk would.

Each way in is a *remote client*: a Telegram bot, or a Discord bot on your own Discord account.

Settings has a group for them, **Remote clients**, at the end of its sidebar: **All clients** for what they all share, then a page for each: **Telegram** and **Discord**.

{% include shot.html name="guide/away/remote-clients" alt="Settings on Remote clients → Telegram, before a bot is set up: The bot, with Use a Telegram bot off and a note in yellow that Telegram can read bot chats; Bot, with No bot yet and Set token…; and Paired with, with No phone is paired yet and Pair a phone… disabled. The sidebar's last group, Remote clients, has All clients, then Telegram and Discord, each marked Off." %}

## Not the Claude app

[The Claude app](the-claude-app.md) is Claude Code's own link to one tab at a time, which needs a claude.ai subscription and keeps the connected tab's conversation on Anthropic's servers. Remote clients are Deventry's own: they cover every tab at once, whatever account it runs under, and nothing about your tabs goes to Anthropic. You can use either, or both.

## Setting up a Telegram bot

You make the bot in Telegram, once, and give Deventry its token.

1. In Telegram, open [@BotFather](https://t.me/BotFather) and send it `/newbot`.
2. Give the bot a name, then a username that ends in `bot`. BotFather answers with the bot's token, a line like `123456789:AAH…`.
3. In Deventry, open Settings → Remote clients → **Telegram** and turn on **Use a Telegram bot**. Until you do, the sidebar says *Off* after **Telegram**.
4. Choose **Set token…**, paste the token and choose **Save token**.

Deventry keeps the token only in your computer's credential store (Credential Manager on Windows, the Keychain on macOS, the Secret Service on Linux): never in its settings, and it doesn't sync. Under **Bot**, Settings shows the bot's `@username` once Telegram has said it, and whether it's **Connected**.

There's no server to set up, and no port to open: Deventry asks Telegram for the bot's messages, so it works behind any router. Telegram lets only one program do that for a bot, so each computer needs a bot of its own. If another Deventry or another program already uses the bot, Settings says so, and Deventry tries again each minute.

If the bot already sends its messages to a web address (a *webhook*), Deventry can't listen to it, and Settings says so. **Use it for Deventry anyway** takes the web address away; Deventry never does that by itself.

To stop using the bot, turn **Use a Telegram bot** off. **Remove…** takes the token off this computer; the bot itself, and its chats, stay in Telegram.

## Setting up a Discord bot

You make the bot in Discord's Developer Portal, once, as an app that goes on your own Discord account rather than in a server, and give Deventry its token.

1. Open the [Developer Portal](https://discord.com/developers/applications), signed in to Discord, and choose **New Application**. Give it a name.
2. Under **Installation**, in **Installation Contexts**, tick **User Install** and untick **Guild Install**, so the bot can go on your account and in no server.
3. Under **Bot**, choose **Reset Token** and copy the token, three parts with dots between them.
4. In Deventry, open Settings → Remote clients → **Discord**, turn on **Use a Discord bot**, choose **Set token…**, paste the token and choose **Save token**.

Deventry keeps the token only in your computer's credential store, as it does Telegram's. Under **Bot**, Settings shows the bot's name and whether it's **Connected**.

{% include shot.html name="guide/away/remote-clients-discord" alt="Settings on Remote clients → Discord, before a bot is set up: The bot, with Use a Discord bot off and a note in yellow that Discord can read direct messages; Bot, with No bot yet, how to make one in the Developer Portal, and Set token…; and Paired with, with Pair a phone… disabled." %}

There's no server to set up here either: Deventry keeps a connection open to Discord, as Discord's own bots do. Discord lets two programs use one bot, but only one can answer its buttons, so each computer needs an app of its own; if another Deventry or another program answers first, Settings says so.

If Settings says the app can't be added to a Discord account, tick **User Install** under **Installation**. If it says the app sends its commands to a web address, clear **Interactions Endpoint URL** under **General Information**. Then turn **Use a Discord bot** off and on again.

## Pairing your phone

The bot answers only the accounts you pair with it. Anyone else who finds it gets no answer at all.

In Telegram:

1. In Settings → Remote clients → **Telegram**, choose **Pair a phone…**. Deventry shows a QR code, and a code like `7KQ2-M9XD`.
2. Scan the QR code with your phone's camera. It opens the bot in Telegram; tap **Start**. Or send the bot `/start` and the code yourself.
3. The bot answers *Paired with Deventry on DESK.*, with your computer's name, and Settings lists your account under **Paired with**.

A code works once, for five minutes, and five wrong codes end it: choose **Pair a phone…** again for a new one. To stop the bot answering an account, choose **Unpair…** beside it.

Only private chats with the bot count. Adding it to a group does nothing.

In Discord:

1. In Settings → Remote clients → **Discord**, choose **Pair a phone…**. Deventry shows a QR code, **Open the link**, and a code like `7KQ2-M9XD`.
2. Scan the QR code with your phone's camera, or choose **Open the link** on this computer, and choose **Add to My Apps**. The bot is on your Discord account now.
3. In any chat in Discord, send `/pair` with the code. Only you see its answer, and the bot writes to you in its direct messages: *Paired with Deventry on DESK.*

Discord may not let a bot write to you before you've written to it. If the answer to `/pair` says so, open the bot's profile from the answer, choose **Message**, and send it `/help`.

## What the bot sends

Each message starts with the tab's name and its project.

- **A prompt.** *Allow this command?* with the command, *Allow editing Player.cs?*, Claude's question, or *Claude has a plan for you to review.* with the plan as a Markdown file; and the card's own answers as buttons: **Allow**, **Deny** and **Deny with a message…**; a question's options and **Answer in my own words**; a plan's **Approve, auto mode**, **Approve, accept edits**, **Approve, ask before edits** and **Keep planning…**. A button that takes words asks for them, and your next message is the answer; one where the words are optional also offers **Without a message**. A question with several parts comes a part at a time.
- **A finished turn.** How long it took, where Claude's task list stands, the start of the reply, and the files it changed with their lines added and removed, with **Whole reply** and **Changed files**. A reply too long for a message comes as a Markdown file.
- **An error.** Why the tab stopped, *Claude Code stopped unexpectedly (exit code 3).*, with **Start it again**. If Claude Code needs you to sign in, the message says to do that at the desk.
- **Usage.** Usage alerts and your accounts' spend warnings, as the [notifications](notifications.md) say them.
- **Closing.** *Deventry on DESK is closing.* when you close Deventry, and, without a sound, that it's back when it starts again.

When a prompt is answered, anywhere, its message loses its buttons and says how: *Allowed from Telegram at 14:09*, *Answered at the desk at 14:31* or *No longer needed*. In Deventry, a card answered from your phone says where: *Answered from Telegram*, or *Answered from Discord*.

Some cards can only be answered at the desk: an MCP server's form, whether to trust a folder, signing in and continuing under another account. Their messages say *Answer this at the desk.*

Buttons belong to the Deventry that sent them. After Deventry restarts, an old button says so and loses its buttons; `/tabs` gives you new ones.

## Talking to a tab

- **Reply** to any of the bot's messages, with Telegram's or Discord's reply, to send your message to that message's tab.
- **`/to`** picks the tab your plain messages go to, from buttons, or by name: `/to grappling`. A tab's **Send my next messages here** does the same.
- A plain message with no tab picked gets the tabs as buttons, *Which tab is this for?*, and goes once you pick one.

The bot answers *Sent to Grappling hook.*, and that message then shows what the tab is doing and for how long, updated as it works, with **Stop**. The turn's end comes as a new message, which makes your phone buzz.

Your message reaches Claude as if you'd typed it at the desk: it waits its turn while Claude is working, and its card says *You, from Telegram* (or Discord). What's in the tab's message box at the desk is left alone. Only text can be sent for now.

## Commands

The bot's commands are in the **/** menu, in Telegram and in Discord. In Discord they work in the bot's direct messages, but for `/pair`, which works in any chat.

| Command | What it does |
|---|---|
| `/tabs` | The tabs, by project, with what each is doing; tap one for its card and buttons. |
| `/to` | Picks the tab your plain messages go to. |
| `/stop` | Stops Claude in a tab, asking which when several are working. |
| `/usage` | Your plan's usage and when it resets, and your accounts' spend today. |
| `/quiet` | No messages for an hour; `/quiet 30` for 30 minutes, `/quiet off` to end it. Answers to what you do from your phone still come, silently. It's the same as the header's **Quiet for an hour**. |
| `/new` | Opens a tab in a favorite or recent folder, when **Open new tabs** is on. |
| `/help` | What the bot can do. |
| `/pair` | Pairs your Discord account, with the code Settings shows. In Telegram, it's `/start` and the code. |

## When messages come

By default, the bot sends messages **While I'm away**: when any of these is true.

- Deventry isn't the window in front.
- Your computer is locked.
- Its screen saver is on, or its display is asleep.
- Nobody has used its keyboard or mouse for five minutes, or what **Count me away after** says.

When you go away, the prompts already waiting are sent then, so walking off with one open still reaches your phone. Settings says what your computer can tell: some Linux desktops say only whether Deventry is in front.

Choose **Always** to have every message come, silently while you're at the desk, so the chat keeps a record and your phone buzzes only when you're away.

A tab you sent a message to from your phone always answers, away or not.

## The icon in the header

While a remote client is on, the header has an icon for it beside the [cup](keeping-the-computer-awake.md): a dot with two arcs on each side. It's one icon however many clients are on, and it shows the state that most needs you:

- **Muted**: connected, and you're at the desk, so messages wait (or come silently, with **Always**). Also while messages are quiet.
- **In the accent color**: connected, and you're away, so messages go to your phone.
- **Dimmed**: a client is connecting, or trying again after a network problem.
- **In the warning color**: a client has stopped and needs you, for example because Telegram or Discord didn't accept its token or another program uses the bot, or no phone is paired yet.

Its arcs light up outward when a client sends you a message or gets one from you. With motion reduced, they only take the accent color for a moment.

Point at it to see each client and what it's doing, such as *Telegram (@deventry_bot): connected, last sent 14:09*, and whether you count as away. Click it, or right-click it, for its menu:

- Each client that's on, with its state and its settings, such as **Telegram settings…**.
- **Quiet for an hour**, which holds messages from every client as `/quiet` does; while they're quiet, **Resume messages** ends it.
- **Remote clients settings…**, which opens **All clients**.

A click never turns a client off: do that on its page in Settings.

## What remote clients can do

Settings → Remote clients → All clients → **What they can do** sets it for every remote client:

- **Watch**: the tabs, Claude's replies, changed files and usage, and messages when things happen.
- **Watch and answer prompts**: and allow or deny, answer a question, approve a plan or keep planning.
- **Watch, answer, send messages and stop Claude**, the default: and send a tab a message, stop it, and start a stopped tab again. Claude can then run what it could at the desk, in the tab's permission mode.

**Open new tabs**, off by default, lets `/new` open a tab in a favorite or recent folder.

Some things are only at the desk, whatever you choose: saving an **Always allow** rule, changing a tab's mode, model, account or settings, rewinding, closing a tab, and an MCP server's form.

## Privacy

Telegram can read bot chats, and Discord its direct messages: neither is end-to-end encrypted. So the service sees what the bot sends you: your tabs' and projects' names, Claude's replies, and the commands and files Claude asks about. Settings says so beside each switch.

To send less, set All clients → **What they say** to **Just what happened**: messages then say only the tab's name and what happened (*Grappling hook needs you.*), with no reply, command, path or file name. A prompt's message has **Show**, which sends that prompt's details and buttons when you ask. See [What leaves this computer](../privacy/what-leaves-this-computer.md).

## Keeping the computer awake

A sleeping computer can't answer your bots. While one is on, Deventry keeps the computer from sleeping, as long as Settings → General → **Also while a remote client is on** is on (the default). The screen can still turn off. See [Keeping the computer awake](keeping-the-computer-awake.md).

## Settings

All this computer's: none of them sync.

| Setting | Default | Where |
|---|---|---|
| **Use a Telegram bot** | Off | Settings → Remote clients → Telegram |
| **Use a Discord bot** | Off | Settings → Remote clients → Discord |
| **Remote clients can** | Watch, answer, send messages and stop Claude | Settings → Remote clients → All clients |
| **Open new tabs** | Off | Settings → Remote clients → All clients |
| A tab needs permission or an answer; finishes its turn; its Claude Code stops with an error; usage alerts | On | Settings → Remote clients → All clients |
| A project action finishes | Off | Settings → Remote clients → All clients |
| **When** | While I'm away | Settings → Remote clients → All clients |
| **Count me away after (minutes)** | 5 | Settings → Remote clients → All clients |
| **What they say** | Everything | Settings → Remote clients → All clients |
| **Also while a remote client is on** | On | Settings → General |

## See also

- [The Claude app](the-claude-app.md), Claude Code's own way to one tab from your phone.
- [Notifications](notifications.md), the ones on this computer.
- [Keeping the computer awake](keeping-the-computer-awake.md), the cup beside the icon.
- [Permission cards](../permissions/permission-cards.md), the cards the bot's buttons answer.
