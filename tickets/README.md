# Ticket board

Three folders (move the file when the state changes):

| Folder | Meaning |
| --- | --- |
| [`done/`](done/) | Shipped — read to learn |
| [`current/`](current/) | **One** ticket you work on now |
| [`todo/`](todo/) | Not started, or blocked |

When you finish `current/`, move it to `done/`, then move the next `todo/` file into `current/`.

## Expected feature

Every ticket has **Expected feature**: what the player (or you) should see when it is done. If that is too big, it is already split into BE-xxx-1, -2, …

## Board

| ID | Title | Folder |
| --- | --- | --- |
| [BE-001](done/BE-001-scaffold.md) | Scaffold Photino + Angular | done |
| [BE-002](done/BE-002-game-location.md) | Find Brawlhalla folder | done |
| [BE-003](done/BE-003-apply-guard.md) | Warn if game running | done |
| [BE-004](done/BE-004-maps-catalog.md) | Maps catalog | done |
| [BE-005](done/BE-005-download.md) | Download zips | done |
| [BE-006-1](current/BE-006-1-apply-button-and-guard.md) | Apply button + refuse if game open | **current** |
| [BE-006-2](todo/BE-006-2-backup-vanilla.md) | Backup vanilla files once | todo |
| [BE-006-3](todo/BE-006-3-extract-and-copy.md) | Extract zip → mapArt | todo |
| [BE-007](todo/BE-007-loadout.md) | One current loadout JSON | todo |
| [BE-008](todo/BE-008-reset-all.md) | Reset all | todo |
| [BE-009](todo/BE-009-reapply.md) | Reapply loadout | todo |
| [BE-010-1](todo/BE-010-1-discover-music.md) | Discover GB + disk audio paths | todo |
| [BE-010-2](todo/BE-010-2-music-catalog.md) | Musiques catalog UI | todo |
| [BE-010-3](todo/BE-010-3-music-apply.md) | Download + apply music | todo |
| [BE-011-1](todo/BE-011-1-choose-downloader.md) | Choose custom-audio tool | todo |
| [BE-011-2](todo/BE-011-2-url-to-game.md) | Paste URL → replace audio | todo |
| [BE-012-1](todo/BE-012-1-define-ranked.md) | Define what “risky” means | todo |
| [BE-012-2](todo/BE-012-2-ranked-action.md) | Ranked/safe button | todo |
| [BE-013-1](todo/BE-013-1-config-schema.md) | Named config JSON | todo |
| [BE-013-2](todo/BE-013-2-config-ipc.md) | Save / load / delete IPC | todo |
| [BE-013-3](todo/BE-013-3-config-ui.md) | Named config list UI | todo |
| [BE-014](todo/BE-014-ui-polish.md) | Visual polish | todo |
| [BE-015-1](todo/BE-015-1-nonblocking-ipc.md) | Don’t freeze UI on HTTP | todo |
| [BE-015-2](todo/BE-015-2-lazy-thumbnails.md) | Lazy thumbnails | todo |
| [BE-015-3](todo/BE-015-3-virtual-scroll.md) | Virtual scroll if needed | todo |

## How to close

1. Read **Expected feature**, then the `.mdc` rules (do not reopen decisions).
2. C# for files/HTTP; Angular for UI + `IpcService.request`.
3. `npx ng build` in `ui/`, then `dotnet build`.
4. Move the file `current/` → `done/`, next `todo/` → `current/`. Update `ROADMAP.md`.

English in tickets and code. Chat can stay French.
