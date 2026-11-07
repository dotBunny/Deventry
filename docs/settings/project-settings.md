---
title: Project settings
parent: Settings
nav_order: 2
summary: "Each project has its settings in a window of its own: its links, its actions, this computer's choices for it, and how its group looks in the sidebar."
---

# Project settings

What belongs to a project rather than to Deventry is in a window of its own, one for each project's folder: its web links, its own actions, its solution, the choices this computer remembers for it, and how its group looks in the sidebar.

## Opening them

- Right-click a group's label in the sidebar, or its color in the rail, and choose **Project settings…**. It works for a group with nothing open in it too, such as one of [Claude Code's projects](../tabs/folder-groups.md#claude-codes-projects).
- Right-click a tab and choose **Project settings…**, after **Tab settings…**, for the tab's folder.
- **Open** beside **Project actions** in [Tab settings…](../tabs/tab-settings.md) opens it at **Actions**.

The window opens beside the main window, which you can keep using. Each project has one: asking for it again brings it to the front at the page you asked for. Press `Esc`, or `Ctrl/Cmd+W`, to close it.

Its title, and the top of its sidebar, name the project, or the folder when Deventry found no project in it, after a dot in the color of its group, or the project's icon in its place. The folder's path is under the name. The pages are listed under them.

## The pages

- **Links** lists the project's web links, from `deventry.json` and `deventry.local.json`, with **Add…**, **Edit…**, **Remove**, **Move up** and **Move down**. Its dialog has each link's **Group** and **Icon** in the project's menu.
- **Actions** is the same for the project's own actions, with **Open file**, and the same **Group** and **Icon**.
- **Solution** is the file that defines the project, usually its solution (`NightOwl.sln`), in either file, with **Choose…** and **Remove**. The project's menu then has **Open NightOwl.sln**: a solution or workspace opens with the app Settings → Project tools → All tools → **Open solutions with** says, your system's own by default, and any other file with the app your system has for it. It sits beside an Unreal, Unity or Godot project's own **Open solution**, and in a folder without one of those it's the project's main action (`Ctrl/Cmd+Shift+E`). The page says what the menu does with it, or why it can't open it.
- **Tools** holds this machine's choices for the project: which project file when the folder has several, its editor configuration or code optimization, and the engine folder, Unity editor or Godot executable you picked, each with **Clear** to forget the pick. Without a project it says "No Unreal, Unity or Godot project in this folder". The defaults every project starts with are on its kind's page in Settings → Project tools, which **Open Unreal Engine** (or Unity, or Godot) at the foot of the page opens; without a project, **Open All tools**.
- **Group** is how the project's group looks in the sidebar, in either file: its color, with **Choose…** and **Remove**, and the picture in place of its dot, with **Choose…** and **Remove**. See [Changing a group's color](../tabs/folder-groups.md#changing-a-groups-color) and [A group's icon](../tabs/folder-groups.md#a-groups-icon).

Each change is saved as you make it, to the file it belongs to, and the project's menu follows at once. Saving rewrites the file, so comments in it are dropped. A file that isn't valid JSON is never rewritten: the page shows why, and you fix the file by hand. [Actions and links](../project-tools/actions-and-links.md) explains what they do.

None of this is in Settings, and nothing on these pages has **Reset to defaults**: they're the project's, not Deventry's settings.
