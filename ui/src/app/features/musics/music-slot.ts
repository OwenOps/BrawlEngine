export type MusicSlot = 'menu' | 'select' | 'victory' | 'combat' | 'other';

export const MUSIC_SLOTS: ReadonlyArray<{ id: MusicSlot; label: string }> = [
  { id: 'menu', label: 'Menu' },
  { id: 'select', label: 'Character select' },
  { id: 'victory', label: 'Victory' },
  { id: 'combat', label: 'Combat / map themes' },
  { id: 'other', label: 'Other' },
];

export function slotForTrackFile(fileName: string): MusicSlot {
  const base = fileName.replace(/\.mp3$/i, '');
  if (/^BrawlhallaCharacterSelect/i.test(base)) {
    return 'select';
  }
  if (/^BrawlhallaWinTheme/i.test(base)) {
    return 'victory';
  }
  if (/^BrawlhallaMenu/i.test(base)) {
    return 'menu';
  }
  if (/^BrawlhallaTheme/i.test(base)) {
    return 'combat';
  }
  return 'other';
}

export function asMusicSlot(value: string): MusicSlot {
  if (
    value === 'menu' ||
    value === 'select' ||
    value === 'victory' ||
    value === 'combat' ||
    value === 'other'
  ) {
    return value;
  }

  return 'other';
}

export function slotHint(slot: MusicSlot): string {
  switch (slot) {
    case 'menu':
      return 'Plays in the main menu.';
    case 'select':
      return 'Plays on the character select screen.';
    case 'victory':
      return 'Plays after a match.';
    case 'combat':
      return 'Plays during fights (map / combat themes).';
    default:
      return 'Other in-game track (event or extra file).';
  }
}
