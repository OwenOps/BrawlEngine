export type AppTabId = 'maps' | 'musics' | 'skins';

export interface AppTabDef {
  id: AppTabId;
  label: string;
  icon: string;
}

/** All three pages stay mounted in app.component.html ([hidden] toggling) so filters survive tab switches. */
export const APP_TABS: ReadonlyArray<AppTabDef> = [
  { id: 'maps', label: 'Maps', icon: '🗺' },
  { id: 'musics', label: 'Audio', icon: '🎵' },
  { id: 'skins', label: 'Skins', icon: '⚔' },
];
