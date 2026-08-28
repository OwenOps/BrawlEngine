import { Component, OnInit, computed, signal } from '@angular/core';
import { IPC_MESSAGE } from '../../core/ipc/ipc.constants';
import { IpcService } from '../../core/ipc/ipc.service';

export interface CatalogItem {
  id: number;
  name: string;
  author: string;
  thumbnailUrl: string | null;
  category: string;
  profileUrl: string;
}

export interface CatalogPage {
  items: CatalogItem[];
  nextApiPage: number;
  complete: boolean;
}

@Component({
  selector: 'app-maps-page',
  standalone: true,
  templateUrl: './maps.page.html',
  styleUrl: './maps.page.scss',
})
export class MapsPage implements OnInit {
  readonly items = signal<CatalogItem[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly complete = signal(false);
  readonly downloadingId = signal<number | null>(null);
  readonly downloadNote = signal<Record<number, string>>({});
  readonly canLoadMore = computed(() => !this.complete() && !this.error());
  private readonly nextApiPage = signal(1);

  constructor(private readonly ipc: IpcService) {}

  ngOnInit(): void {
    this.load(true);
  }

  loadMore(): void {
    this.load(false);
  }

  download(mod: CatalogItem): void {
    if (this.downloadingId() !== null) {
      return;
    }
    this.downloadingId.set(mod.id);

    this.ipc
      .request(IPC_MESSAGE.MOD_DOWNLOAD, { id: mod.id })
      .then((reply) => {
        if (!reply.ok) {
          this.downloadNote.update((notes) => ({
            ...notes,
            [mod.id]: reply.error ?? 'Download failed.',
          }));
          return;
        }
        this.downloadNote.update((notes) => ({
          ...notes,
          [mod.id]: 'Downloaded. Apply comes next.',
        }));
      })
      .catch((error: unknown) => {
        const message =
          error instanceof Error ? error.message : 'Download failed.';
        this.downloadNote.update((notes) => ({ ...notes, [mod.id]: message }));
      })
      .finally(() => {
        this.downloadingId.set(null);
      });
  }

  apply(mod: CatalogItem): void {
    
  }

  private load(reset: boolean): void {
    if (this.loading()) {
      return;
    }

    this.loading.set(true);
    this.error.set(null);
    const page = reset ? 1 : this.nextApiPage();

    this.ipc
      .request(IPC_MESSAGE.CATALOG_MAPS, { page })
      .then((reply) => {
        if (!reply.ok) {
          this.error.set(reply.error ?? 'Could not load maps.');
          return;
        }

        const data = reply.payload as CatalogPage | undefined;
        if (!data) {
          this.error.set('Empty catalog response.');
          return;
        }

        this.items.update((existing) =>
          reset ? data.items : [...existing, ...data.items],
        );
        this.nextApiPage.set(data.nextApiPage);
        this.complete.set(data.complete);
      })
      .catch((error: unknown) => {
        this.error.set(
          error instanceof Error ? error.message : 'Could not load maps.',
        );
      })
      .finally(() => {
        this.loading.set(false);
      });
  }
}
