export type AppTabId = 'maps' | 'musics' | 'skins' | 'stats';

export interface AppTabDef {
  id: AppTabId;
  label: string;
  icon: string;
}

/** Visited pages stay mounted in app.component.html ([hidden] toggling) so filters survive tab switches. */
export const APP_TABS: ReadonlyArray<AppTabDef> = [
  { id: 'maps', label: 'Maps', icon: '🗺' },
  { id: 'skins', label: 'Skins', icon: '⚔' },
  { id: 'stats', label: 'Stats', icon: '👤' },
  { id: 'musics', label: 'Audio', icon: '🎵' },
];
