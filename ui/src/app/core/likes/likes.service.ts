import { Injectable, computed, inject, signal } from '@angular/core';
import { CatalogItem } from '../ipc/contracts/catalog.contracts';
import { LikeKind, Likes } from '../ipc/contracts/likes.contracts';
import { IPC_MESSAGE } from '../ipc/ipc.constants';
import { IpcService } from '../ipc/ipc.service';

@Injectable({ providedIn: 'root' })
export class LikesService {
  private readonly ipc = inject(IpcService);
  private readonly likes = signal<Likes>({ maps: [], sounds: [], skins: [] });

  readonly mapIds = computed(() => new Set(this.likes().maps));
  readonly soundIds = computed(() => new Set(this.likes().sounds));
  readonly skinIds = computed(() => new Set(this.likes().skins));

  constructor() {
    this.refresh();
  }

  has(kind: LikeKind, id: number): boolean {
    return this.ids(kind).has(id);
  }

  ids(kind: LikeKind): ReadonlySet<number> {
    if (kind === 'sounds') {
      return this.soundIds();
    }
    if (kind === 'skins') {
      return this.skinIds();
    }
    return this.mapIds();
  }

  refresh(): Promise<void> {
    return this.ipc.request(IPC_MESSAGE.LIKES_GET).then((reply) => {
      if (!reply.ok) {
        return;
      }
      this.apply(reply.payload as Likes | undefined);
    });
  }

  toggle(kind: LikeKind, item: CatalogItem): Promise<void> {
    return this.ipc
      .request(IPC_MESSAGE.LIKES_TOGGLE, { kind, id: item.id, item })
      .then((reply) => {
        if (!reply.ok) {
          return;
        }
        this.apply(reply.payload as Likes | undefined);
      });
  }

  private apply(payload: Likes | undefined): void {
    this.likes.set({
      maps: payload?.maps ?? [],
      sounds: payload?.sounds ?? [],
      skins: payload?.skins ?? [],
    });
  }
}
