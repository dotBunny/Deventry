---
title: Perforce
parent: Project tools
nav_order: 8
summary: "In a Perforce workspace, Claude runs p4 commands through its Bash tool."
---

<h1 class="perforce-title">{% include perforce-logo.html full=true %}</h1>

In a Perforce workspace, Claude runs `p4` commands through its Bash tool, and Deventry opens files in Perforce as you and Claude change them ([below](#opening-files-in-perforce)). Perforce login tickets expire, often after 12 hours, and after that every `p4` command fails with *"Your session has expired, please login again."* Claude can't fix that itself, because `p4 login` asks for a password, so a long-running tab gets stuck.

Deventry can keep each Perforce tab logged in. Claude can query and use Perforce without stopping, and it never sees your password. Deventry also shows the changelist Claude is working in, whether or not login handling is on.

## Turn it on

Settings → Perforce → **Keep Perforce logins fresh** is off by default. It applies to tabs started after you change it.

## How a workspace is recognized

Before a tab starts its `claude`, Deventry asks Perforce about the tab's folder. Perforce finds the server, user and workspace the usual way (a `P4CONFIG` file, `p4 set` or the environment), which is how Claude's own `p4` commands find them, with the same environment ([Command line and environment](../reference/command-line-and-environment.md)). The folder is a workspace when `p4` answers, the workspace exists and the folder is under its root. The question gives up after 10 seconds, and a missing `p4` or an unreachable server just means no Perforce handling.

In a workspace:

- The tab's [info card](../tabs/the-tab-info-card.md) has a **Perforce** row, such as *"matt @ ssl:perforce:1666, ticket expires in 11h"*. Other states read "ticket expired", "not logged in", "server unreachable" and "log in yourself (single sign-on or a second factor)". A failed login adds its reason.
- Claude is told the folder is a Perforce workspace, with its server, user and workspace, to use `p4` rather than assume git, never to run `p4 login` or pass a password, and to wait for the renewal message after an expired-session error.

## How the ticket is kept fresh

- **Ahead of time.** Deventry checks the ticket when the tab's session starts, when each turn starts and every 15 minutes. When it has expired, doesn't exist or has less than 30 minutes left, Deventry logs in again. The 30 is Settings → Perforce → **Renew the ticket when less is left than (minutes)**.
- **Just in time.** Before each Bash command that runs `p4`, Deventry checks the ticket, and never changes the command or skips a permission prompt.
- **Recovery.** If a `p4` command still fails because the session expired, or with *"Perforce password (P4PASSWD) invalid or unset."*, Deventry logs in again, tells Claude *"Perforce login renewed. Retry the last p4 command."* and adds a note to the conversation. It does this at most once per turn, so a command that fails for another reason can't loop.
- **After a failure.** When a login fails or you cancel the password prompt, Deventry doesn't try again for 5 minutes. A new turn does.

Tabs in one workspace log in once, not once each.

## The password

Settings → Perforce → **Password source** says where Deventry gets the password to log in:

- **Stored by Deventry (recommended)**, the default. The password is saved in your OS's credential store (Windows Credential Manager, the macOS Keychain, or the Secret Service on Linux). It's never written to Deventry's settings, never synced and never logged. When nothing is stored yet, or Perforce refuses the stored password, the tab asks.
- **Perforce's own configuration (P4PASSWD)**. Deventry reads `P4PASSWD` from your `P4CONFIG` file, `p4 set` or the environment and passes it on. Those sources keep the password in plain text, and Settings warns you. If it isn't set, or Perforce refuses it, the tab says so and Deventry doesn't ask.
- **Ask each time**. Deventry asks whenever a login is needed and stores nothing.

When Deventry asks, a **Log in to Perforce** box appears over the tab with a password field, **Log in** and **Cancel**. With the stored source it also has **Save it in** your store, ticked, and the password is saved once the login has worked. If Perforce refuses a password, the box shows the reason and lets you try again, up to three times. Claude never sees the password: it goes straight to `p4 login`, and never on a command line.

While the box waits, the tab shows *Needs your input*, counts in the Dock or taskbar badge, and sends a notification if you aren't looking.

Under **Stored password**, you can also save or forget a password without waiting for a tab to ask. Enter the **Server (P4PORT)**, **User** and **Password**, then **Save password** or **Forget password**. On Linux this needs `secret-tool`, which comes from the `libsecret-tools` package. Without it, Settings says so, and passwords can only be asked for.

## Servers that need you to log in yourself

Deventry doesn't try a password with servers that sign in through single sign-on or multi-factor authentication. It adds a note to the conversation and sends a notification, *"Perforce needs you to log in: run p4 login in a terminal, or log in with P4V."*, then checks every minute until the ticket is valid.

## The changelist

When Claude works in a numbered changelist, Deventry shows it:

- The tab's info card has a **Changelist** row, such as *"CL 12345"*, and *"earlier: CL 12001 · submitted, …"* when the session used several.
- With Settings → Perforce → **Show changelist on tabs** on (off by default), a `CL 12345` badge sits at the right of the tab's second line in the sidebar. Clicking it offers **Copy changelist number** and **Open in P4V**.
- The info card lists every changelist with **Copy** and **Open in P4V**.

Deventry reads it from the `p4` commands Claude runs and their output, so it works in every tab, even with **Keep Perforce logins fresh** off. A command that names a changelist, such as `p4 edit -c 12345`, or output such as *"Change 12345 created."* counts. The most recently used changelist wins, and the default changelist isn't shown. When the changelist is submitted, the badge says `CL 12345 · submitted`. When it's deleted, the badge goes away. The changelist is saved with the tab, and a link can use it as `{changelist}` ([Actions and links](actions-and-links.md)).

## In the Files tab

In a Perforce workspace, a project's [Files tab](../project-files/index.md) shows what Perforce has open, and its menus have Perforce's commands. This works whether or not **Keep Perforce logins fresh** is on.

Beside a file's name, the explorer says what it's opened for, in Perforce's own words: *edit*, *add*, *delete*, *move*, or *others* when only someone else has it open. Point at it for the whole of it, such as *Checked out in CL 12345.* Files nobody has open have no mark.

Deventry asks Perforce in the background, a folder at a time as the explorer shows it, and again when files change on disk, after its own commands and on **Refresh**. The tree never waits for Perforce: the marks fill in when the answer comes. When the server doesn't answer, the explorer says so once, under its header, and has no marks.

Right-click a file or folder, or a file's tab, for the Perforce section:

- **Check out**, for a file Perforce has that isn't open.
- **Mark for add**, for a file Perforce doesn't have. On a folder, it adds the new files in it.
- **Mark for delete**, which asks first, since Perforce deletes the file from the disk too. **Revert** brings it back.
- **Revert**, which asks first when you'd lose changes.
- **Diff against the depot**, the revision you have against the file, in the diff window or your [diff tool](../changes/external-diff-tools.md).

What Perforce refuses, such as a file someone else has locked, shows under the explorer's header until you dismiss it.

## Opening files in Perforce

In a workspace, Deventry opens files in Perforce as they change, so you and Claude don't have to. Each of these is a switch in Settings → Perforce → **Opening files in Perforce**, on by default:

- **Check out files before they're changed.** A file Perforce hasn't checked out is read-only. In a Files tab it opens ready to edit anyway, and your first change checks it out; saving waits for that. Before Claude's Edit or Write changes one, Deventry checks it out first. With this off, such a file opens read-only in the Files tab, and its tab's menu has **Check out**.
- **Add new files.** A file you make with **New file**, or that Claude creates, is marked for add. Perforce's own ignore rules still apply.
- **Mark deleted files for delete.** A file or folder you delete from the explorer goes to the trash, as before, and is marked for delete. Files Claude deletes with a command aren't seen.
- **Rename with p4 move.** Renaming in the explorer moves the file in Perforce too.
- **Show Perforce state in the Files tab**, the marks above.

Files go in the changelist the tab is working in, the one its `CL` badge shows, or the default changelist when it has none. From the Files tab, it's the changelist of the project's tab you used last.

## Settings

All in Settings → Perforce. None of it syncs between machines. **Reset to defaults** keeps the per-folder entries and stored passwords.

| Setting | Default | What it does |
|---|---|---|
| **Keep Perforce logins fresh** | Off | Detects workspaces, tells Claude, and renews tickets. |
| **Password source** | **Stored by Deventry (recommended)** | Where the password comes from. |
| **Renew the ticket when less is left than (minutes)** | 30 | When Deventry logs in again. |
| **Request tickets valid on all hosts** | Off | Asks Perforce for a ticket valid on every host (`p4 login -a`). |
| **Show changelist on tabs** | Off | The `CL 12345` badge on a tab's row. |
| **Check out files before they're changed** | On | Checks out a file as you start editing it in a Files tab, and before Claude's Edit or Write changes it. |
| **Add new files** | On | Marks for add a file made with **New file**, or created by Claude. |
| **Mark deleted files for delete** | On | Marks for delete a file deleted from the explorer. |
| **Rename with p4 move** | On | Moves a file renamed in the explorer in Perforce too. |
| **Show Perforce state in the Files tab** | On | The marks beside files' names. |
| **Stored password** | None | **Save password** and **Forget password**, for a server and user. |
| **Per-folder server and user** | None | **Add folder…** sets a server or user for tabs in a folder, when Perforce's own answer isn't right. The deepest folder that holds the tab's wins. |

## See also

- [Passwords and keys](../privacy/passwords-and-keys.md) for where secrets are kept.
- [Notifications](../away/notifications.md), where a waiting password prompt counts as needing input.
