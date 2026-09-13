export interface ApplyAttempt {
  applied: boolean;
  reason: string | null;
}

export type ApplyWorkKind = 'maps' | 'sounds' | 'skins';

export interface ApplyProgress {
  kind: ApplyWorkKind;
  id: number;
  action: 'apply' | 'reset';
  done: number;
  total: number;
  name?: string | null;
}
