# BE-008 — Reset all

| Field | Value |
| --- | --- |
| Folder | todo |
| Blocked by | BE-006-2 (backups), BE-006-3 |
| Rule | `.cursor/rules/mod-apply.mdc` |

## Expected feature

A **Reset all** button restores **every** backed-up file into the game (full vanilla). Game must be closed. This is **not** Ranked/safe (BE-012).

After success, clear or empty the loadout (BE-007) if it exists.

## Tasks

- [ ] IPC `mods.resetAll`
- [ ] Shell button
- [ ] Guard + copy backups back

## Acceptance

- [ ] `mapArt` matches backups (vanilla)
- [ ] Distinct from a future Ranked button
