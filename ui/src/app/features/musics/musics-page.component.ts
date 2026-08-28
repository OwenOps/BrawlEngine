import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'app-musics-page',
  standalone: true,
  template: `
    <h1>Musics</h1>
    <p>Music mods and custom links will live here. Catalog wiring is a later step.</p>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MusicsPageComponent {}
