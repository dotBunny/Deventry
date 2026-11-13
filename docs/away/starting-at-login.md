---
title: Starting at login
parent: Away from your desk
nav_order: 6
summary: "Deventry can start by itself when you log in to your computer, with your tabs back where they were, so a task you left for tomorrow is a click away."
---

# Starting at login

Deventry can start by itself when you log in to your computer, with your tabs back where they were, so a task you left for tomorrow is a click away.

## Turning it on

Turn on Settings → General → **Start Deventry when I log in**. It's off by default.

It lives in the operating system rather than in Deventry's settings, so it's for this machine alone, it doesn't [sync](../history/settings-sync.md), and **Reset to defaults** leaves it.

At login Deventry opens minimized to the taskbar or Dock, without taking focus, and restores your tabs as on any launch (see [Closing and restoring](../tabs/closing-and-restoring.md)). If the window was maximized, it comes back maximized. If Deventry is already running, nothing happens.

## How each system does it

- **Windows, installed from the package (MSIX):** a startup task, listed as Deventry in Task Manager's Startup apps.
- **Other Windows builds:** a `Deventry` entry in your Run list.
- **macOS:** a login item that runs once at login. It runs on macOS 12 too.
- **Linux:** a `deventry.desktop` file in your autostart folder (`~/.config/autostart`, or `$XDG_CONFIG_HOME/autostart`).

## Which copy starts

A machine can have an installed release, another build, and builds from a source checkout. The entry starts the best one: an installed release first, then another build, then a source build.

- Turning it on from a source build starts the installed release when there is one, and the page says so: *It starts the installed Deventry 1.0.0 rather than this source build.*
- A source build's entry starts its newest build ([Running from source](../getting-started/running-from-source.md)). Once Deventry is installed, the installed one starts instead.
- On macOS and Linux, a build that runs on a .NET in your home folder, such as the one Microsoft's install script puts in `~/.dotnet`, is started with that .NET's `dotnet`. Its own program looks for .NET only in the usual places, and nothing at login tells it otherwise.
- A Deventry started at login that finds a better copy installed hands over to it and exits.
- When Deventry starts, it tidies the entry without turning it on or off. An entry whose copy has gone, such as a deleted checkout or an uninstalled release, starts the best copy that's left.

On Windows, only the installed package can turn its own startup task on or off. While that task is on, the switch in a source build is on but disabled: *The installed Deventry 1.0.0 starts at login. Turn it off in that Deventry, or in Task Manager's Startup apps.*

## When it's turned off somewhere else

Only you can turn some of these back on, so the switch shows them off and disabled, and says where:

- **Windows:** Task Manager's Startup apps can turn off the package's startup task or the Run entry. An organization's policy can hold the task on or off, and the switch says which.
- **Linux:** a `deventry.desktop` file with `Hidden=true` or `X-GNOME-Autostart-enabled=false` is off.
- **macOS:** you can turn off the login item under **Allow in the Background** in System Settings → General → Login Items. Deventry doesn't read that, so its switch can show on while macOS holds it off.

## Trying things with DEVENTRY_HOME

A Deventry started with the `DEVENTRY_HOME` environment variable, which keeps its settings and tabs in a folder of its own, leaves the entry alone and disables the switch, since the entry would start Deventry without it.

## Settings

| Setting | Default | Where |
|---|---|---|
| **Start Deventry when I log in** | Off | Settings → General |

## See also

- [Keeping the computer awake](keeping-the-computer-awake.md), for tasks that must outlast a sleep.
- [Windows, macOS and Linux](../reference/windows-macos-and-linux.md).
