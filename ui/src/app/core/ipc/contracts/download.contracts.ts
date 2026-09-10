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
}

export interface MapNamesResult {
  names: string[];
}

export interface LocalDownloadList {
  items: LocalDownload[];
}

export type DownloadKind = 'maps' | 'sounds' | 'skins';

export interface DownloadProgress {
  kind: DownloadKind;
  id: number;
  fileName: string;
  fileIndex: number;
  fileCount: number;
  bytesDownloaded: number;
  totalBytes: number;
}
