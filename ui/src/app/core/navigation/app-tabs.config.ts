import { Type } from '@angular/core';
import { MapsPageComponent } from '../../features/maps/maps-page.component';
import { MusicsPageComponent } from '../../features/musics/musics-page.component';

export type AppTabId = 'maps' | 'musics';

export interface AppTabDef {
  id: AppTabId;
  label: string;
  component: Type<unknown>;
}

export const APP_TABS: ReadonlyArray<AppTabDef> = [
  { id: 'maps', label: 'Maps', component: MapsPageComponent },
  { id: 'musics', label: 'Musics', component: MusicsPageComponent },
];
