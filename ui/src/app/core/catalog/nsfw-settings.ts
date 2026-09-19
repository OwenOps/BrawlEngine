import { Injectable, signal } from '@angular/core';

const STORAGE_KEY = 'brawlengine.showNsfw';

@Injectable({ providedIn: 'root' })
export class NsfwSettings {
  readonly show = signal(localStorage.getItem(STORAGE_KEY) === '1');

  setFromCheckbox(event: Event): void {
    const on = (event.target as HTMLInputElement).checked;
    this.show.set(on);
    localStorage.setItem(STORAGE_KEY, on ? '1' : '0');
  }
}
