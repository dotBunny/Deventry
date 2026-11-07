---
title: Picking up on another machine
parent: History and syncing
nav_order: 4
summary: "A session that syncs shows up in History on your other machines, where you can continue it."
---

# Picking up on another machine

A session that [syncs](syncing-a-tab.md) shows up in History on your other machines, where you can continue it. The library moves the conversation, not your code, so Deventry checks the folder and the code on this machine before it starts.

You need Settings → Sessions → Session library → **Folder** to be the same synced folder on both machines ([the session library](the-session-library.md)).

## Continuing a session

1. On the first machine, make sure the tab syncs and its turn has finished. Wait for your sync client to upload the library.
2. On the other machine, open [History](history.md). The session is there, with the first machine's name in its details.
3. Click it. Deventry finds the project, checks the code, and resumes the session in a new tab. From then on the tab syncs too.

Deventry copies the session to a working folder on this machine and resumes it from there, so Claude Code never writes into your synced folder in the middle of a turn. After each turn it copies the session back.

## Finding the folder

Folder paths differ between machines, so a session records its project by its git remote, its branch and the folder inside the repository. Deventry looks for a matching folder among the folders it remembers, your recent and favorite folders and your open tabs, and prefers a clone on the recorded branch. Then it tries the recorded path, in case it exists here too.

If it finds nothing, it asks *Where is the folder for "…" on this machine?* and remembers your answer for next time.

## When the code may differ

If this folder is on another branch or commit than the other machine's was, or the other machine had changes it hadn't committed, Deventry warns you first, under the heading **The code here may be different**. It says where the session was last used, on which branch and commit, and what this folder has. Claude's earlier file changes may not be here.

Choose **Continue anyway**, or cancel, push or pull the code to get it in step, and open the session again.

## One machine at a time

A tab that syncs keeps a small claim on its session in the library, refreshed every minute, so two machines don't write the same session at once. A claim that hasn't been refreshed for 10 minutes counts as stale.

If you open a session from History that another machine is actively using, Deventry asks *"…" is open on DESKTOP-01*, and offers:

- **Open a copy**, which starts a new session that begins as a copy. Both go their own way.
- **Take over**, which continues the session here. The tab on the other machine becomes read-only the next time it syncs.

A tab that was taken over stops its Claude Code, says *This session was taken over by DESKTOP-01.*, and becomes read-only. It reads *This session continued on DESKTOP-01. The tab is read-only here; to continue on this machine, close it and open the session again from History.* It doesn't start Claude Code again, and has no **Restart**. A tab restored at launch that finds its session was taken over while Deventry was closed does the same instead of starting.

A tab that doesn't sync ignores all of this.

## Coming back to the first machine

A tab that syncs, such as a pinned one, can come back to a session you carried on elsewhere while Deventry was closed here. If the other machine has since closed Deventry, the tab picks up where it left off, from the library, rather than from this machine's older copy. The conversation ends with *Picked up where LAPTOP-02 left off at 12:04.*

Your sync client may still be bringing the other machine's turns when Deventry starts, especially when it starts as you log in. So while the tab is open, it keeps looking:

- **While Claude waits**, it checks the library once a minute. When the other machine's turns arrive, the tab starts Claude Code again from them and shows them in the conversation. What you're typing stays in the composer.
- **When you send your first message** in the tab, it checks first. If the other machine's turns have arrived, your message isn't sent: the tab shows them and says *Your message wasn't sent, so you can read it again against what's new. It's still in the composer.* Send it again when you're ready.

If the session went on in both places, with turns on this machine the library never got, neither machine's turns are written over. Deventry keeps this machine's in the library as a copy of their own, which History lists under this machine's name beside the other machine's (see [Conflict copies](#conflict-copies)), and the tab becomes read-only, saying *LAPTOP-02 carried this session on too, so copying this tab's turns to the session library would lose the ones made there.* Close the tab, then open whichever you want to go on with from History.

Turning on **Sync to other machines** for a session in that state leaves it off, and says why, rather than making the tab read-only.

## Conflict copies

Sync clients sometimes make a second copy of a file when two machines change it at once, such as `session (1).jsonl`. Deventry shows these in History as separate entries, with the copy's label after the name, and never merges them. Opening one starts it as its own session in a folder of its own, so it can't overwrite the original. It recognizes the copies Google Drive, OneDrive, Dropbox and Syncthing make, and the ones Deventry makes itself when a session went on in two places ([above](#coming-back-to-the-first-machine)), labeled with this machine's name.

{: .note }
A transcript remembers file paths from the machine that wrote it. Continuing a session recorded on Windows on a Mac, or the other way round, hasn't been tried on real machines yet, so check what Claude does with the first file it touches.

## See also

- [Closing and restoring](../tabs/closing-and-restoring.md) for a pinned tab whose session Claude Code has deleted, which resumes from its library copy when there is one.
- [Settings sync](settings-sync.md) for keeping your settings the same on both machines.
