import { Type } from '@angular/core';
import { MapsPage } from '../../features/maps/maps.page';
import { MusicsPage } from '../../features/musics/musics.page';

export type AppTabId = 'maps' | 'musics';
export interface AppTabDef { id: AppTabId; label: string; component: Type<unknown>; }

export const APP_TABS: ReadonlyArray<AppTabDef> = [
  { id: 'maps', label: 'Maps', component: MapsPage },
  { id: 'musics', label: 'Musics', component: MusicsPage },
];