---
title: Tickets
parent: Project tools
nav_order: 7
summary: "A project's GitHub and YouTrack issues in a tab beside Project Files, and a tab started on one with the ticket in its message."
---

# Tickets

{% include unreleased.html %}

A project's tickets, from the trackers it uses, in a tab of the project's own beside Project Files: the list, a ticket's text and comments, and **Work on it**, which starts a tab on the ticket with the ticket in its message, ready for you to send. GitHub's issues and YouTrack's issues are the first trackers Deventry reads.

{% include shot.html name="guide/project-tools/tickets" alt="The starfall group's Tickets tab under its Project Files row: the open GitHub issues listed at the left with their labels, age and comments, and issue #142 at the right with its state, assignee and milestone, Work on it and Open on GitHub, its text and three comments." %}

## Open the Tickets tab

A group whose folder has a ticket source has a **Tickets** button on its row, a ticket's outline, between the project's icon and **Project files**. Click it, or choose **Tickets** in the group's menu (under **Folder**), or **Open tickets** in the command palette. The tab opens as the group's second row, under its Project Files tab, or is selected when it's open.

Like the Project Files tab, the Tickets tab has no session: it can go [side by side](../tabs/split-panes.md) with a conversation, comes back when Deventry starts, with its filter and the ticket it showed, keeps its group in the sidebar when you close the group's last tab, and `Ctrl+Tab` passes it by. Its row says how many of the tickets shown are open (*12 open*, or *50+ open* while there are more to load), and how many were opened since you last looked (*· 1 new*). Close it with its ×.

## Where a project's tickets come from

- **From the git remote.** A folder whose git remote (`origin`, or else the first) is a GitHub repository has that repository's issues, without anything to set up. Settings → Tickets → **GitHub** → **Find a project's repository from its git remote** turns that off.
- **From the project's settings.** Choose **Project settings…** in the group's menu and go to **Tickets**, or **Ticket sources…** in the Tickets tab's **⋯**. **Add…** asks for the provider (**GitHub** or **YouTrack**), the repository (`owner/name`) or the YouTrack server, project and query, a **Name** for the source menu, and the file it goes in: **Just me (deventry.local.json)** or **Shared with the project (deventry.json)**. The one from git stays beside them: a YouTrack project you add sits next to your GitHub repository. Only a GitHub repository you add takes its place, and removing it brings the one from git back.
- **By hand**, in `tickets` in [`deventry.json`](../reference/deventry-json.md).

A project can have several sources; the list's header then has a menu to choose one, or **All sources**. Showing them all, each ticket has a chip naming its source, as the menu does, and so does the ticket you're reading.

## Find a ticket

- The filter box filters as you type, and takes the tracker's own search words: GitHub's `label:bug`, `author:kim` or `no:assignee`, or YouTrack's query words, such as `Type: Bug` or `#Critical`. Its tip shows some.
- **Open**, **Closed** or **All**; **Mine**, for those assigned to you, while Deventry is signed in; and **Sort**: **Newest**, **Recently updated** or **Oldest**.
- The list loads 50 tickets at a time: **Load more** at its end reads the next. **Refresh** (or `F5`) reads it again; it's also read again when you come back to the tab more than five minutes later. Nothing is read while the tab isn't open.

Each row has the ticket's key and title, and under them its labels, how long ago it was opened, its comments, and how many open tabs work on it (*1 tab*).

Drag the list's right edge to make it wider or narrower, for every Tickets tab; double-click the edge for its first width. In a pane too narrow for both, the ticket takes the list's place.

## Read a ticket

Choose a row. The ticket shows at the right: its key and title, its state, labels and who opened it, its other fields (assignees and milestone, or YouTrack's fields), its text and its comments, as Markdown. Links open in your browser, and images on the web wait until you click **Load image**, since a ticket is anyone's text. **Open on GitHub** (or **Open on YouTrack**) opens it in your browser; **⋯** has **Copy link** and **Copy key**.

In a narrow pane, the ticket takes the list's place, with **← Tickets** to go back.

## Work on a ticket

**Work on it** opens a new tab in the project's folder, with the ticket in its message:

- The message box has the ticket as an attachment, *GitHub issue #142 · 3 comments · 4 KB*, with *Work on this ticket.* typed under it. **Nothing is sent** until you send it: say what you want done, and read what the ticket asks first. Anyone can file an issue on a public repository, and its text goes to Claude, marked as the tracker's text rather than yours.
- Its arrow offers **In a new tab** and **In a new tab in a worktree**, a [worktree](../tabs/worktree-tabs.md) named for the ticket (`issue-142`, on the branch `worktree-issue-142`). Settings → Tickets → **All providers** → **Work on a ticket in** chooses what the button does by itself.
- The tab is named after the ticket, *#142 Grappling hook detaches on slopes*, as a rename would name it; **Reset name** gives it Claude Code's name back.

{% include shot.html name="guide/project-tools/tickets-work-on-it" alt="A new tab in the starfall group named #142 Grappling hook detaches on slopes, its message box holding the ticket as an attachment, GitHub issue #142 · 3 comments · 1 KB, with Work on this ticket. typed under it, nothing sent yet." %}

The tab remembers its ticket: its [info card](../tabs/the-tab-info-card.md) has a **Ticket** row, its menu has **Open ticket**, which goes back to it in the Tickets tab, and the ticket shows **Tab on it** with **Go to tab**. A copy of the tab keeps it.

## Change a ticket

With Settings → Tickets → **GitHub** (or **YouTrack**) → **Let Deventry change tickets** on, a ticket also has:

- **Comment…**, a box under its comments, in Markdown;
- **Change state**: GitHub's **Close as completed**, **Close as not planned** and **Reopen**, or the states the YouTrack project has. Closing or resolving asks first;
- **Assign to me**, in **⋯**.

Each change is made with your token's own rights: one that may only read gets the tracker's refusal, which says what the token needs.

## Sign in

Settings → Tickets has a page for each provider, with its servers.

- **GitHub** reads public repositories without signing in, at GitHub's 60 requests an hour for your computer. For private repositories, and a higher limit, choose how Deventry signs in to github.com under **Sign in with**:
  - **A token**: a fine-grained personal access token with **Issues: Read-only** on the repositories you want (or **Read and write** to change tickets), or a classic one with `repo`. **Set token…** checks it with GitHub before keeping it.
  - **The GitHub CLI (gh)**, if you're signed in to `gh`.
  - **Git Credential Manager**, which Git uses for GitHub once you've cloned or pushed over HTTPS.

  **Add a GitHub Enterprise server…** adds your company's server, and its repositories are found from git remotes too.
- **YouTrack**: **Set token…** with a permanent token from YouTrack (your profile → **Account Security** → **New token…**). A server a project's ticket source names is listed here once its Tickets tab has read it; **Add a YouTrack server…** adds another by its address (`https://yourteam.youtrack.cloud`, or your server's). Without a token, Deventry reads what the server shows its guests: a project that's private says to set a token, with **YouTrack settings…**.

Tokens go only to your system's credential store (Windows Credential Manager, the macOS Keychain, or the Secret Service on Linux), never into Deventry's settings, and they aren't synced. A token from `gh` or Git Credential Manager is only kept in memory.

When something goes wrong, the list says what and what to do: a token GitHub didn't accept, a repository it didn't find (which may be private), the limit used up until when, or a tracker it couldn't reach, with **Try again**.

## Settings

Settings → **Tickets**:

| Setting | Default | What it does |
|---|---|---|
| All providers → **Work on a ticket in** | **A new tab** | What **Work on it** opens: a new tab, or a new tab in a worktree named for the ticket. |
| All providers → **Name new tabs after their ticket** | On | The new tab's name is the ticket's key and title. |
| All providers → **Include the ticket's comments** | On | The comments go in the message, after the ticket's text. |
| GitHub → **Sign in with** | **Nothing (public repositories only)** | How Deventry signs in to each server. |
| GitHub → **Find a project's repository from its git remote** | On | A folder whose remote is on GitHub has its issues without a source of its own. |
| GitHub, YouTrack → **Let Deventry change tickets** | Off | Comment, change a ticket's state and assign it to you from the Tickets tab. |

All of them are this computer's, and don't sync.

## Shortcuts

| Keys | Does |
|---|---|
| `F5` | Refresh the list. |
| `Ctrl/Cmd+F` | Go to the filter box. |
| `Up`, `Down` | Choose a ticket. |

## See also

- [Plans, tasks and agents](../plans/index.md): Claude's own task list, which isn't tickets.
- [Actions and links](actions-and-links.md): a link to your issue board in the project's menu.
