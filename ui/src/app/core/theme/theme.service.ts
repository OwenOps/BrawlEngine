import { Injectable, signal } from '@angular/core';

export type ThemeName = 'blue' | 'purple' | 'green' | 'orange' | 'red' | 'teal';

export interface ThemePreset {
  label: string;
  accent: string;
  accentMaps: string;
  accentMusic: string;
  accentSkins: string;
}

export const THEME_PRESETS: Record<ThemeName, ThemePreset> = {
  blue: {
    label: 'Blue',
    accent: '#4c8dff',
    accentMaps: '#4c8dff',
    accentMusic: '#b57bf6',
    accentSkins: '#ff9f5b',
  },
  purple: {
    label: 'Purple',
    accent: '#a374f7',
    accentMaps: '#a374f7',
    accentMusic: '#e17bf0',
    accentSkins: '#7bb8f7',
  },
  green: {
    label: 'Green',
    accent: '#46c98a',
    accentMaps: '#46c98a',
    accentMusic: '#8fd15a',
    accentSkins: '#2fb8a6',
  },
  orange: {
    label: 'Orange',
    accent: '#ff9f43',
    accentMaps: '#ff9f43',
    accentMusic: '#ffb85c',
    accentSkins: '#f76b3c',
  },
  red: {
    label: 'Red',
    accent: '#ef6a6a',
    accentMaps: '#ef6a6a',
    accentMusic: '#c96ce0',
    accentSkins: '#ff8f70',
  },
  teal: {
    label: 'Teal',
    accent: '#33c2c9',
    accentMaps: '#33c2c9',
    accentMusic: '#5ad1a0',
    accentSkins: '#3aa7e0',
  },
};

const STORAGE_KEY = 'brawlengine.theme';

/** Lets the user swap the app's accent palette (nav, buttons, per-tab colors). Purely visual, no host round-trip. */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly presets = THEME_PRESETS;
  readonly current = signal<ThemeName>(this.readStored());

  constructor() {
    this.apply(this.current());
  }

  setTheme(name: ThemeName): void {
    this.current.set(name);
    localStorage.setItem(STORAGE_KEY, name);
    this.apply(name);
  }

  private apply(name: ThemeName): void {
    const preset = THEME_PRESETS[name];
    const root = document.documentElement.style;
    root.setProperty('--accent', preset.accent);
    root.setProperty('--accent-maps', preset.accentMaps);
    root.setProperty('--accent-music', preset.accentMusic);
    root.setProperty('--accent-skins', preset.accentSkins);
  }

  private readStored(): ThemeName {
    const stored = localStorage.getItem(STORAGE_KEY);
    return stored !== null && stored in THEME_PRESETS ? (stored as ThemeName) : 'blue';
  }
}
