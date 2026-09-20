import { Injectable, computed, inject, signal } from '@angular/core';
import { DownloadKind, DownloadProgress, DownloadSummary } from '../ipc/contracts/download.contracts';
import { IPC_MESSAGE } from '../ipc/ipc.constants';
import { IpcService } from '../ipc/ipc.service';
import { formatBytes } from './format-bytes';

/** Tracks active downloads (host push events) so any page or the sidebar can show live progress. */
@Injectable({ providedIn: 'root' })
export class DownloadActivityService {
  private readonly ipc = inject(IpcService);
  private readonly items = signal<ReadonlyMap<string, DownloadProgress>>(new Map());
  readonly count = signal(0);
  readonly sizeBytes = signal(0);
  readonly folder = signal('');
  readonly usingDefaultFolder = signal(true);
  readonly summaryLabel = computed(() => {
    const n = this.count();
    const size = formatBytes(this.sizeBytes());
    if (n === 0) {
      return 'No downloads yet';
    }
    const noun = n === 1 ? 'download' : 'downloads';
    return n + ' ' + noun + ' · ' + size;
  });

  readonly active = computed(() => Array.from(this.items().values()));

  constructor() {
    this.ipc.on(IPC_MESSAGE.DOWNLOAD_PROGRESS, (msg) => {
      const progress = msg.payload as DownloadProgress | undefined;
      if (!progress) {
        return;
      }
      this.items.update((map) => {
        const next = new Map(map);
        next.set(key(progress.kind, progress.id), progress);
        return next;
      });
    });
    this.refreshSummary();
  }

  progressFor(kind: DownloadKind, id: number): DownloadProgress | undefined {
    return this.items().get(key(kind, id));
  }

  percentFor(kind: DownloadKind, id: number): number | null {
    const progress = this.progressFor(kind, id);
    if (!progress || progress.totalBytes <= 0) {
      return null;
    }
    return Math.min(100, Math.round((progress.bytesDownloaded / progress.totalBytes) * 100));
  }

  /** Bump when download folders on disk change outside a page (Reset all wipe). */
  readonly inventoryEpoch = signal(0);

  notifyInventoryChanged(): void {
    this.inventoryEpoch.update((n) => n + 1);
    this.refreshSummary();
  }

  private refreshSummary(): void {
    void this.ipc.request(IPC_MESSAGE.DOWNLOADS_SUMMARY).then((reply) => {
      if (!reply.ok) {
        return;
      }
      const data = reply.payload as DownloadSummary | undefined;
      this.count.set(data?.count ?? 0);
      this.sizeBytes.set(data?.sizeBytes ?? 0);
      this.folder.set(data?.folder ?? '');
      this.usingDefaultFolder.set(data?.isDefault !== false);
    });
  }

  /** Ask the host to stop this download (queued or in flight). */
  cancel(kind: DownloadKind, id: number): void {
    void this.ipc.request(IPC_MESSAGE.DOWNLOADS_CANCEL, { kind, id });
  }

  /** Call once a download settles (success or failure) so a stale bar does not linger on the card. */
  clear(kind: DownloadKind, id: number): void {
    this.items.update((map) => {
      if (!map.has(key(kind, id))) {
        return map;
      }
      const next = new Map(map);
      next.delete(key(kind, id));
      return next;
    });
  }
}

function key(kind: DownloadKind, id: number): string {
  return `${kind}:${id}`;
}
