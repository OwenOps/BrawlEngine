export interface MusicTrack {
  fileName: string;
  label: string;
  slot: string;
  changed?: boolean;
  source?: string | null;
  changedAt?: string | null;
}

export interface MusicTracks {
  tracks: MusicTrack[];
  otherChanges?: string[];
  otherCount?: number;
}
