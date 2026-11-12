---
title: Your Claude sign-in
parent: Accounts
nav_order: 1
summary: "Claude Code keeps your Claude credentials, not Deventry."
---

# Your Claude sign-in

Claude Code keeps your Claude credentials, not Deventry. Whatever Claude Code is signed in to is the account your tabs run under, and the account whose plan limits the [usage header](../usage/the-usage-header.md) shows.

## The account menu

Click your email at the header's right to open the account menu. It shows:

- *Signed in to Claude Code as*, then your email, your plan (such as *Max plan*) and your organization.
- **Plan and billing**, which opens your plan and billing on claude.ai in your browser. For an API key or a Claude Console account, the link is **Console billing** and opens the Console's billing page instead. A cloud provider (Bedrock, Vertex or Foundry) bills through that provider, so there's no link.
- **Sign out…**

When Claude Code is signed out, the menu says *Claude Code isn't signed in.* and offers **Sign in**.

## The plan beside your email

The plan in the header, such as *Max plan*, is a link too. It opens your plan's usage on claude.ai. For an API key or Console account it opens the Console's billing page.

## Signing out

1. Open the account menu and click **Sign out…**.
2. Read the question, *Sign out of Claude Code?*, and click **Sign out**. Every tab stops working until you sign in again, and Claude Code is signed out in the terminal too.

Deventry then asks Claude Code again. When it's signed out, the banner *Claude Code needs you to sign in.* shows across all tabs, messages you send are held, and every tab that was running starts again on its session after your next sign-in, since that may be a different account. There's no notification, because you just did it.

If Claude Code still reports a sign-in afterward, the menu says so: an API key set in the environment or in Claude Code's settings isn't removed by signing out.

Settings → Claude Code shows the same account line, with **Sign in** and **Sign out…**.

## Signing in again

The banner's **Sign in** opens the sign-in dialog and starts signing in. So does **Sign in** in the header's account menu, which is there while Claude Code needs a sign-in, with the banner dismissed too. [Signing in](../getting-started/signing-in.md) walks through it, and [Signing in](../troubleshooting/signing-in.md) in Troubleshooting covers when it fails.

## The Default account

Once you add an [API-key account](api-key-accounts.md), this sign-in is called **Default**. Tabs run under it unless their folder group or the tab picks another account, and the header's plan meters are its plan's.

The sign-in banner and the messages held while signed out are about Default. A tab on an API-key account has its own key, and doesn't use them.

## See also

- [API-key accounts](api-key-accounts.md) for keys you add yourself.
- [A tour of the window](../getting-started/a-tour-of-the-window.md) for the rest of the header.
