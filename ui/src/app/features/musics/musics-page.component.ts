import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ApplyAttempt } from '../../core/ipc/contracts/apply.contracts';
import { CatalogItem, CatalogPage } from '../../core/ipc/contracts/catalog.contracts';
import { MusicTracks } from '../../core/ipc/contracts/music.contracts';
import { IPC_MESSAGE } from '../../core/ipc/ipc.constants';
import { IpcService } from '../../core/ipc/ipc.service';
import { LoadoutService } from '../../core/loadout/loadout.service';

@Component({
  selector: 'app-musics-page',
  standalone: true,
  templateUrl: './musics-page.component.html',
  styleUrl: './musics-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MusicsPageComponent {
  private readonly ipc = inject(IpcService);
  private readonly loadout = inject(LoadoutService);

  readonly items = signal<CatalogItem[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly complete = signal(false);
  readonly downloadingId = signal<number | null>(null);
  readonly applyingId = signal<number | null>(null);
  readonly cardNote = signal<Record<number, string>>({});
  readonly tracks = signal<string[]>([]);
  readonly selectedTrack = signal('');
  readonly customNote = signal<string | null>(null);
  readonly replacing = signal(false);
  readonly canLoadMore = computed(() => !this.complete() && this.error() === null);
  readonly isBusy = computed(
    () =>
      this.downloadingId() !== null ||
      this.applyingId() !== null ||
      this.replacing() ||
      this.loadout.busy(),
  );
  private readonly nextApiPage = signal(1);

  constructor() {
    this.load(true);
    this.loadTracks();
  }

  isActive(id: number): boolean {
    return this.loadout.activeMusicIds().has(id);
  }

  loadMore(): void {
    this.load(false);
  }

  selectTrack(event: Event): void {
    const select = event.target as HTMLSelectElement;
    this.selectedTrack.set(select.value);
  }

  download(item: CatalogItem): void {
    if (this.isBusy()) {
      return;
    }
    this.downloadingId.set(item.id);
    this.ipc
      .request(IPC_MESSAGE.SOUND_DOWNLOAD, { id: item.id })
      .then((reply) => {
        this.setNote(item.id, reply.ok ? 'Downloaded. You can Apply.' : (reply.error ?? 'Download failed.'));
      })
      .catch((error: unknown) => {
        this.setNote(item.id, error instanceof Error ? error.message : 'Download failed.');
      })
      .finally(() => {
        this.downloadingId.set(null);
      });
  }

  apply(item: CatalogItem): void {
    if (this.isBusy()) {
      return;
    }
    this.applyingId.set(item.id);
    this.ipc
      .request(IPC_MESSAGE.MUSIC_APPLY, { id: item.id })
      .then((reply) => {
        if (!reply.ok) {
          this.setNote(item.id, reply.error ?? 'Apply failed.');
          return;
        }
        const result = reply.payload as ApplyAttempt | undefined;
        this.setNote(item.id, result?.reason ?? 'Applied.');
        void this.loadout.refresh();
      })
      .catch((error: unknown) => {
        this.setNote(item.id, error instanceof Error ? error.message : 'Apply failed.');
      })
      .finally(() => {
        this.applyingId.set(null);
      });
  }

  replaceTrack(): void {
    const target = this.selectedTrack();
    if (!target || this.isBusy()) {
      return;
    }

    this.replacing.set(true);
    this.customNote.set(null);
    this.ipc
      .request(IPC_MESSAGE.MUSIC_REPLACE, { target })
      .then((reply) => {
        if (!reply.ok) {
          this.customNote.set(reply.error ?? 'Replace failed.');
          return;
        }
        const result = reply.payload as ApplyAttempt | undefined;
        this.customNote.set(result?.reason ?? 'Replaced.');
      })
      .catch((error: unknown) => {
        this.customNote.set(error instanceof Error ? error.message : 'Replace failed.');
      })
      .finally(() => {
        this.replacing.set(false);
      });
  }

  private setNote(id: number, message: string): void {
    this.cardNote.update((notes) => ({ ...notes, [id]: message }));
  }

  private loadTracks(): void {
    this.ipc.request(IPC_MESSAGE.MUSIC_TRACKS).then((reply) => {
      if (!reply.ok) {
        this.customNote.set(reply.error ?? 'Could not list mp3 tracks.');
        return;
      }

      const data = reply.payload as MusicTracks | undefined;
      const list = data?.tracks ?? [];
      this.tracks.set(list);
      if (list.length > 0 && !this.selectedTrack()) {
        this.selectedTrack.set(list[0]);
      }
    });
  }

  private load(reset: boolean): void {
    if (this.loading()) {
      return;
    }

    this.loading.set(true);
    this.error.set(null);
    const page = reset ? 1 : this.nextApiPage();

    this.ipc
      .request(IPC_MESSAGE.CATALOG_SOUNDS, { page })
      .then((reply) => {
        if (!reply.ok) {
          this.error.set(reply.error ?? 'Could not load sounds.');
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
          error instanceof Error ? error.message : 'Could not load sounds.',
        );
      })
      .finally(() => {
        this.loading.set(false);
      });
  }
}
