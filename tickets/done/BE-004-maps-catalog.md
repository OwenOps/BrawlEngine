# BE-004 — Maps catalog

| Field | Value |
| --- | --- |
| Folder | done |
| Rule | `.cursor/rules/maps-catalog.mdc` |

## Expected feature

The **Maps** tab shows a grid of Brawlhalla **Realms** from GameBanana: image, title, author, **Load more**. Browsing works without downloading.

## What shipped (learn)

- Game id **5704** (not 5723). Category **6463** Realms.
- HTTP only in C#. IPC `catalog.maps`.

## Files

- `host/Catalog/GameBananaClient.cs` — `ListRealmsAsync`
- `ui/src/app/pages/maps.page.ts`
