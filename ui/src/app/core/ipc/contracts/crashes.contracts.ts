import { LikeKind } from './likes.contracts';

export interface CrashTag {
  id: number;
  note: string | null;
}

export interface Crashes {
  maps: CrashTag[];
  sounds: CrashTag[];
  skins: CrashTag[];
}

export type CrashKind = LikeKind;
