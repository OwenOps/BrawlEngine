# BE-015-1 — Non-blocking HTTP / apply

| Field | Value |
| --- | --- |
| Folder | todo |
| Blocked by | feel freeze on download/apply first |

## Expected feature

While a download or apply runs, the window still paints (tabs, scroll). Today `GetAwaiter().GetResult()` on the Photino thread can freeze the UI.

## Learn

- `Task.Run` + `SendWebMessage` when finished, or progress IPC.

## Tasks

- [ ] Catalog/download/apply do not block the UI thread for the whole request

## Acceptance

- [ ] You can move the window / switch tabs during a download
