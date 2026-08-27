# BE-007 — One current loadout

| Field | Value |
| --- | --- |
| Folder | todo |
| Blocked by | BE-006-3 |
| Rule | `.cursor/rules/mod-apply.mdc` |

## Expected feature

The app remembers **one** “what is applied now” (e.g. which Realm mod ids). After restart, that info is still there. **Not** several named presets (BE-013).

Example: `%LocalAppData%\BrawlEngine\loadout.json` with `{ "maps": [{ "modId": 123 }], "music": [] }`. Update it after a successful Apply.

Optional: a small “Active” label on the matching Maps card.

## Tasks

- [ ] JSON schema + read/write
- [ ] Write after successful apply
- [ ] Read on startup

## Acceptance

- [ ] Restart app → last applied maps are known
- [ ] No “Config 1 / Config 2”
