# BE-001 — Scaffold Photino + Angular

| Field | Value |
| --- | --- |
| Folder | done |
| Rule | `.cursor/rules/scaffold.mdc` |

## Expected feature

A **Windows desktop app** (not a website): a native window titled BrawlEngine. Inside it, a local Angular UI with **Maps** and **Musiques** tabs. The C# host and the UI can exchange a JSON `ping` / `pong`. No GameBanana and no game files yet.

## What shipped (learn)

- Photino loads `host/wwwroot/index.html` from disk.
- Hash routes + `<base href="./">` because this is `file://`, not a server.
- IPC: `{ id, type, ok, error?, payload? }`.

## Files

- `host/Program.cs`, `host/Ipc/IpcRouter.cs`
- `ui/src/app/ipc/ipc.service.ts`
