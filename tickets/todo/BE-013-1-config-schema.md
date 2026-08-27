# BE-013-1 — Named config JSON schema

| Field | Value |
| --- | --- |
| Folder | todo |
| Blocked by | BE-007 |
| Next | BE-013-2 |

## Expected feature

On disk: several named loadouts, e.g. `{ "configs": [ { "id", "name", "maps", "music" } ] }`. No UI yet. Document the schema in a short comment or `mod-apply.mdc`.

## Tasks

- [ ] File format next to loadout.json
- [ ] Read/write helpers only

## Acceptance

- [ ] Can save two configs in JSON by calling C# (even from a tiny test/IPC later)
