import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ApplyAttempt } from '../../core/ipc/contracts/apply.contracts';
import { CatalogItem, CatalogPage } from '../../core/ipc/contracts/catalog.contracts';
import { IPC_MESSAGE } from '../../core/ipc/ipc.constants';
import { IpcService } from '../../core/ipc/ipc.service';

@Component({
  selector: 'app-maps-page',
  standalone: true,
  templateUrl: './maps-page.component.html',
  styleUrl: './maps-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MapsPageComponent {
  private readonly ipc = inject(IpcService);

  readonly items = signal<CatalogItem[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly complete = signal(false);
  readonly downloadingId = signal<number | null>(null);
  readonly applyingId = signal<number | null>(null);
  readonly cardNote = signal<Record<number, string>>({});
  readonly canLoadMore = computed(() => !this.complete() && this.error() === null);
  readonly isBusy = computed(
    () => this.downloadingId() !== null || this.applyingId() !== null,
  );
  private readonly nextApiPage = signal(1);

  constructor() {
    this.load(true);
  }

  loadMore(): void {
    this.load(false);
  }

  download(mod: CatalogItem): void {
    if (this.isBusy()) {
      return;
    }
    this.downloadingId.set(mod.id);

    this.ipc
      .request(IPC_MESSAGE.MOD_DOWNLOAD, { id: mod.id })
      .then((reply) => {
        this.setNote(mod.id, reply.ok ? 'Downloaded. You can Apply.' : (reply.error ?? 'Download failed.'));
      })
      .catch((error: unknown) => {
        this.setNote(mod.id, error instanceof Error ? error.message : 'Download failed.');
      })
      .finally(() => {
        this.downloadingId.set(null);
      });
  }

  apply(mod: CatalogItem): void {
    if (this.isBusy()) {
      return;
    }
    this.applyingId.set(mod.id);

    this.ipc
      .request(IPC_MESSAGE.MOD_APPLY, { id: mod.id })
      .then((reply) => {
        if (!reply.ok) {
          this.setNote(mod.id, reply.error ?? 'Apply failed.');
          return;
        }
        const result = reply.payload as ApplyAttempt | undefined;
        if (result?.applied) {
          this.setNote(mod.id, 'Applied.');
          return;
        }
        this.setNote(mod.id, result?.reason ?? 'Ready. Extract comes next.');
      })
      .catch((error: unknown) => {
        this.setNote(mod.id, error instanceof Error ? error.message : 'Apply failed.');
      })
      .finally(() => {
        this.applyingId.set(null);
      });
  }

  private setNote(modId: number, message: string): void {
    this.cardNote.update((notes) => ({ ...notes, [modId]: message }));
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
