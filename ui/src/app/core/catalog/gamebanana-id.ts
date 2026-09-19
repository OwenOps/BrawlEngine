import { DownloadKind } from '../ipc/contracts/download.contracts';

export type AddModMode = 'zip' | 'url';

export function gameBananaBucket(kind: DownloadKind): 'mods' | 'sounds' {
  return kind === 'sounds' ? 'sounds' : 'mods';
}

export function gameBananaExampleUrl(kind: DownloadKind): string {
  return kind === 'sounds'
    ? 'https://gamebanana.com/sounds/12345'
    : 'https://gamebanana.com/mods/12345';
}

/** GameBanana mods/sounds URL, or a plain numeric id. */
export function readGameBananaItemId(
  raw: string,
  kind: DownloadKind,
): { id: number } | { error: string } {
  const text = raw.trim();
  if (!text) {
    return { error: 'Paste a GameBanana URL or id.' };
  }

  const asNumber = Math.trunc(Number(text));
  if (Number.isInteger(asNumber) && asNumber > 0 && String(asNumber) === text) {
    return { id: asNumber };
  }

  let uri: URL;
  try {
    uri = new URL(text);
  } catch {
    return { error: 'Need a gamebanana.com link or a number.' };
  }

  const host = uri.hostname.toLowerCase();
  if (host !== 'gamebanana.com' && host !== 'www.gamebanana.com') {
    return { error: 'Need a gamebanana.com link or a number.' };
  }

  const parts = uri.pathname.split('/').filter((part) => part.length > 0);
  if (parts.length < 2) {
    return { error: 'Need a GameBanana mods or sounds page.' };
  }

  const bucket = parts[parts.length - 2].toLowerCase();
  const expect = gameBananaBucket(kind);
  if (bucket === 'mods' && expect === 'sounds') {
    return { error: 'That URL is a mod. Open Maps or Skins.' };
  }
  if (bucket === 'sounds' && expect === 'mods') {
    return { error: 'That URL is a sound. Open the Audio tab.' };
  }
  if (bucket !== 'mods' && bucket !== 'sounds') {
    return { error: 'Need a GameBanana mods or sounds page.' };
  }

  const id = Math.trunc(Number(parts[parts.length - 1]));
  if (!Number.isInteger(id) || id <= 0) {
    return { error: 'Need a GameBanana URL or id.' };
  }

  return { id };
}
