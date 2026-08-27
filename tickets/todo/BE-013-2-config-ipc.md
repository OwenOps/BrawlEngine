# BE-013-2 — Named config IPC

| Field | Value |
| --- | --- |
| Folder | todo |
| Blocked by | BE-013-1 |
| Next | BE-013-3 |

## Expected feature

IPC: list / save-current-as-name / load-by-id / delete. Loading a config reapplies it (reuse BE-009). No pretty UI yet (013-3).

## Tasks

- [ ] `configs.list`, `configs.save`, `configs.load`, `configs.delete` (names can vary)
- [ ] Load = apply that loadout with game closed

## Acceptance

- [ ] Two configs round-trip via IPC
