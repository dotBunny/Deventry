---
title: Alerts
parent: Usage and limits
nav_order: 7
summary: "Deventry tells you when your session is getting full, so you don't have to watch the header."
---

# Alerts

Deventry tells you when your session is getting full, so you don't have to watch the header.

## When an alert comes

- **The session crosses a warning level.** One alert when it passes the warning level (75% by default), and one when it passes the alert level (90%). If it jumps past both, that's one alert. The title reads *Session usage passed 75%*, and the message says *You've used 76% of your session limit. It resets in 2h 14m.*
- **You're on pace to hit the limit.** When the projection first says you'll reach the limit before the session resets: *On pace to hit the session limit*, with the same line the header shows.
- **A limit resets.** When any limit's window starts over and its percentage drops: *Session limit reset*, *Weekly limit reset*, or a model's, with the new percentage.
- **Usage credits start paying.** When a limit is used up and your plan's usage credits start paying for Claude: *Using usage credits*, with the same line as [the notice under the header](the-usage-header.md#when-usage-credits-pay). Once each time they start.

Each alert comes once per window. The first reading after Deventry starts doesn't alert for levels you'd already crossed, or for usage credits already paying.

## Where they show

- **A line under the header**, with **Dismiss**. It keeps itself up to date, so it never disagrees with the header:
  - A warning level's line keeps up with the session meter, and goes once the session resets.
  - The projection's keeps up with the projection, and goes once you're no longer on pace to hit the limit.
  - A reset's keeps up with the limit that reset.
- **The usage credits notice** instead, for usage credits. Once they start paying, the session's warning and projection lines give way to it, since it says the limit is used up. See [When usage credits pay](the-usage-header.md#when-usage-credits-pay).
- **An OS notification**, when Deventry isn't in front. Clicking it opens [the Usage window](the-usage-window.md).

[Notifications](../away/notifications.md) explains how notifications work on each system.

## Turning them off

Settings → Notifications has a switch, **Usage crosses a warning level, is on pace to run out, or resets**, that controls the OS notification. The line under the header still shows.

The levels themselves are **Warn at (% of the session used)** and **Alert at (% of the session used)** in Settings → Usage. They also set when the meters turn amber and red.

## Spend warnings

An API-key account can warn when its estimated spend passes an amount you set. Those use the same notification switch: see [Spend and its warnings](../accounts/spend-and-its-warnings.md).

## Settings

| Setting | Default | Where |
|---|---|---|
| **Usage crosses a warning level, is on pace to run out, or resets** | On | Settings → Notifications |
| **Warn at (% of the session used)** | 75 | Settings → Usage |
| **Alert at (% of the session used)** | 90 | Settings → Usage |

## See also

- [When a limit stops a task](when-a-limit-stops-a-task.md) for what happens when you do hit one.
- [The usage header](the-usage-header.md) for the colors and the projection.
