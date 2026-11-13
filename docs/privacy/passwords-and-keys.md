---
title: Passwords and keys
parent: Privacy and your data
nav_order: 3
summary: "Deventry keeps the secrets you give it, Perforce passwords, API keys and your bots' tokens, in your operating system's credential store and nowhere else."
---

# Passwords and keys

Deventry keeps the secrets you give it, Perforce passwords, API keys and your bots' tokens, in your operating system's credential store and nowhere else. They're not in settings, in state, in logs, in an environment variable or on a command line, and they're never synced.

Claude Code's own sign-in is Claude Code's. Deventry never reads or stores it.

## The credential store

- **Windows:** Credential Manager, kept for your user on this machine and not roamed to others.
- **macOS:** the Keychain.
- **Linux:** the Secret Service, through `secret-tool`. Without `secret-tool` (the `libsecret-tools` package), Settings says to install it. Perforce passwords can then only be asked for each time, and API keys can't be added. See [Something missing on Linux](../troubleshooting/something-missing-on-linux.md).

Each machine has its own store, so a secret you saved on one machine isn't on another. [Settings sync](../history/settings-sync.md) and the [session library](../history/the-session-library.md) never carry them.

## Perforce passwords

Settings → Perforce → **Password source** chooses where the password comes from:

- **Stored by Deventry (recommended)** keeps it in the credential store, under the server and user. The first time a tab needs to log in, it asks and offers to save the password once the login has worked. Settings → Perforce → **Stored password** can also **Save password** or **Forget password** for a server and user.
- **Perforce's own configuration (P4PASSWD)** reads `P4PASSWD` from your Perforce configuration. Those places keep a password as plain text, and Settings warns about it. Deventry only reads it.
- **Ask each time** shows a password prompt when a login is needed and stores nothing.

When Deventry logs in for you, it gives the password to `p4 login` on its standard input, so it never appears on a command line, where other programs could see it. Claude never sees it. See [Perforce](../project-tools/perforce.md).

## API keys

A key you add in Settings → Accounts → **Add an API key** goes to the credential store as soon as you save it. Deventry's files keep only the account's name and color. Without a credential store, such as Linux without `secret-tool`, adding a key is off, and says why.

Claude Code gets the key when it asks for it, through a small command that Deventry itself runs: it prints the key from the credential store for the `claude` that asked, then exits. The key isn't in an environment variable, on a command line or in a file. **Change key…** replaces it, and **Remove…** on an account deletes the key from the store. See [API-key accounts](../accounts/api-key-accounts.md).

## A bot's token

The token you give Settings → Remote clients → Telegram or Discord → **Set token…** goes to the credential store as soon as you save it, and Deventry's files keep only the bot's name (and a Discord app's id, for the link that adds it). Every request Deventry makes to a Telegram bot has the token in its address, and every answer to a tap in Discord has that tap's own token in its address, so Deventry's log never records an address, and an error that would show one is reworded without it. **Remove…** deletes the token from the store. See [Remote clients](../away/remote-clients.md).

## In the logs

Deventry's own log, `deventry.log`, holds its warnings and errors. The optional protocol log (Settings → Advanced → **Log protocol traffic**) takes sign-in codes and values under the usual names for tokens, keys and passwords out of each line, but it holds your prompts and file contents, so treat it as private and don't post it as it is. It's deleted after 7 days.

## See also

- [Where Deventry keeps things](where-deventry-keeps-things.md).
- [What leaves this computer](what-leaves-this-computer.md).
