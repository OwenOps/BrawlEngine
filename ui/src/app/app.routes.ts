import { Routes } from '@angular/router';
import { MapsPage } from './features/maps/maps.page';
import { MusiquesPage } from './features/musics/musics.page';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'maps' },
  { path: 'maps', component: MapsPage },
  { path: 'musiques', component: MusiquesPage },
];
