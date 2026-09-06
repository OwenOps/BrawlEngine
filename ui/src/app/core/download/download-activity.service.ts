import { Injectable, computed, inject, signal } from '@angular/core';
import { DownloadKind, DownloadProgress } from '../ipc/contracts/download.contracts';
import { IPC_MESSAGE } from '../ipc/ipc.constants';
import { IpcService } from '../ipc/ipc.service';

/** Tracks active downloads (host push events) so any page or the sidebar can show live progress. */
@Injectable({ providedIn: 'root' })
export class DownloadActivityService {
  private readonly ipc = inject(IpcService);
  private readonly items = signal<ReadonlyMap<string, DownloadProgress>>(new Map());

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
