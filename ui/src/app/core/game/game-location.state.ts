import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class GameLocationState {
  readonly mp3Path = signal<string | null>(null);
}
