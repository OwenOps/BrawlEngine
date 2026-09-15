import { Injectable, computed, inject, signal } from '@angular/core';
import { ApplyProgress, ApplyWorkKind } from '../ipc/contracts/apply.contracts';
import { IPC_MESSAGE } from '../ipc/ipc.constants';
import { IpcService } from '../ipc/ipc.service';

/** Live Apply / Reset percent on the card (host push, same idea as downloads). */
@Injectable({ providedIn: 'root' })
export class ApplyActivityService {
  private readonly ipc = inject(IpcService);
  private readonly items = signal<ReadonlyMap<string, ApplyProgress>>(new Map());
  readonly active = computed(() => Array.from(this.items().values()));

  constructor() {
    this.ipc.on(IPC_MESSAGE.APPLY_PROGRESS, (msg) => {
      const progress = msg.payload as ApplyProgress | undefined;
      if (!progress || progress.id <= 0) {
        return;
      }
      this.items.update((map) => {
        const next = new Map(map);
        next.set(key(progress.kind, progress.id), progress);
        return next;
      });
    });
  }

  isActive(kind: ApplyWorkKind, id: number): boolean {
    return this.items().has(key(kind, id));
  }

  percentFor(kind: ApplyWorkKind, id: number): number | null {
    const progress = this.items().get(key(kind, id));
    if (!progress || progress.total <= 0) {
      return null;
    }

    return Math.min(100, Math.round((progress.done / progress.total) * 100));
  }

  labelFor(kind: ApplyWorkKind, id: number, fallback: 'apply' | 'reset' = 'apply'): string {
    const progress = this.items().get(key(kind, id));
    if (progress?.name) {
      return progress.name;
    }
    const verb = progress?.action === 'reset' || fallback === 'reset' ? 'Resetting' : 'Applying';
    const percent = this.percentFor(kind, id);
    return percent === null ? verb + '…' : verb + ' ' + percent + '%';
  }

  clear(kind: ApplyWorkKind, id: number): void {
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

function key(kind: ApplyWorkKind, id: number): string {
  return `${kind}:${id}`;
}
