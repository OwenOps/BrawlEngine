import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  OnDestroy,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { LIBRARY_FILTERS, LibraryFilter } from '../../core/catalog/library-filter';
import { scrollMainToTop } from '../../core/ui/scroll-main';
import { BrowserService } from '../../core/browser/browser.service';
import { formatBytes, confirmDeleteDownload } from '../../core/download/format-bytes';
import { DownloadActivityService } from '../../core/download/download-activity.service';
import { GameLocationState } from '../../core/game/game-location.state';
import { ApplyAttempt } from '../../core/ipc/contracts/apply.contracts';
import {
  CATALOG_SORTS,
  CatalogItem,
  CatalogPage,
  CatalogSort,
} from '../../core/ipc/contracts/catalog.contracts';
import { MusicTrack, MusicTracks } from '../../core/ipc/contracts/music.contracts';
import { LocalDownloadList } from '../../core/ipc/contracts/download.contracts';
import { IPC_MESSAGE } from '../../core/ipc/ipc.constants';
import { IpcService } from '../../core/ipc/ipc.service';
import { LoadoutService } from '../../core/loadout/loadout.service';
import {
  MUSIC_SLOTS,
  asMusicSlot,
  slotHint,
} from './music-slot';
import { SOUND_CATEGORIES } from './sound-categories';

@Component({
  selector: 'app-musics-page',
  standalone: true,
  templateUrl: './musics-page.component.html',
  styleUrl: './musics-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MusicsPageComponent implements OnDestroy {
  private readonly ipc = inject(IpcService);
  private readonly loadout = inject(LoadoutService);
  private readonly browser = inject(BrowserService);
  private readonly gameLocation = inject(GameLocationState);
  readonly downloadActivity = inject(DownloadActivityService);
  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  private loadToken = 0;

  readonly sorts = CATALOG_SORTS;
  readonly libraryFilters = LIBRARY_FILTERS;
  readonly libraryFilter = signal<LibraryFilter>('all');
  readonly musicSlots = MUSIC_SLOTS;
  readonly soundCategories = SOUND_CATEGORIES;
  readonly categoryId = signal(0);
  readonly queryInput = signal('');
  readonly query = signal('');
  readonly sort = signal<CatalogSort>('newest');
  readonly items = signal<CatalogItem[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly page = signal(1);
  readonly totalPages = signal(1);
  readonly totalCount = signal(0);
  readonly downloadingId = signal<number | null>(null);
  readonly applyingId = signal<number | null>(null);
  readonly deletingId = signal<number | null>(null);
  readonly cardNote = signal<Record<number, string>>({});
  readonly tracks = signal<MusicTrack[]>([]);
  readonly selectedTrack = signal('');
  readonly customNote = signal<string | null>(null);
  readonly audioUrl = signal('');
  readonly replacing = signal(false);
  readonly infoItem = signal<CatalogItem | null>(null);
  private readonly downloadedIds = signal<ReadonlySet<number>>(new Set());
  private readonly folders = signal<Record<number, string>>({});
  private readonly sizeBytes = signal<Record<number, number>>({});
  readonly isBusy = computed(
    () =>
      this.downloadingId() !== null ||
      this.applyingId() !== null ||
      this.deletingId() !== null ||
      this.replacing() ||
      this.loadout.busy(),
  );
  readonly isLibrary = computed(() => this.libraryFilter() !== 'all');
  readonly shownItems = computed(() => {
    let list = this.items();
    if (this.isLibrary()) {
      const query = this.queryInput().trim().toLowerCase();
      if (query) {
        list = list.filter((item) => item.name.toLowerCase().includes(query));
      }
      const categoryId = this.categoryId();
      if (categoryId !== 0) {
        const cat = this.soundCategories.find((row) => row.id === categoryId);
        if (cat) {
          list = list.filter((item) => item.category === cat.label);
        }
      }
    }
    return list;
  });
  readonly trackGroups = computed(() =>
    MUSIC_SLOTS.map((slot) => ({
      id: slot.id,
      label: slot.label,
      tracks: this.tracks().filter((track) => track.slot === slot.id),
    })).filter((group) => group.tracks.length > 0),
  );
  readonly usesWem = computed(() =>
    this.tracks().some((track) => track.fileName.toLowerCase().endsWith('.wem')),
  );
  readonly selectedSlotHint = computed(() => {
    const file = this.selectedTrack();
    const meta = this.tracks().find((track) => track.fileName === file);
    if (!meta) {
      return '';
    }
    return slotHint(asMusicSlot(meta.slot));
  });

  constructor() {
    this.loadLocal();
    this.load(1);
    effect(() => {
      this.gameLocation.mp3Path();
      untracked(() => this.loadTracks());
    });
  }

  ngOnDestroy(): void {
    if (this.searchTimer !== undefined) {
      clearTimeout(this.searchTimer);
    }
  }

  isActive(id: number): boolean {
    return this.loadout.activeMusicIds().has(id);
  }

  isDownloaded(id: number): boolean {
    return this.downloadedIds().has(id) || this.isActive(id);
  }

  downloadLabel(id: number): string {
    const percent = this.downloadActivity.percentFor('sounds', id);
    return percent === null ? 'Downloading…' : `Downloading ${percent}%…`;
  }

  diskSize(id: number): string | null {
    const bytes = this.sizeBytes()[id];
    if (bytes === undefined || bytes <= 0) {
      return null;
    }
    return formatBytes(bytes);
  }

  onQueryInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.queryInput.set(input.value);
    if (this.isLibrary()) {
      return;
    }
    if (this.searchTimer !== undefined) {
      clearTimeout(this.searchTimer);
    }
    this.searchTimer = setTimeout(() => {
      this.query.set(this.queryInput().trim());
      this.load(1);
    }, 350);
  }

  onSort(event: Event): void {
    const select = event.target as HTMLSelectElement;
    const value = select.value as CatalogSort;
    if (value !== 'newest' && value !== 'liked' && value !== 'downloaded') {
      return;
    }
    this.sort.set(value);
    if (this.isLibrary()) {
      return;
    }
    this.load(1);
  }

  goToPage(page: number): void {
    if (this.loading() || page < 1 || page > this.totalPages() || page === this.page()) {
      return;
    }
    this.load(page);
    scrollMainToTop();
  }

  retry(): void {
    if (this.isLibrary()) {
      this.loadLibrary();
      return;
    }
    this.load(this.page());
  }

  openInfo(item: CatalogItem): void {
    this.infoItem.set(item);
  }

  closeInfo(): void {
    this.infoItem.set(null);
  }

  @HostListener('document:keydown.escape')
  closeInfoOnEscape(): void {
    if (this.infoItem()) {
      this.closeInfo();
    }
  }

  openProfile(url: string): void {
    this.browser.open(url);
  }

  openDownloadsFolder(): void {
    this.browser.openDownloads('sounds');
  }

  openFolder(item: CatalogItem): void {
    const path = this.folders()[item.id];
    if (!path) {
      return;
    }
    this.browser.openFolder(path);
  }

  selectTrack(event: Event): void {
    const select = event.target as HTMLSelectElement;
    this.selectedTrack.set(select.value);
  }

  setAudioUrl(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.audioUrl.set(input.value);
  }

  setCategoryFilter(id: number): void {
    if (this.categoryId() === id) {
      return;
    }
    this.categoryId.set(id);
    if (this.isLibrary()) {
      return;
    }
    this.load(1);
  }

  setLibraryFilter(id: LibraryFilter): void {
    if (this.libraryFilter() === id) {
      return;
    }
    this.libraryFilter.set(id);
    if (id === 'all') {
      this.load(1);
      return;
    }
    this.loadLibrary();
  }

  reloadTracks(): void {
    this.loadTracks();
  }

  download(item: CatalogItem): void {
    if (this.isBusy()) {
      return;
    }
    this.downloadingId.set(item.id);
    this.ipc
      .request(IPC_MESSAGE.SOUND_DOWNLOAD, { id: item.id })
      .then((reply) => {
        if (!reply.ok) {
          this.setNote(item.id, reply.error ?? 'Download failed.');
          return;
        }
        this.downloadedIds.update((ids) => new Set(ids).add(item.id));
        this.setNote(item.id, 'Downloaded. You can Apply.');
        this.loadLocal();
        if (this.isLibrary()) {
          this.loadLibrary();
        }
      })
      .catch((error: unknown) => {
        this.setNote(item.id, error instanceof Error ? error.message : 'Download failed.');
      })
      .finally(() => {
        this.downloadingId.set(null);
        this.downloadActivity.clear('sounds', item.id);
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
        void this.loadout.refresh().then(() => {
          if (this.libraryFilter() === 'applied') {
            this.loadLibrary();
          }
        });
      })
      .catch((error: unknown) => {
        this.setNote(item.id, error instanceof Error ? error.message : 'Apply failed.');
      })
      .finally(() => {
        this.applyingId.set(null);
      });
  }

  replaceTrack(): void {
    this.runReplace(undefined);
  }

  replaceFromUrl(): void {
    const url = this.audioUrl().trim();
    if (!url) {
      this.customNote.set('Paste a direct http(s) link to an .mp3 file.');
      return;
    }
    this.runReplace(url);
  }

  private runReplace(url: string | undefined): void {
    const target = this.selectedTrack();
    if (!target || this.isBusy()) {
      return;
    }

    this.replacing.set(true);
    this.customNote.set(null);
    const payload = url ? { target, url } : { target };
    this.ipc
      .request(IPC_MESSAGE.MUSIC_REPLACE, payload)
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

  /** Frees disk space. Safe even if applied: Reapply just re-downloads it. */
  deleteDownload(id: number): void {
    if (this.isBusy()) {
      return;
    }
    const item = this.shownItems().find((row) => row.id === id);
    if (!confirmDeleteDownload(item?.name ?? 'this sound', this.sizeBytes()[id])) {
      return;
    }
    this.deletingId.set(id);
    this.ipc
      .request(IPC_MESSAGE.DOWNLOADS_DELETE, { kind: 'sounds', id })
      .then((reply) => {
        if (!reply.ok) {
          this.setNote(id, reply.error ?? 'Delete failed.');
          return;
        }
        this.downloadedIds.update((ids) => {
          const next = new Set(ids);
          next.delete(id);
          return next;
        });
        this.folders.update((map) => {
          const { [id]: _folder, ...rest } = map;
          return rest;
        });
        this.sizeBytes.update((sizes) => {
          const { [id]: _size, ...rest } = sizes;
          return rest;
        });
        this.setNote(id, 'Removed from disk.');
        if (this.isLibrary()) {
          this.loadLibrary();
        }
      })
      .catch((error: unknown) => {
        this.setNote(id, error instanceof Error ? error.message : 'Delete failed.');
      })
      .finally(() => {
        this.deletingId.set(null);
      });
  }

  private setNote(id: number, message: string): void {
    this.cardNote.update((notes) => ({ ...notes, [id]: message }));
  }

  private loadLocal(): void {
    this.ipc.request(IPC_MESSAGE.DOWNLOADS_LIST, { kind: 'sounds' }).then((reply) => {
      if (!reply.ok) {
        return;
      }
      const data = reply.payload as LocalDownloadList | undefined;
      const items = data?.items ?? [];
      this.downloadedIds.set(new Set(items.map((item) => item.id)));
      const sizes: Record<number, number> = {};
      const folders: Record<number, string> = {};
      for (const item of items) {
        folders[item.id] = item.folder;
        if (item.sizeBytes !== undefined) {
          sizes[item.id] = item.sizeBytes;
        }
      }
      this.folders.set(folders);
      this.sizeBytes.set(sizes);
    });
  }

  private loadTracks(): void {
    this.ipc.request(IPC_MESSAGE.MUSIC_TRACKS).then((reply) => {
      if (!reply.ok) {
        this.tracks.set([]);
        this.customNote.set(reply.error ?? 'Could not list game audio tracks.');
        return;
      }

      const data = reply.payload as MusicTracks | undefined;
      const list = data?.tracks ?? [];
      this.tracks.set(list);
      if (list.length > 0) {
        this.customNote.set(null);
        const names = list.map((track) => track.fileName);
        if (!this.selectedTrack() || !names.includes(this.selectedTrack())) {
          this.selectedTrack.set(list[0].fileName);
        }
      }
    });
  }

  private loadLibrary(): void {
    const ids =
      this.libraryFilter() === 'applied'
        ? [...this.loadout.activeMusicIds()]
        : [...this.downloadedIds()];
    const token = ++this.loadToken;
    this.loading.set(true);
    this.error.set(null);
    if (ids.length === 0) {
      this.items.set([]);
      this.page.set(1);
      this.totalCount.set(0);
      this.totalPages.set(1);
      this.loading.set(false);
      return;
    }

    this.ipc
      .request(IPC_MESSAGE.CATALOG_BY_IDS, { kind: 'sounds', ids })
      .then((reply) => {
        if (token !== this.loadToken) {
          return;
        }
        if (!reply.ok) {
          this.error.set(reply.error ?? 'Could not load library.');
          return;
        }
        const data = reply.payload as CatalogPage | undefined;
        const list = data?.items ?? [];
        this.items.set(list);
        this.page.set(1);
        this.totalCount.set(list.length);
        this.totalPages.set(1);
      })
      .catch((error: unknown) => {
        if (token !== this.loadToken) {
          return;
        }
        this.error.set(error instanceof Error ? error.message : 'Could not load library.');
      })
      .finally(() => {
        if (token === this.loadToken) {
          this.loading.set(false);
        }
      });
  }

  private load(page: number): void {
    if (this.isLibrary()) {
      this.loadLibrary();
      return;
    }
    const token = ++this.loadToken;
    this.loading.set(true);
    this.error.set(null);

    this.ipc
      .request(IPC_MESSAGE.CATALOG_SOUNDS, {
        page,
        query: this.query(),
        sort: this.sort(),
        categoryId: this.categoryId(),
      })
      .then((reply) => {
        if (token !== this.loadToken) {
          return;
        }
        if (!reply.ok) {
          this.error.set(reply.error ?? 'Could not load sounds.');
          return;
        }

        const data = reply.payload as CatalogPage | undefined;
        if (!data) {
          this.error.set('Empty catalog response.');
          return;
        }

        this.items.set(data.items);
        this.page.set(data.page);
        this.totalCount.set(data.totalCount);
        this.totalPages.set(this.computeTotalPages(data));
      })
      .catch((error: unknown) => {
        if (token !== this.loadToken) {
          return;
        }
        this.error.set(error instanceof Error ? error.message : 'Could not load sounds.');
      })
      .finally(() => {
        if (token === this.loadToken) {
          this.loading.set(false);
        }
      });
  }

  private computeTotalPages(data: CatalogPage): number {
    if (data.totalCount > 0 && data.pageSize > 0) {
      return Math.max(1, Math.ceil(data.totalCount / data.pageSize));
    }
    return data.complete ? data.page : data.page + 1;
  }
}
