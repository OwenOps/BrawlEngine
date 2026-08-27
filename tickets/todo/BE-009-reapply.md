# BE-009 — Reapply current loadout

| Field | Value |
| --- | --- |
| Folder | todo |
| Blocked by | BE-007 |
| Rule | `.cursor/rules/mod-apply.mdc` |

## Expected feature

After Steam overwrites `mapArt`, **Reapply** puts the **current loadout** back. **Manual button only** — not on app start.

Use cached downloads if present; otherwise download again, then same apply/backup as BE-006-3. Game must be closed.

## Tasks

- [ ] IPC `mods.reapply`
- [ ] Shell **Reapply** button
- [ ] No auto-run on startup

## Acceptance

- [ ] Simulate a Steam wipe → Reapply restores the loadout
- [ ] Starting the app does nothing to game files
