import { Injectable, computed, inject, signal } from '@angular/core';
import { CrashKind, CrashTag, Crashes } from '../ipc/contracts/crashes.contracts';
import { IPC_MESSAGE } from '../ipc/ipc.constants';
import { IpcService } from '../ipc/ipc.service';

@Injectable({ providedIn: 'root' })
export class CrashesService {
  private readonly ipc = inject(IpcService);
  private readonly crashes = signal<Crashes>({ maps: [], sounds: [], skins: [] });

  readonly mapIds = computed(() => idsOf(this.crashes().maps));
  readonly soundIds = computed(() => idsOf(this.crashes().sounds));
  readonly skinIds = computed(() => idsOf(this.crashes().skins));

  constructor() {
    this.refresh();
  }

  has(kind: CrashKind, id: number): boolean {
    return this.ids(kind).has(id);
  }

  note(kind: CrashKind, id: number): string | null {
    const tag = this.tags(kind).find((row) => row.id === id);
    return tag?.note ?? null;
  }

  ids(kind: CrashKind): ReadonlySet<number> {
    if (kind === 'sounds') {
      return this.soundIds();
    }
    if (kind === 'skins') {
      return this.skinIds();
    }
    return this.mapIds();
  }

  refresh(): Promise<void> {
    return this.ipc.request(IPC_MESSAGE.CRASHES_GET).then((reply) => {
      if (!reply.ok) {
        return;
      }
      this.apply(reply.payload as Crashes | undefined);
    });
  }

  askNote(): string | null | undefined {
    const typed = window.prompt('Why skip Apply? (optional)');
    if (typed === null) {
      return undefined;
    }
    const trimmed = typed.trim();
    return trimmed.length === 0 ? null : trimmed;
  }

  toggle(kind: CrashKind, id: number, note?: string | null): Promise<void> {
    return this.ipc.request(IPC_MESSAGE.CRASHES_TOGGLE, { kind, id, note }).then((reply) => {
      if (!reply.ok) {
        return;
      }
      this.apply(reply.payload as Crashes | undefined);
    });
  }

  private tags(kind: CrashKind): CrashTag[] {
    if (kind === 'sounds') {
      return this.crashes().sounds;
    }
    if (kind === 'skins') {
      return this.crashes().skins;
    }
    return this.crashes().maps;
  }

  private apply(payload: Crashes | undefined): void {
    this.crashes.set({
      maps: payload?.maps ?? [],
      sounds: payload?.sounds ?? [],
      skins: payload?.skins ?? [],
    });
  }
}

function idsOf(tags: CrashTag[]): Set<number> {
  return new Set(tags.map((tag) => tag.id));
}
