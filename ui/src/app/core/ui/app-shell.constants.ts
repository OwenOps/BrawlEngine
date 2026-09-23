export const APP_SHELL_TEXT = {
  hostChecking: 'Checking host…',
  gameLooking: 'Looking for Brawlhalla…',
} as const;

/** GameBanana Index is often slow; keep this on the catalog spinner. */
export const GAMEBANANA_WAIT = 'GameBanana is slow. This can take a minute…';

/** First launch only (localStorage). Explains the catalog wait and the zip workaround. */
export const CATALOG_HINT_STORAGE_KEY = 'brawlengine.sawCatalogHint';
export const CATALOG_HINT = {
  title: 'GameBanana is slow',
  body: "Listing mods from the catalog can take a minute. That is GameBanana's API, not this app hanging. If that gets in the way, download the zip on the site, then Add zip here, or drop it into the mods folder (name the folder with the GameBanana id).",
} as const;

/** On disk / Applied / Liked read item.json — no catalog wait copy. */
export const LIBRARY_WAIT = 'Loading library…';

/** Skin Reset restores SWFs then reapplies the others — no percent from the host. */
export const RESET_WAIT = 'Resetting… This can take a minute.';

export const GAMEBANANA_GAME_URL = 'https://gamebanana.com/games/5704';

/** Adoptium JRE for Windows — skins Apply needs java.exe. */
export const JAVA_DOWNLOAD_URL = 'https://adoptium.net/temurin/releases/?os=windows&package=jre';

export const SOURCE_REPO_URL = 'https://github.com/OwenOps/BrawlEngine';

/** Discord username (not a server invite). Click copies it. */
export const CONTACT_DISCORD = 'owenops';

export const UPDATE_DISMISS_STORAGE_KEY = 'brawlengine.dismissedUpdate';