# BE-006-3 — Extract zip and copy into mapArt

| Field | Value |
| --- | --- |
| Folder | todo |
| Blocked by | BE-006-1, BE-006-2 |
| Rule | `.cursor/rules/mod-apply.mdc`, `.cursor/rules/download.mdc` |

## Expected feature

With the game **closed**, Apply on a downloaded Realm:

1. Prefer the copy-pastable `.zip` if several files exist (not only the Bmod zip).
2. Extract to a **temp** folder (`ZipFile.ExtractToDirectory`). v1: zip only; if RAR, English error.
3. For each extracted file that belongs in `mapArt`, call backup (BE-006-2), then overwrite `{game}\mapArt\...`.
4. A pack may replace **several** files — apply all of them.
5. Delete the extract temp. Keep the downloaded zips for later Reapply.
6. UI: success or error on the card.

Do not save loadout JSON (BE-007). Do not Reset all.

## Learn

- `System.IO.Compression.ZipFile`.
- Match zip layout to `mapArt` (inspect a real Realm zip once).
- Fill in the `mod.apply` handler from BE-006-1.

## Tasks

- [ ] `ModApplier` in `host/Infrastructure/Apply/` used by `mod.apply`
- [ ] Guard + resolve game path + backup + copy
- [ ] Clean extract temp always
- [ ] `npx ng build` + `dotnet build`

## Acceptance

- [ ] Game closed → `mapArt` files change; backups exist
- [ ] Game open → still refused (006-1)
- [ ] Second mod does not overwrite the first vanilla backup
