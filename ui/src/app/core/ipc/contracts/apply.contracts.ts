export interface ApplyAttempt {
  applied: boolean;
  reason: string | null;
  queued?: boolean;
}

export type ApplyWorkKind = 'maps' | 'sounds' | 'skins';

/** Host ApplyProgress.CustomReplaceId — custom audio replace, not a catalog mod. */
export const CUSTOM_AUDIO_PROGRESS_ID = 2147483647;

export interface ApplyProgress {
  kind: ApplyWorkKind;
  id: number;
  action: 'apply' | 'reset';
  done: number;
  total: number;
  name?: string | null;
}
