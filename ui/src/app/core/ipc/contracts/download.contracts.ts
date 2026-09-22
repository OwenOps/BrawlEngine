export interface DownloadedFile {
  name: string;
  path: string;
  bytes: number;
}

export interface DownloadResult {
  modId: number;
  folder: string;
  files: DownloadedFile[];
}

export interface LocalDownload {
  id: number;
  folder: string;
  mapCount?: number | null;
  sizeBytes?: number;
  downloadedUtc?: string | null;
}

export interface MapNamesResult {
  names: string[];
}

export interface LocalDownloadList {
  items: LocalDownload[];
}

export interface DownloadSummary {
  count: number;
  sizeBytes: number;
  folder?: string;
  isDefault?: boolean;
}

export interface DownloadsLocation {
  path: string;
  cancelled?: boolean;
  error?: string;
}

export interface DownloadImport {
  id: number;
  folder: string;
  fileCount: number;
  cancelled?: boolean;
  error?: string;
}

export type DownloadKind = 'maps' | 'sounds' | 'skins';

/** `mods` is the parent folder (Maps + sounds + skins). */
export type DownloadsFolderKind = DownloadKind | 'mods';

export interface DownloadProgress {
  kind: DownloadKind;
  id: number;
  fileName: string;
  fileIndex: number;
  fileCount: number;
  bytesDownloaded: number;
  totalBytes: number;
}
