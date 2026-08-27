# BE-015-2 — Lazy thumbnails

| Field | Value |
| --- | --- |
| Folder | todo |
| Blocked by | BE-004 |

## Expected feature

Grid images load as you scroll (`loading="lazy"` and/or smaller thumbs). Catalog still shows titles if an image fails.

## Tasks

- [ ] Lazy `<img>` (and optional C# thumb cache later)

## Acceptance

- [ ] Opening Maps does not fetch every huge image at once if the grid is long
