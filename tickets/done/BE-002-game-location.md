# BE-002 — Find Brawlhalla folder

| Field | Value |
| --- | --- |
| Folder | done |
| Rule | `.cursor/rules/game-location.mdc` |

## Expected feature

On launch, the app shows the Brawlhalla install path (or “not found”). **Choose folder** lets the player pick the folder that contains `Brawlhalla.exe`. The path is remembered next time.

## What shipped (learn)

- Steam registry + `libraryfolders.vdf`, then picker.
- `%LocalAppData%\BrawlEngine\settings.json`.
- Warn if `mapArt` is missing; still accept the folder.

## Files

- `host/Game/BrawlhallaLocator.cs`, `AppSettings.cs`
- `ui/src/app/app.component.ts`
