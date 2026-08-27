# BE-006-2 — Backup vanilla files once

| Field | Value |
| --- | --- |
| Folder | todo |
| Blocked by | BE-006-1 |
| Next | BE-006-3 |
| Rule | `.cursor/rules/mod-apply.mdc` |

## Expected feature

Before any game file is overwritten, the app copies the **vanilla** file to a backup tree, e.g. `%LocalAppData%\BrawlEngine\backups\mapArt\...` with the same relative path. If that backup **already exists**, do **not** overwrite it (or you would save a modded file as “vanilla”).

This ticket can be a C# helper `BackupIfMissing(gameFile, backupFile)` used by BE-006-3. No need for a new UI.

## Learn

- `File.Copy` + `Directory.CreateDirectory`.
- Never backup after the file was already replaced.

## Tasks

- [ ] Helper in e.g. `host/Apply/VanillaBackup.cs`
- [ ] Unit-of-work: given a target path under `mapArt`, backup once
- [ ] Do not extract zips here

## Acceptance

- [ ] First backup creates the file under `backups\`
- [ ] Second call leaves the first backup unchanged
