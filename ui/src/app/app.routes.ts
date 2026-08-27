import { Routes } from '@angular/router';
import { MapsPage } from './pages/maps.page';
import { MusiquesPage } from './pages/musiques.page';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'maps' },
  { path: 'maps', component: MapsPage },
  { path: 'musiques', component: MusiquesPage },
];
