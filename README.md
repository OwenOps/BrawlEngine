# BrawlEngine

Windows desktop app to browse [GameBanana](https://gamebanana.com/games/5704) Brawlhalla mods and apply them: **maps (realms)**, **skins**, and **audio**.

I built it because I wanted something that looks like a modern app, and because installing mods by hand was getting in the way — especially **skins** and **map art**. Download, Apply, keep a vanilla backup, restore when you need ranked. That is the whole point.

Unofficial. Not affiliated with Blue Mammoth Games.

## Screenshots

<img src="docs/screenshots/maps.png" alt="Maps tab" width="720">

<img src="docs/screenshots/skins.png" alt="Skins tab" width="720">

<img src="docs/screenshots/audio.png" alt="Audio tab" width="720">

## What it does

- Lists GameBanana Realms, Legend Skins, and Sounds (catalog can be slow; that is their API).
- Downloads zip / rar / 7z into a **Mods** folder (`Maps`, `sounds`, `skins`).
- **Apply** writes into your Brawlhalla install after a one-time vanilla backup. If the game is open, Apply waits until it closes.
- **Reset** one pack or everything. **Ranked / safe** restores map art and skin SWFs and leaves audio.
- Add a zip from disk or a GameBanana URL if you would rather skip the catalog.

It is **not** Brawlhalla Mod Loader. That tool installs `.bmod` packs from Mod Creator. This one is for GameBanana archives and applying them to the game files.

## Requirements

- Windows 10 / 11
- Steam Brawlhalla
- **Java 11+** on the machine for skins (JPEXS `ffdec_lib` is fetched automatically)

Maps and most audio packs do not need Java.

## Install

No installer. Pack a zip with `scripts\pack-release.ps1`. Extract **the whole folder**. You get `BrawlEngine.lnk` next to `app\` — double-click the shortcut (do not open `app\` to hunt among DLLs).

Downloaded maps / skins / audio are **not** in the zip. They live under `%LocalAppData%\BrawlEngine` (or the Mods folder you pick). If a new PC already shows On disk cards, that folder still has old files — they did not come from the zip.

Windows may say **Unknown publisher**. That is an unsigned exe, not a virus scan by itself. More info → Run anyway. If SmartScreen bothers you, download the zip from [GitHub Releases](https://github.com/OwenOps/BrawlEngine/releases) and check [VirusTotal](https://www.virustotal.com) yourself.

## Ranked / Easy Anti-Cheat

- **Map art / backgrounds** — usually fine with EAC on.
- **Skins** — typically need `-noeac`. Ranked will not work with those files still in place.
- Use **Ranked / safe** before you queue, or **Reset all** if you want vanilla everything.

Cosmetic mods are a community thing. Tournaments have their own rules. This app does not bypass anti-cheat.

## Audio

**Custom audio** (replace a theme with your own MP3) is **hidden for now**. Brawlhalla dropped the old `mp3` files; converting to `.wem` did not change what the game plays, so that UI is off until there is a real fix.

Old **Sound.swf** UI / weapon packs cannot Apply — those files are gone from the game. A GameBanana pack is only copied in when its filenames already match vanilla `.wem` / `.bnk`.

## Build from source

```powershell
cd ui
npx ng build
cd ..\host
dotnet publish -c Release -r win-x64 --self-contained true -p:DebugType=None -o ..\dist\BrawlEngine
```

Do not ship `bin\Release` — that build needs the .NET 10 runtime on the PC.

### GitHub Release (zip)

From the repo root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\pack-release.ps1
```

That builds the UI, publishes self-contained, **strips Maps / sounds / skins / mods** if they snuck into the publish folder, writes `BrawlEngine.lnk` next to `app\`, and zips `dist\BrawlEngine-<version>-win-x64.zip`.

Bump `<Version>` in `host/BrawlEngine.Host.csproj` before the next tag (`v0.6.1`, …) so the in-app update check can see it.

## Credits

BrawlEngine is [MIT](LICENSE). Third-party tools keep their own licenses: skin apply uses [JPEXS `ffdec_lib`](https://github.com/jindrapetrik/jpexs-decompiler) (LGPLv3); `wav2wem.exe` is GPLv2 from [pas2k/wav2wem](https://github.com/pas2k/wav2wem), run as a separate process (see `host/tools/NOTICE.txt`). Mods come from [GameBanana](https://gamebanana.com/games/5704).
