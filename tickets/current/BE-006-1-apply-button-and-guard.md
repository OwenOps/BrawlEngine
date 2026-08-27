# BE-006-1 — Apply button + refuse if game open

| Field | Value |
| --- | --- |
| Folder | **current** |
| Blocked by | BE-005, BE-003, BE-002 |
| Next | BE-006-2 |
| Rules | `.cursor/rules/apply-guard.mdc`, `.cursor/rules/mod-apply.mdc` |

## Expected feature

Each Maps card gets **Apply** (enabled after a successful download, or always visible but it fails if there is no download). Clicking Apply:

- If Brawlhalla is **running** → English error, **no files written**.
- If the game is closed but extract is not built yet → you may return a clear “not implemented” **or** call a stub that only runs the guard. Prefer: IPC `mod.apply` `{ id }` that **only** checks the guard + “download folder exists”, and returns ok/error. Real copy is BE-006-3.

Do **not** extract zips or backup in this ticket.

## Learn

- Reuse `BrawlhallaProcess.Check()` and `BrawlhallaLocator.Resolve()`.
- Keep Apply next to Download on the card.
- One IPC now so 006-2/006-3 can fill the same handler.

## Tasks

- [ ] Apply button on Maps cards
- [ ] IPC `mod.apply` `{ id }`
- [ ] If `running` → `ok: false`, game files untouched
- [ ] If no download folder → `ok: false`, English message
- [ ] If closed + folder exists → `ok: true` with payload `{ applied: false, reason: "extract not implemented" }` **or** wait and implement 006-3 immediately after — still no zip extract in **this** file’s scope
- [ ] `npx ng build` + `dotnet build`

## Acceptance

- [ ] Game open + Apply → error, `mapArt` unchanged
- [ ] Game closed + no download → error about missing files
- [ ] No zip extract yet

## Files

- `host/Presentation/Ipc/IpcRouter.cs`
- `ui/src/app/features/maps/`
