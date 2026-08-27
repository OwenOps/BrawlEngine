# BE-005 — Download zips

| Field | Value |
| --- | --- |
| Folder | done |
| Rule | `.cursor/rules/download.mdc` |

## Expected feature

Each Maps card has **Download**. Files land in `%LocalAppData%\BrawlEngine\downloads\{modId}\`. The game folder is **not** changed. Apply is later (BE-006-*).

## What shipped (learn)

- `Mod/{id}/ProfilePage` → `_aFiles` → HTTP URLs (skip `bmod://`).
- Delete partial files on failure.

## Files

- `GameBananaClient.DownloadModAsync`
