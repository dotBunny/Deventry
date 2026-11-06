---
title: Games with Joulene
parent: Joulene
nav_order: 2
summary: "While Claude works, or a usage limit holds a task, you can play a game against Joulene: tic-tac-toe, Connect Four, hangman, Minesweeper or Pipeline."
redirect_from: /claudette/games-with-claudette/
---

# Games with Joulene

While Claude works, or a usage limit holds a task, you can play a game against Joulene: tic-tac-toe, Connect Four, hangman, Minesweeper or Pipeline. The games never touch Claude Code, use no usage and reach nothing outside Joulene. A game left open stays out of the way of everything else.

## Start a game

Any of these shows the **Games** tool window:

- Click its icon, the controller, on the strip at the window's right, under the line. It's always there while games are on; with no game open, the window lists the five, each with your score so far: click one to start it.
- Right-click Joulene and choose **Play tic-tac-toe**, **Play Connect Four**, **Play hangman**, **Play Minesweeper** or **Play Pipeline**.
- Open the [command palette](../tabs/the-command-palette.md) and choose **Play tic-tac-toe with Joulene**, **Play Connect Four with Joulene**, **Play hangman with Joulene**, **Play Minesweeper with Joulene** or **Play Pipeline with Joulene**.
- When a usage limit stops a task, choose **Play a game while you wait** on the limit bar over the message box ([When a limit stops a task](../usage/when-a-limit-stops-a-task.md)).

Now and then, while Claude works or a limit holds the task, one of her tips says *Right-click me for a game while you wait*. If you've hidden her, the palette and the limit bar still work.

## One game at a time

There's one game for the whole app. It follows you from tab to tab, as she does, so the Games tool window shows it whichever tab is in front. Its icon is on the strip whenever games are on, game or no game, so you can start one at any time.

- The game's name beside the window's title, such as **Hangman ▾**, has a menu of the games: choose another to switch to it. A round in progress is dropped, and not counted.
- **Done playing** ends the game, and the window lists the games again.

## The games

### Tic-tac-toe

You're ✕ and Joulene is ○. Whoever didn't start the last round starts the next. The **Level** menu sets how well she plays:

- **Easy**: she takes a win that's in front of her, and otherwise plays anywhere.
- **Normal**: she also blocks your wins, and plays her best move half the time. This is the default.
- **Unbeatable**: she never loses.

### Connect Four

Seven columns of six. Click a column, or press its number key, to drop a piece in it. Who starts takes turns, as in tic-tac-toe. Your pieces are blue and hers are rose, and the four that win are ringed. The **Level** menu offers:

- **Easy**: she looks one move ahead, and plays anywhere a third of the time.
- **Normal**: she looks four moves ahead. This is the default.
- **Hard**: she looks six moves ahead.

### Hangman

Joulene thinks of a word, three times in four from programming (*refactor*, *compiler*, *semaphore*) and otherwise an everyday one, and tells you which and how many letters. Guess a letter by typing it while the page has focus, or by clicking the letters under the word. Six misses draw the whole figure on the gallows and lose the round, and then she shows you the word. She doesn't pick a word again until twenty others have come up. Hangman has no levels.

### Minesweeper

Joulene hides her mines in a field of nine squares by nine, and you open every square without one. She hides them as you open the first square, never in it or next to it, so it always opens onto some room. A number on an open square says how many mines touch it; a square with none around it opens those around it too.

- Click a square to open it. Open one of her mines and she wins, and shows you where the rest were; a flag where there was no mine is crossed out.
- Right-click a square, or press `F` on the one with the focus, to flag it, and again to take the flag away. Over the field it says how many mines are left to flag.
- Click a number that has as many flags around it as mines to open the rest of the squares around it at once. A flag in the wrong place opens a mine.

Open every square without a mine and you win. The **Level** menu sets how many mines she hides: **Easy** 10, **Normal** 13 (the default) or **Hard** 16. Changing it starts a new field.

### Pipeline

Joulene's tap is on a board of eight squares by eight, and in a few seconds she opens it. Lay pipe from it before then, ahead of her flow: the flow has to get through as many pipes as she asks before it spills.

- She hands out the pieces: straight ones, corners and now and then a crossing. Over the board, the next piece is ringed, with the four after it beside it.
- Click a square to lay the next piece there. The square under the pointer shows the piece faintly before you do. You can lay a piece over one the flow hasn't reached yet, in place of it, but not over her tap, her rocks, or a pipe the flow has been in.
- The ring round her tap runs down as her countdown does, and over the board it says how long is left. **Open now** opens it at once.
- The flow goes from pipe to pipe until it comes to an open end, the edge of the board, one of her rocks or a pipe that doesn't take it, and spills. A crossing takes it twice, once each way, and counts twice.
- Over the board it says how many pipes the flow is through. Once it's on its way, **Faster** hurries it till it spills.

If the flow has been through as many pipes as she asked when it spills, you win; if not, she does. The **Level** menu sets how far it has to go and how fast it goes:

- **Easy**: 10 pipes, her tap opens after 25 seconds, and each pipe takes 4 seconds to fill. No rocks.
- **Normal**: 14 pipes, 20 seconds and 3 seconds a pipe, with 2 of her rocks in the way. This is the default.
- **Hard**: 18 pipes, 15 seconds and 2.4 seconds a pipe, with 4 rocks.

Changing it starts a new board. The flow, and her countdown, wait while the Games window is hidden or Deventry is in the background, and carry on when you're back, so going off to answer Claude doesn't lose you the round.

## Her turn

She thinks for about a second before each move, with a "?" over her head on the message box. She reacts as she does to the tab ([Joulene on the composer](joulene-on-the-composer.md)): confetti when she wins, dizzy when she loses, a hop at a draw. She wipes her brow when she blocks a move that would have won, when you're down to your last miss, when you have one square left to open in Minesweeper, or when her flow reaches a pipe you laid only just in time. Your moves keep her awake.

While motion is reduced, or she's hidden, she still plays. She just doesn't react on the message box.

## The score

Each game keeps its own score, such as *You 3 · Joulene 2 · Draws 1*, and its own level, on this computer and across restarts. **Reset score** sets a game's score back to nothing.

During a round, **New game** drops it without counting it. When a round ends, **Play again** starts the next.

## When Claude needs you

A game never moves or hides anything. While a tab waits on you, a line over the board says so: *Claude needs you in auth refactor*, or *Claude needs you in 2 tabs*, with **Go there**, which goes to the next one. In the tab that's waiting, it says *Claude is waiting for you in this tab*. Her waving, the badge and notifications carry on as usual.

## Keyboard and screen readers

Each square, column and letter is a button with a name, such as *Top left, empty*, *Column 3, 2 of 6 filled*, *Letter E*, *Row 2, column 5, 1 mine around* and *Row 3, column 4, corner, top to right, full*. While the Games page has focus, the number keys play tic-tac-toe's squares (1 to 9, from the top left) and Connect Four's columns (1 to 7), and the letters guess in hangman. In Minesweeper, the arrow keys move between the squares, `Space` or `Enter` opens the one with the focus and `F` flags it. In Pipeline, the arrow keys move between the squares, the one with the focus shows the next piece, and `Space` or `Enter` lays it there. None of it reaches the message box. Her moves and every result are read out, such as *Joulene took the middle.*; in Pipeline, so are the piece you laid and the next one.

## Settings

| Setting | Default | Where |
|---|---|---|
| **Games with Joulene** | On | Settings → Appearance |

Turning it off removes every way in and ends a game that's open. It works whether or not **Show Joulene on the composer** is on.

## Shortcuts

| | Keys |
|---|---|
| Tic-tac-toe square | `1` to `9`, from the top left |
| Connect Four column | `1` to `7` |
| Hangman letter | The letter, while the page has focus |
| Minesweeper: move between squares | Arrow keys |
| Minesweeper: open a square | `Space` or `Enter` |
| Minesweeper: flag a square | `F` |
| Pipeline: move between squares | Arrow keys |
| Pipeline: lay the next piece | `Space` or `Enter` |

## See also

- [Joulene on the composer](joulene-on-the-composer.md), for her right-click menu and what she does the rest of the time.
- [Tool windows](../tabs/tool-windows.md), for the other windows.
