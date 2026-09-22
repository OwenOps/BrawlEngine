export interface AppInfo {
  madeBy: string;
  version: string;
}

export interface AppUpdate {
  available: boolean;
  latest?: string | null;
  url?: string | null;
}
