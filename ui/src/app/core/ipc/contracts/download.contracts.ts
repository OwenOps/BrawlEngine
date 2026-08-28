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
