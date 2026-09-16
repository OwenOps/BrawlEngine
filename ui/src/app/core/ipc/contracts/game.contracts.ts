export interface GameLocation {
  path: string | null;
  found: boolean;
  source: string;
  hasMapArt: boolean;
  mp3Path?: string | null;
  hasMp3?: boolean;
  mp3Source?: string;
  cancelled?: boolean;
  error?: string;
}

export interface ApplyGuard {
  allowed: boolean;
  running: boolean;
  message: string | null;
  pending?: number;
}