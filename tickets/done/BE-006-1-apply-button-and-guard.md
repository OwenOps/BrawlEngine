# BE-006-1 — Apply button + refuse if game open

| Field | Value |
| --- | --- |
| Folder | done |
| Next | BE-006-2 |

## Expected feature

Each Maps card has **Apply**. If Brawlhalla is running → English error, no game files written. If not downloaded → error. If closed and files exist → success payload `{ applied: false }` (extract is BE-006-3).

## What shipped

- IPC `mod.apply` `{ id }`
- `ModApplyService.TryPrepare` — guard + download folder check, no extract
- Maps **Apply** button (signals / OnPush)

## Files

- `host/Infrastructure/Apply/ModApplyService.cs`
- `host/Presentation/Ipc/IpcRouter.cs`
- `ui/src/app/features/maps/maps-page.component.ts`
