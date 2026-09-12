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
import { ApplyAttempt } from '../../core/ipc/contracts/apply.contracts';
import {
  CATALOG_SORTS,
  CatalogItem,
  CatalogPage,
  CatalogSort,
} from '../../core/ipc/contracts/catalog.contracts';
import { LocalDownloadList, MapNamesResult } from '../../core/ipc/contracts/download.contracts';
import { IPC_MESSAGE } from '../../core/ipc/ipc.constants';
import { IpcService } from '../../core/ipc/ipc.service';
import { LoadoutService } from '../../core/loadout/loadout.service';

@Component({
  selector: 'app-maps-page',
  standalone: true,
  templateUrl: './maps-page.component.html',
  styleUrl: './maps-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MapsPageComponent implements OnDestroy {
  private readonly ipc = inject(IpcService);
  private readonly loadout = inject(LoadoutService);
  private readonly browser = inject(BrowserService);
  readonly downloadActivity = inject(DownloadActivityService);
  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  private loadToken = 0;

  readonly sorts = CATALOG_SORTS;
  readonly libraryFilters = LIBRARY_FILTERS;
  readonly libraryFilter = signal<LibraryFilter>('all');
  readonly queryInput = signal('');
  readonly query = signal('');
  readonly sort = signal<CatalogSort>('newest');
  readonly items = signal<CatalogItem[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly page = signal(1);
  readonly totalPages = signal(1);
  readonly totalCount = signal(0);
  readonly downloadingIds = signal<ReadonlySet<number>>(new Set());
  readonly applyingId = signal<number | null>(null);
  readonly resettingId = signal<number | null>(null);
  readonly deletingId = signal<number | null>(null);
  readonly cardNote = signal<Record<number, string>>({});
  readonly infoItem = signal<CatalogItem | null>(null);
  readonly infoMapNames = signal<string[] | null>(null);
  readonly infoMapNamesLoading = signal(false);
  private readonly downloadedIds = signal<ReadonlySet<number>>(new Set());
  private readonly folders = signal<Record<number, string>>({});
  private readonly mapCounts = signal<Record<number, number | null>>({});
  private readonly sizeBytes = signal<Record<number, number>>({});
  readonly isBusy = computed(
    () =>
      this.applyingId() !== null ||
      this.resettingId() !== null ||
      this.deletingId() !== null ||
      this.loadout.busy(),
  );
  readonly isLibrary = computed(() => this.libraryFilter() !== 'all');
  readonly shownItems = computed(() => {
    const list = this.items();
    if (!this.isLibrary()) {
      return list;
    }
    const query = this.queryInput().trim().toLowerCase();
    if (!query) {
      return list;
    }
    return list.filter((item) => item.name.toLowerCase().includes(query));
  });

  constructor() {
    this.loadLocal();
    this.load(1);
    effect(() => {
      this.downloadActivity.inventoryEpoch();
      untracked(() => this.loadLocal());
    });
  }

  ngOnDestroy(): void {
    if (this.searchTimer !== undefined) {
      clearTimeout(this.searchTimer);
    }
  }

  isActive(modId: number): boolean {
    return this.loadout.activeMapIds().has(modId);
  }

  isDownloading(modId: number): boolean {
    return this.downloadingIds().has(modId);
  }

  isDownloaded(modId: number): boolean {
    return this.downloadedIds().has(modId) || this.isActive(modId);
  }

  downloadPercent(modId: number): number | null {
    return this.downloadActivity.percentFor('maps', modId);
  }

  downloadLabel(modId: number): string {
    const percent = this.downloadPercent(modId);
    return percent === null ? 'Downloading…' : `Downloading ${percent}%…`;
  }

  /** Null while unknown (not downloaded, or the archive could not be inspected). */
  packLabel(modId: number): string | null {
    const count = this.mapCounts()[modId];
    if (count === undefined || count === null) {
      return null;
    }
    return count > 1 ? `Pack · ${count} maps` : 'Single map';
  }

  diskSize(modId: number): string | null {
    const bytes = this.sizeBytes()[modId];
    if (bytes === undefined || bytes <= 0) {
      return null;
    }
    return formatBytes(bytes);
  }

  initial(name: string): string {
    return name.trim().charAt(0).toUpperCase() || '?';
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

  openInfo(mod: CatalogItem): void {
    this.infoItem.set(mod);
    this.infoMapNames.set(null);
    const count = this.mapCounts()[mod.id];
    if (count === null || count === undefined || count <= 1) {
      return;
    }
    this.infoMapNamesLoading.set(true);
    this.ipc
      .request(IPC_MESSAGE.MOD_MAP_NAMES, { id: mod.id })
      .then((reply) => {
        if (!reply.ok || this.infoItem()?.id !== mod.id) {
          return;
        }
        const result = reply.payload as MapNamesResult | undefined;
        this.infoMapNames.set(result?.names ?? []);
      })
      .finally(() => {
        this.infoMapNamesLoading.set(false);
      });
  }

  closeInfo(): void {
    this.infoItem.set(null);
    this.infoMapNames.set(null);
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
    this.browser.openDownloads('maps');
  }

  openFolder(mod: CatalogItem): void {
    const path = this.folders()[mod.id];
    if (!path) {
      return;
    }
    this.browser.openFolder(path);
  }

  download(mod: CatalogItem): void {
    if (this.isDownloading(mod.id)) {
      return;
    }
    this.downloadingIds.update((ids) => new Set(ids).add(mod.id));

    this.ipc
      .request(IPC_MESSAGE.MOD_DOWNLOAD, { id: mod.id })
      .then((reply) => {
        if (!reply.ok) {
          this.setNote(mod.id, reply.error ?? 'Download failed.');
          this.downloadedIds.update((ids) => {
            const next = new Set(ids);
            next.delete(mod.id);
            return next;
          });
          this.loadLocal();
          return;
        }
        this.downloadedIds.update((ids) => new Set(ids).add(mod.id));
        this.setNote(mod.id, 'Downloaded. You can Apply.');
        this.loadLocal();
        if (this.isLibrary()) {
          this.loadLibrary();
        }
      })
      .catch((error: unknown) => {
        this.setNote(mod.id, error instanceof Error ? error.message : 'Download failed.');
        this.loadLocal();
      })
      .finally(() => {
        this.downloadingIds.update((ids) => {
          const next = new Set(ids);
          next.delete(mod.id);
          return next;
        });
        this.downloadActivity.clear('maps', mod.id);
      });
  }

  cancelDownload(mod: CatalogItem): void {
    this.downloadActivity.cancel('maps', mod.id);
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
          this.setNote(mod.id, result.reason ?? 'Applied.');
          void this.loadout.refresh().then(() => {
            if (this.libraryFilter() === 'applied') {
              this.loadLibrary();
            }
          });
          return;
        }
        this.setNote(mod.id, result?.reason ?? 'Apply did not change any files.');
      })
      .catch((error: unknown) => {
        this.setNote(mod.id, error instanceof Error ? error.message : 'Apply failed.');
      })
      .finally(() => {
        this.applyingId.set(null);
      });
  }

  reset(mod: CatalogItem): void {
    if (this.isBusy() || !this.isActive(mod.id)) {
      return;
    }
    this.resettingId.set(mod.id);
    this.ipc
      .request(IPC_MESSAGE.MOD_RESET, { id: mod.id })
      .then((reply) => {
        if (!reply.ok) {
          this.setNote(mod.id, reply.error ?? 'Reset failed.');
          return;
        }
        const result = reply.payload as ApplyAttempt | undefined;
        this.setNote(mod.id, result?.reason ?? 'Removed.');
        void this.loadout.refresh().then(() => {
          if (this.libraryFilter() === 'applied') {
            this.loadLibrary();
          }
        });
      })
      .catch((error: unknown) => {
        this.setNote(mod.id, error instanceof Error ? error.message : 'Reset failed.');
      })
      .finally(() => {
        this.resettingId.set(null);
      });
  }

  /** Frees disk space. Safe even if the mod is currently applied: Reapply just re-downloads it. */
  deleteDownload(mod: CatalogItem): void {
    if (this.isBusy()) {
      return;
    }
    if (!confirmDeleteDownload(mod.name, this.sizeBytes()[mod.id])) {
      return;
    }
    this.deletingId.set(mod.id);
    this.ipc
      .request(IPC_MESSAGE.DOWNLOADS_DELETE, { kind: 'maps', id: mod.id })
      .then((reply) => {
        if (!reply.ok) {
          this.setNote(mod.id, reply.error ?? 'Delete failed.');
          return;
        }
        this.downloadedIds.update((ids) => {
          const next = new Set(ids);
          next.delete(mod.id);
          return next;
        });
        this.folders.update((map) => {
          const { [mod.id]: _folder, ...rest } = map;
          return rest;
        });
        this.mapCounts.update((counts) => {
          const { [mod.id]: _removed, ...rest } = counts;
          return rest;
        });
        this.sizeBytes.update((sizes) => {
          const { [mod.id]: _size, ...rest } = sizes;
          return rest;
        });
        this.setNote(mod.id, 'Removed from disk.');
        if (this.isLibrary()) {
          this.loadLibrary();
        }
      })
      .catch((error: unknown) => {
        this.setNote(mod.id, error instanceof Error ? error.message : 'Delete failed.');
      })
      .finally(() => {
        this.deletingId.set(null);
      });
  }

  private setNote(modId: number, message: string): void {
    this.cardNote.update((notes) => ({ ...notes, [modId]: message }));
  }

  private loadLocal(): void {
    this.ipc.request(IPC_MESSAGE.DOWNLOADS_LIST, { kind: 'maps' }).then((reply) => {
      if (!reply.ok) {
        return;
      }
      const data = reply.payload as LocalDownloadList | undefined;
      const items = data?.items ?? [];
      this.downloadedIds.set(new Set(items.map((item) => item.id)));
      const counts: Record<number, number | null> = {};
      const sizes: Record<number, number> = {};
      const folders: Record<number, string> = {};
      for (const item of items) {
        folders[item.id] = item.folder;
        counts[item.id] = item.mapCount ?? null;
        if (item.sizeBytes !== undefined) {
          sizes[item.id] = item.sizeBytes;
        }
      }
      this.folders.set(folders);
      this.mapCounts.set(counts);
      this.sizeBytes.set(sizes);
    });
  }

  private loadLibrary(): void {
    const ids =
      this.libraryFilter() === 'applied'
        ? [...this.loadout.activeMapIds()]
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
      .request(IPC_MESSAGE.CATALOG_BY_IDS, { kind: 'maps', ids })
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
      .request(IPC_MESSAGE.CATALOG_MAPS, {
        page,
        query: this.query(),
        sort: this.sort(),
      })
      .then((reply) => {
        if (token !== this.loadToken) {
          return;
        }
        if (!reply.ok) {
          this.error.set(reply.error ?? 'Could not load maps.');
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
        this.error.set(error instanceof Error ? error.message : 'Could not load maps.');
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
