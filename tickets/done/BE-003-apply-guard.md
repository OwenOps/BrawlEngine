# BE-003 — Warn if Brawlhalla is running

| Field | Value |
| --- | --- |
| Folder | done |
| Rule | `.cursor/rules/apply-guard.mdc` |

## Expected feature

If Brawlhalla is open, a message in the shell: close the game before changing files. The app **never** kills the game. Later Apply/Reset must reuse `BrawlhallaProcess.Check()`.

## What shipped (learn)

- `Process.GetProcessesByName("Brawlhalla")`.
- IPC `game.running` polled every 2s.

## Files

- `host/Game/BrawlhallaProcess.cs`
