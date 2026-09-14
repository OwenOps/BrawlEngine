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
import { LIBRARY_FILTERS, LibraryFilter, libraryFollowsLoadout, libraryIds, libraryStatusLabel, parseCatalogPage, pinLibraryCards, showBackToPage as catalogShowBackToPage } from '../../core/catalog/library-filter';
import { NewDownloadsService } from '../../core/catalog/new-downloads.service';
import { ApplySelectedDialogComponent } from '../../core/catalog/apply-selected-dialog.component';
import { AddModDialogComponent } from '../../core/catalog/add-mod-dialog.component';
import { AuthorCreditComponent } from '../../core/catalog/author-credit.component';
import { AuthorFilter, matchesAuthor } from '../../core/catalog/author-filter';
import { AddModMode } from '../../core/catalog/gamebanana-id';
import { ApplySelectedRow, applySelectedDone, applySelectedRows } from '../../core/catalog/apply-selected';
import { NsfwSettings } from '../../core/catalog/nsfw-settings';
import { ThumbSrcPipe } from '../../core/catalog/thumb-src.pipe';
import { scrollMainToTop } from '../../core/ui/scroll-main';
import { BrowserService } from '../../core/browser/browser.service';
import { formatBytes, confirmDeleteDownload } from '../../core/download/format-bytes';
import { ApplyActivityService } from '../../core/apply/apply-activity.service';
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
import { LikesService } from '../../core/likes/likes.service';
import { CrashesService } from '../../core/crashes/crashes.service';
import { GAMEBANANA_WAIT, LIBRARY_WAIT } from '../../core/ui/app-shell.constants';

@Component({
  selector: 'app-maps-page',
  standalone: true,
  imports: [ThumbSrcPipe, ApplySelectedDialogComponent, AddModDialogComponent, AuthorCreditComponent],
  templateUrl: './maps-page.component.html',
  styleUrl: './maps-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MapsPageComponent implements OnDestroy {
  private readonly ipc = inject(IpcService);
  private readonly loadout = inject(LoadoutService);
  private readonly likes = inject(LikesService);
  private readonly crashes = inject(CrashesService);
  readonly nsfw = inject(NsfwSettings);
  private readonly browser = inject(BrowserService);
  readonly downloadActivity = inject(DownloadActivityService);
  readonly applyActivity = inject(ApplyActivityService);
  readonly newDownloads = inject(NewDownloadsService);
  readonly gameBananaWait = GAMEBANANA_WAIT;
  readonly libraryWait = LIBRARY_WAIT;
  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  private loadToken = 0;

  readonly sorts = CATALOG_SORTS;
  readonly libraryFilters = LIBRARY_FILTERS;
  readonly libraryStatusLabel = libraryStatusLabel;
  readonly libraryFilter = signal<LibraryFilter>('disk');
  readonly authorFilter = signal<AuthorFilter | null>(null);
  readonly queryInput = signal('');
  readonly query = signal('');
  readonly sort = signal<CatalogSort>('newest');
  readonly items = signal<CatalogItem[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly page = signal(1);
  readonly lastPage = signal(1);
  readonly totalPages = signal(1);
  readonly totalCount = signal(0);
  readonly downloadingIds = signal<ReadonlySet<number>>(new Set());
  readonly applyingId = signal<number | null>(null);
  readonly resettingId = signal<number | null>(null);
  readonly deletingId = signal<number | null>(null);
  readonly importing = signal(false);
  readonly cardNote = signal<Record<number, string>>({});
  readonly infoItem = signal<CatalogItem | null>(null);
  readonly infoMapNames = signal<string[] | null>(null);
  readonly infoMapNamesLoading = signal(false);
  readonly applySelectedOpen = signal(false);
  readonly applySelectedRows = signal<ApplySelectedRow[]>([]);
  readonly applySelectedStatus = signal<string | null>(null);
  readonly applySelectedRunning = signal(false);
  readonly addOpen = signal(false);
  readonly addMode = signal<AddModMode>('zip');
  private readonly downloadedIds = signal<ReadonlySet<number>>(new Set());
  private readonly folders = signal<Record<number, string>>({});
  private readonly downloadedAt = signal<Record<number, string>>({});
  private readonly mapCounts = signal<Record<number, number | null>>({});
  private readonly sizeBytes = signal<Record<number, number>>({});
  readonly isBusy = computed(
    () =>
      this.applyingId() !== null ||
      this.resettingId() !== null ||
      this.deletingId() !== null ||
      this.importing() ||
      this.applySelectedRunning() ||
      this.loadout.busy(),
  );
  readonly catalogBusy = computed(() => {
    if (this.loading() && this.shownItems().length > 0 && !this.isLibrary()) {
      return this.gameBananaWait;
    }
    return null;
  });
  readonly isLibrary = computed(() => this.libraryFilter() !== 'all');
  readonly diskCount = computed(() => this.downloadedIds().size);
  readonly showBackToPage = computed(() =>
    catalogShowBackToPage(this.lastPage(), this.page(), this.isLibrary()),
  );
  readonly shownItems = computed(() => {
    let list = this.items();
    if (!this.isLibrary() && !this.nsfw.show()) {
      list = list.filter((item) => !item.nsfw);
    }
    const author = this.authorFilter();
    if (author) {
      list = list.filter((item) => matchesAuthor(item, author));
    }
    if (!this.isLibrary()) {
      return pinLibraryCards(list, this.likes.mapIds(), (id) => this.isNew(id));
    }
    const query = this.queryInput().trim().toLowerCase();
    if (query) {
      list = list.filter((item) => item.name.toLowerCase().includes(query));
    }
    return pinLibraryCards(list, this.likes.mapIds(), (id) => this.isNew(id));
  });

  constructor() {
    this.loadLocal();
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

  isNew(modId: number): boolean {
    return this.newDownloads.isNew('maps', modId, this.downloadedAt()[modId]);
  }

  isLiked(modId: number): boolean {
    return this.likes.has('maps', modId);
  }

  toggleLike(mod: CatalogItem, event: Event): void {
    event.stopPropagation();
    const reload = this.libraryFilter() === 'liked';
    this.likes.toggle('maps', mod).then(() => {
      if (reload) {
        this.loadLibrary();
      }
    });
  }

  isCrashTagged(modId: number): boolean {
    return this.crashes.has('maps', modId);
  }

  crashNote(modId: number): string | null {
    return this.crashes.note('maps', modId);
  }

  toggleCrash(mod: CatalogItem, event: Event): void {
    event.stopPropagation();
    if (this.crashes.has('maps', mod.id)) {
      void this.crashes.toggle('maps', mod.id);
      return;
    }
    const note = this.crashes.askNote();
    if (note === undefined) {
      return;
    }
    void this.crashes.toggle('maps', mod.id, note);
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
    this.leaveOnDisk();
    this.libraryFilter.set(id);
    if (id === 'all') {
      this.load(1, false);
      return;
    }
    this.loadLibrary();
  }

  moreFromAuthor(item: CatalogItem): void {
    if (!item.authorId && !item.author) {
      return;
    }

    this.closeInfo();
    this.authorFilter.set({ id: item.authorId ?? 0, name: item.author });
    this.queryInput.set('');
    this.query.set('');
    if (item.authorId) {
      this.leaveOnDisk();
      this.libraryFilter.set('all');
      this.load(1);
    }
  }

  clearAuthor(): void {
    if (!this.authorFilter()) {
      return;
    }
    this.authorFilter.set(null);
    if (!this.isLibrary()) {
      this.load(1);
    }
  }

  backToLastPage(): void {
    const last = this.lastPage();
    if (this.loading() || last <= 1) {
      return;
    }
    this.libraryFilter.set('all');
    this.load(last);
    scrollMainToTop();
  }

  goToPage(page: number): void {
    if (this.loading() || page < 1 || page > this.totalPages() || page === this.page()) {
      return;
    }
    this.load(page);
    scrollMainToTop();
  }

  jumpToPage(event: Event): void {
    const input = event.target as HTMLInputElement;
    const next = parseCatalogPage(input.value, this.totalPages());
    if (next === null) {
      input.value = String(this.page());
      return;
    }

    input.value = String(next);
    this.goToPage(next);
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
    if (this.addOpen()) {
      this.closeAdd();
      return;
    }
    if (this.applySelectedOpen()) {
      this.closeApplySelected();
      return;
    }
    if (this.infoItem()) {
      this.closeInfo();
    }
  }

  openProfile(url: string): void {
    if (!url.trim()) {
      return;
    }
    this.browser.open(url);
  }

  openDownloadsFolder(): void {
    this.browser.openDownloads('maps');
  }

  openAddZip(): void {
    this.openAdd('zip');
  }

  openAddUrl(): void {
    this.openAdd('url');
  }

  closeAdd(): void {
    this.addOpen.set(false);
  }

  confirmAdd(id: number): void {
    this.addOpen.set(false);
    if (this.addMode() === 'zip') {
      this.importFromDisk(id);
      return;
    }
    this.startDownload(id);
  }

  private openAdd(mode: AddModMode): void {
    if (this.isBusy()) {
      return;
    }
    this.addMode.set(mode);
    this.addOpen.set(true);
  }

  private importFromDisk(id: number): void {
    this.importing.set(true);
    this.browser.importZip('maps', id).then((status) => {
      if (status === 'ok') {
        this.setNote(id, 'Copied from disk. You can Apply.');
        this.newDownloads.forget('maps', id);
        this.loadLocal();
        this.downloadActivity.notifyInventoryChanged();
      } else if (status === 'fail') {
        this.setNote(id, 'Could not copy those files.');
      }
    }).finally(() => this.importing.set(false));
  }

  openFolder(mod: CatalogItem): void {
    const path = this.folders()[mod.id];
    if (!path) {
      return;
    }
    void this.browser.openFolder(path).then((error) => {
      if (error) {
        this.setNote(mod.id, error);
      }
    });
  }

  download(mod: CatalogItem): void {
    this.startDownload(mod.id);
  }

  private startDownload(id: number): void {
    if (this.isDownloading(id)) {
      return;
    }
    this.downloadingIds.update((ids) => new Set(ids).add(id));

    this.ipc
      .request(IPC_MESSAGE.MOD_DOWNLOAD, { id })
      .then((reply) => {
        if (!reply.ok) {
          this.setNote(id, reply.error ?? 'Download failed.');
          this.downloadedIds.update((ids) => {
            const next = new Set(ids);
            next.delete(id);
            return next;
          });
          this.loadLocal();
          return;
        }
        this.downloadedIds.update((ids) => new Set(ids).add(id));
        this.setNote(id, 'Downloaded. You can Apply.');
        this.newDownloads.forget('maps', id);
        this.loadLocal();
        if (this.isLibrary()) {
          this.loadLibrary();
        }
      })
      .catch((error: unknown) => {
        this.setNote(id, error instanceof Error ? error.message : 'Download failed.');
        this.loadLocal();
      })
      .finally(() => {
        this.downloadingIds.update((ids) => {
          const next = new Set(ids);
          next.delete(id);
          return next;
        });
        this.downloadActivity.clear('maps', id);
        this.downloadActivity.notifyInventoryChanged();
      });
  }

  cancelDownload(mod: CatalogItem): void {
    this.downloadActivity.cancel('maps', mod.id);
  }

  apply(mod: CatalogItem): void {
    if (this.isBusy()) {
      return;
    }
    void this.applyOne(mod.id);
  }

  openApplySelected(): void {
    if (this.isBusy()) {
      return;
    }

    const ids = [...this.downloadedIds()];
    if (ids.length === 0) {
      return;
    }

    this.applySelectedOpen.set(true);
    this.applySelectedRows.set([]);
    this.applySelectedStatus.set('Loading…');
    this.ipc.request(IPC_MESSAGE.CATALOG_BY_IDS, { kind: 'maps', ids }).then((reply) => {
      if (!this.applySelectedOpen()) {
        return;
      }
      const data = reply.ok ? (reply.payload as CatalogPage | undefined) : undefined;
      this.applySelectedRows.set(
        applySelectedRows(ids, data?.items ?? [], this.crashes.ids('maps')),
      );
      this.applySelectedStatus.set(null);
    });
  }

  closeApplySelected(): void {
    if (this.applySelectedRunning()) {
      return;
    }
    this.applySelectedOpen.set(false);
    this.applySelectedStatus.set(null);
  }

  runApplySelected(ids: number[]): void {
    if (this.isBusy() || ids.length === 0) {
      return;
    }
    void this.applyMany(ids);
  }

  private async applyMany(ids: number[]): Promise<void> {
    this.applySelectedRunning.set(true);
    const names = new Map(this.applySelectedRows().map((row) => [row.id, row.name]));
    const failed: string[] = [];
    let okCount = 0;
    try {
      for (let index = 0; index < ids.length; index++) {
        const id = ids[index];
        this.applySelectedStatus.set(
          `Applying ${index + 1} of ${ids.length}: ${names.get(id) ?? id}`,
        );
        const ok = await this.applyOne(id, false);
        if (ok) {
          okCount += 1;
        } else {
          failed.push(names.get(id) ?? String(id));
        }
      }
      this.applySelectedStatus.set(applySelectedDone(okCount, failed));
      await this.loadout.refresh();
      if (libraryFollowsLoadout(this.libraryFilter())) {
        this.loadLibrary();
      }
      if (failed.length === 0) {
        this.applySelectedOpen.set(false);
      }
    } finally {
      this.applySelectedRunning.set(false);
    }
  }

  private applyOne(modId: number, refreshLoadout = true): Promise<boolean> {
    this.applyingId.set(modId);
    return this.ipc
      .request(IPC_MESSAGE.MOD_APPLY, { id: modId })
      .then((reply) => {
        if (!reply.ok) {
          this.setNote(modId, reply.error ?? 'Apply failed.');
          return false;
        }
        const result = reply.payload as ApplyAttempt | undefined;
        if (result?.applied) {
          this.setNote(modId, result.reason ?? 'Applied.');
          if (refreshLoadout) {
            void this.loadout.refresh().then(() => {
              if (libraryFollowsLoadout(this.libraryFilter())) {
                this.loadLibrary();
              }
            });
          }
          return true;
        }
        this.setNote(modId, result?.reason ?? 'Apply did not change any files.');
        return true;
      })
      .catch((error: unknown) => {
        const message = error instanceof Error ? error.message : 'Apply failed.';
        this.setNote(modId, message);
        return false;
      })
      .finally(() => {
        this.applyingId.set(null);
        this.applyActivity.clear('maps', modId);
      });
  }

  reset(mod: CatalogItem): void {
    void this.resetApplied(mod);
  }

  /** Frees disk space. If the mod is applied, a second confirm can also Reset it in the game. */
  deleteDownload(mod: CatalogItem): void {
    if (this.isBusy()) {
      return;
    }
    const choice = confirmDeleteDownload(mod.name, this.sizeBytes()[mod.id], this.isActive(mod.id));
    if (!choice.proceed) {
      return;
    }
    if (choice.alsoReset && this.isActive(mod.id)) {
      void this.resetApplied(mod).then((ok) => {
        if (ok) {
          this.removeDownloadFolder(mod, true);
        }
      });
      return;
    }
    this.removeDownloadFolder(mod, false);
  }

  private resetApplied(mod: CatalogItem): Promise<boolean> {
    if (this.isBusy() || !this.isActive(mod.id)) {
      return Promise.resolve(false);
    }
    this.resettingId.set(mod.id);
    return this.ipc
      .request(IPC_MESSAGE.MOD_RESET, { id: mod.id })
      .then((reply) => {
        if (!reply.ok) {
          this.setNote(mod.id, reply.error ?? 'Reset failed.');
          return false;
        }
        const result = reply.payload as ApplyAttempt | undefined;
        this.setNote(mod.id, result?.reason ?? 'Removed.');
        return this.loadout.refresh().then(() => {
          if (libraryFollowsLoadout(this.libraryFilter())) {
            this.loadLibrary();
          }
          return true;
        });
      })
      .catch((error: unknown) => {
        this.setNote(mod.id, error instanceof Error ? error.message : 'Reset failed.');
        return false;
      })
      .finally(() => {
        this.resettingId.set(null);
        this.applyActivity.clear('maps', mod.id);
      });
  }

  private removeDownloadFolder(mod: CatalogItem, alsoReset: boolean): void {
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
        this.setNote(
          mod.id,
          alsoReset
            ? 'Reset in the game and removed from disk.'
            : this.isActive(mod.id)
              ? 'Removed from disk. Still applied in the game.'
              : 'Removed from disk.',
        );
        if (this.isLibrary()) {
          this.loadLibrary();
        }
        this.downloadActivity.notifyInventoryChanged();
      })
      .catch((error: unknown) => {
        this.setNote(mod.id, error instanceof Error ? error.message : 'Delete failed.');
      })
      .finally(() => {
        this.deletingId.set(null);
      });
  }

  private leaveOnDisk(): void {
    if (this.libraryFilter() !== 'disk') {
      return;
    }

    this.newDownloads.markSeen(
      'maps',
      [...this.downloadedIds()].filter((id) => this.isNew(id)),
    );
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
      const times: Record<number, string> = {};
      for (const item of items) {
        folders[item.id] = item.folder;
        counts[item.id] = item.mapCount ?? null;
        if (item.sizeBytes !== undefined) {
          sizes[item.id] = item.sizeBytes;
        }
        if (item.downloadedUtc) {
          times[item.id] = item.downloadedUtc;
        }
      }
      this.folders.set(folders);
      this.mapCounts.set(counts);
      this.sizeBytes.set(sizes);
      this.downloadedAt.set(times);
      if (this.isLibrary()) {
        this.loadLibrary();
      }
    });
  }

  private loadLibrary(): void {
    const ids = libraryIds(
      this.libraryFilter(),
      this.downloadedIds(),
      this.loadout.activeMapIds(),
      this.likes.mapIds(),
    );
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

  private load(page: number, rememberPage = true): void {
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
        authorId: this.authorFilter()?.id ?? 0,
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
        if (rememberPage) {
          this.lastPage.set(data.page);
        }
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
