export interface GameLocation {
  path: string | null;
  found: boolean;
  source: string;
  hasMapArt: boolean;
  cancelled?: boolean;
  error?: string;
}

export interface ApplyGuard {
  allowed: boolean;
  running: boolean;
  message: string | null;
}