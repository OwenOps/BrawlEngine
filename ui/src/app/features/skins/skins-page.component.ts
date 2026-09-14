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
import { ApplySelectedDialogComponent } from '../../core/catalog/apply-selected-dialog.component';
import { AddModDialogComponent } from '../../core/catalog/add-mod-dialog.component';
import { AuthorCreditComponent } from '../../core/catalog/author-credit.component';
import { AuthorFilter, matchesAuthor } from '../../core/catalog/author-filter';
import { AddModMode } from '../../core/catalog/gamebanana-id';
import { ApplySelectedRow, applySelectedDone, applySelectedRows } from '../../core/catalog/apply-selected';
import { LIBRARY_FILTERS, LibraryFilter, libraryFollowsLoadout, libraryIds, libraryStatusLabel, parseCatalogPage, pinLibraryCards, showBackToPage as catalogShowBackToPage } from '../../core/catalog/library-filter';
import { NewDownloadsService } from '../../core/catalog/new-downloads.service';
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
import { DownloadResult, LocalDownloadList } from '../../core/ipc/contracts/download.contracts';
import { FfdecTools } from '../../core/ipc/contracts/ffdec.contracts';
import { IPC_MESSAGE } from '../../core/ipc/ipc.constants';
import { IpcEnvelope, IpcService } from '../../core/ipc/ipc.service';
import { LoadoutService } from '../../core/loadout/loadout.service';
import { LikesService } from '../../core/likes/likes.service';
import { CrashesService } from '../../core/crashes/crashes.service';
import { GAMEBANANA_WAIT, LIBRARY_WAIT } from '../../core/ui/app-shell.constants';
import { SKIN_LEGENDS } from './skin-legends';

@Component({
  selector: 'app-skins-page',
  standalone: true,
  imports: [ThumbSrcPipe, ApplySelectedDialogComponent, AddModDialogComponent, AuthorCreditComponent],
  templateUrl: './skins-page.component.html',
  styleUrl: './skins-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SkinsPageComponent implements OnDestroy {
  private readonly ipc = inject(IpcService);
  private readonly browser = inject(BrowserService);
  private readonly loadout = inject(LoadoutService);
  private readonly likes = inject(LikesService);
  private readonly crashes = inject(CrashesService);
  readonly nsfw = inject(NsfwSettings);
  readonly downloadActivity = inject(DownloadActivityService);
  readonly applyActivity = inject(ApplyActivityService);
  readonly newDownloads = inject(NewDownloadsService);
  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  private loadToken = 0;

  readonly sorts = CATALOG_SORTS;
  readonly libraryFilters = LIBRARY_FILTERS;
  readonly libraryStatusLabel = libraryStatusLabel;
  readonly libraryFilter = signal<LibraryFilter>('disk');
  readonly authorFilter = signal<AuthorFilter | null>(null);
  readonly skinLegends = SKIN_LEGENDS;
  readonly legendId = signal(0);
  readonly queryInput = signal('');
  readonly query = signal('');
  readonly sort = signal<CatalogSort>('newest');
  readonly items = signal<CatalogItem[]>([]);
  readonly loading = signal(false);
  readonly targetsBusy = signal(false);
  readonly error = signal<string | null>(null);
  readonly page = signal(1);
  readonly lastPage = signal(1);
  readonly totalPages = signal(1);
  readonly totalCount = signal(0);
  readonly downloadingIds = signal<ReadonlySet<number>>(new Set());
  readonly deletingId = signal<number | null>(null);
  readonly importing = signal(false);
  readonly cardNote = signal<Record<number, string>>({});
  readonly folders = signal<Record<number, string>>({});
  private readonly downloadedAt = signal<Record<number, string>>({});
  private readonly sizeBytes = signal<Record<number, number>>({});
  readonly infoItem = signal<CatalogItem | null>(null);
  readonly applySelectedOpen = signal(false);
  readonly applySelectedRows = signal<ApplySelectedRow[]>([]);
  readonly applySelectedStatus = signal<string | null>(null);
  readonly applySelectedRunning = signal(false);
  readonly addOpen = signal(false);
  readonly addMode = signal<AddModMode>('zip');
  readonly tools = signal<FfdecTools | null>(null);
  readonly toolsLoading = signal(false);
  readonly pickingTools = signal(false);
  readonly applyingId = signal<number | null>(null);
  readonly resettingId = signal<number | null>(null);
  readonly gameBananaWait = GAMEBANANA_WAIT;
  readonly libraryWait = LIBRARY_WAIT;
  private readonly stopSkinTargets: () => void;
  readonly isBusy = computed(
    () =>
      this.deletingId() !== null ||
      this.importing() ||
      this.pickingTools() ||
      this.toolsLoading() ||
      this.applyingId() !== null ||
      this.resettingId() !== null ||
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
  readonly diskCount = computed(() => Object.keys(this.folders()).length);
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
      return pinLibraryCards(list, this.likes.skinIds(), (id) => this.isNew(id));
    }
    const query = this.queryInput().trim().toLowerCase();
    if (query) {
      list = list.filter((item) => item.name.toLowerCase().includes(query));
    }
    const legendId = this.legendId();
    if (legendId !== 0) {
      const legend = this.skinLegends.find((row) => row.id === legendId);
      if (legend) {
        list = list.filter((item) => item.category === legend.label);
      }
    }
    return pinLibraryCards(list, this.likes.skinIds(), (id) => this.isNew(id));
  });

  constructor() {
    this.stopSkinTargets = this.ipc.on(IPC_MESSAGE.CATALOG_SKIN_TARGETS, (msg) =>
      this.applySkinTargets(msg),
    );
    this.loadLocal();
    this.loadTools();
    effect(() => {
      this.downloadActivity.inventoryEpoch();
      untracked(() => this.loadLocal());
    });
  }

  ngOnDestroy(): void {
    this.stopSkinTargets();
    if (this.searchTimer !== undefined) {
      clearTimeout(this.searchTimer);
    }
  }

  isDownloading(id: number): boolean {
    return this.downloadingIds().has(id);
  }

  isDownloaded(id: number): boolean {
    return this.folders()[id] !== undefined;
  }

  isNew(id: number): boolean {
    return this.newDownloads.isNew('skins', id, this.downloadedAt()[id]);
  }

  isLiked(id: number): boolean {
    return this.likes.has('skins', id);
  }

  toggleLike(item: CatalogItem, event: Event): void {
    event.stopPropagation();
    const reload = this.libraryFilter() === 'liked';
    this.likes.toggle('skins', item).then(() => {
      if (reload) {
        this.loadLibrary();
      }
    });
  }

  isCrashTagged(id: number): boolean {
    return this.crashes.has('skins', id);
  }

  crashNote(id: number): string | null {
    return this.crashes.note('skins', id);
  }

  toggleCrash(item: CatalogItem, event: Event): void {
    event.stopPropagation();
    if (this.crashes.has('skins', item.id)) {
      void this.crashes.toggle('skins', item.id);
      return;
    }
    const note = this.crashes.askNote();
    if (note === undefined) {
      return;
    }
    void this.crashes.toggle('skins', item.id, note);
  }

  isActive(id: number): boolean {
    return this.loadout.activeSkinIds().has(id);
  }

  downloadLabel(id: number): string {
    const percent = this.downloadActivity.percentFor('skins', id);
    return percent === null ? 'Downloading…' : `Downloading ${percent}%…`;
  }

  diskSize(id: number): string | null {
    const bytes = this.sizeBytes()[id];
    if (bytes === undefined || bytes <= 0) {
      return null;
    }
    return formatBytes(bytes);
  }

  skinTargetLine(item: CatalogItem): string | null {
    const target = item.skinTarget?.trim();
    if (!target) {
      return null;
    }

    return target === 'Default' ? 'Default skin' : 'Needs ' + target;
  }

  canSearchSkinTarget(item: CatalogItem): boolean {
    const target = item.skinTarget?.trim();
    return !!target && target !== 'Default';
  }

  openSkinTargetImages(item: CatalogItem, event: Event): void {
    event.stopPropagation();
    const target = item.skinTarget?.trim();
    if (!target || target === 'Default') {
      return;
    }
    this.browser.openSkinImages(target);
  }

  private applySkinTargets(msg: IpcEnvelope): void {
    const data = msg.payload as
      | { items?: { id: number; skinTarget?: string | null; description?: string | null }[] }
      | undefined;
    const rows = data?.items ?? [];
    if (rows.length === 0) {
      this.targetsBusy.set(false);
      return;
    }

    const byId = new Map(
      rows.map((row) => [
        row.id,
        { skinTarget: row.skinTarget ?? null, description: row.description ?? null },
      ]),
    );
    this.items.update((list) =>
      list.map((item) => {
        const extra = byId.get(item.id);
        return extra
          ? { ...item, skinTarget: extra.skinTarget, description: extra.description ?? item.description }
          : item;
      }),
    );
    this.targetsBusy.set(false);
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

  onLegendFilter(event: Event): void {
    const id = Number((event.target as HTMLSelectElement).value);
    if (!Number.isFinite(id)) {
      return;
    }
    this.setLegendFilter(id);
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

  setLegendFilter(id: number): void {
    if (this.legendId() === id) {
      return;
    }
    this.legendId.set(id);
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

  openInfo(item: CatalogItem): void {
    this.infoItem.set(item);
  }

  closeInfo(): void {
    this.infoItem.set(null);
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
    this.browser.openDownloads('skins');
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
    this.browser.importZip('skins', id).then((status) => {
      if (status === 'ok') {
        this.setNote(id, 'Copied from disk. You can Apply.');
        this.newDownloads.forget('skins', id);
        this.loadLocal();
        this.downloadActivity.notifyInventoryChanged();
      } else if (status === 'fail') {
        this.setNote(id, 'Could not copy those files.');
      }
    }).finally(() => this.importing.set(false));
  }

  pickJava(): void {
    this.pickTools('java');
  }

  retryTools(): void {
    this.loadTools();
  }

  private pickTools(kind: 'java'): void {
    if (this.pickingTools()) {
      return;
    }
    this.pickingTools.set(true);
    this.ipc
      .request(IPC_MESSAGE.SKINS_TOOLS_PICK, { kind })
      .then((reply) => {
        if (!reply.ok) {
          return;
        }
        this.tools.set((reply.payload as FfdecTools | undefined) ?? null);
      })
      .finally(() => {
        this.pickingTools.set(false);
      });
  }

  download(item: CatalogItem): void {
    this.startDownload(item.id);
  }

  private startDownload(id: number): void {
    if (this.isDownloading(id)) {
      return;
    }
    this.downloadingIds.update((ids) => new Set(ids).add(id));
    this.ipc
      .request(IPC_MESSAGE.SKIN_DOWNLOAD, { id })
      .then((reply) => {
        if (!reply.ok) {
          this.setNote(id, reply.error ?? 'Download failed.');
          this.folders.update((map) => {
            const next = { ...map };
            delete next[id];
            return next;
          });
          this.loadLocal();
          return;
        }
        const result = reply.payload as DownloadResult | undefined;
        if (result?.folder) {
          this.folders.update((map) => ({ ...map, [id]: result.folder }));
        }
        this.setNote(id, 'Downloaded. Apply writes sprites into the game SWFs.');
        this.newDownloads.forget('skins', id);
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
        this.downloadActivity.clear('skins', id);
        this.downloadActivity.notifyInventoryChanged();
      });
  }

  cancelDownload(item: CatalogItem): void {
    this.downloadActivity.cancel('skins', item.id);
  }

  openFolder(item: CatalogItem): void {
    const path = this.folders()[item.id];
    if (!path) {
      return;
    }
    void this.browser.openFolder(path).then((error) => {
      if (error) {
        this.setNote(item.id, error);
      }
    });
  }

  apply(item: CatalogItem): void {
    if (this.isBusy()) {
      return;
    }
    void this.applyOne(item.id);
  }

  openApplySelected(): void {
    if (this.isBusy()) {
      return;
    }

    const ids = Object.keys(this.folders()).map((id) => Number(id));
    if (ids.length === 0) {
      return;
    }

    this.applySelectedOpen.set(true);
    this.applySelectedRows.set([]);
    this.applySelectedStatus.set('Loading…');
    this.ipc.request(IPC_MESSAGE.CATALOG_BY_IDS, { kind: 'skins', ids }).then((reply) => {
      if (!this.applySelectedOpen()) {
        return;
      }
      const data = reply.ok ? (reply.payload as CatalogPage | undefined) : undefined;
      this.applySelectedRows.set(
        applySelectedRows(ids, data?.items ?? [], this.crashes.ids('skins')),
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

  private applyOne(id: number, refreshLoadout = true): Promise<boolean> {
    this.applyingId.set(id);
    return this.ipc
      .request(IPC_MESSAGE.SKIN_APPLY, { id })
      .then((reply) => {
        if (!reply.ok) {
          this.setNote(id, reply.error ?? 'Apply failed.');
          return false;
        }
        const result = reply.payload as ApplyAttempt | undefined;
        if (result?.applied) {
          this.setNote(id, result.reason ?? 'Applied.');
          if (refreshLoadout) {
            void this.loadout.refresh().then(() => {
              if (libraryFollowsLoadout(this.libraryFilter())) {
                this.loadLibrary();
              }
            });
          }
          return true;
        }
        this.setNote(id, result?.reason ?? 'Apply did not change any files.');
        return true;
      })
      .catch((error: unknown) => {
        const message = error instanceof Error ? error.message : 'Apply failed.';
        this.setNote(id, message);
        return false;
      })
      .finally(() => {
        this.applyingId.set(null);
        this.applyActivity.clear('skins', id);
      });
  }

  reset(item: CatalogItem): void {
    void this.resetApplied(item);
  }

  deleteDownload(item: CatalogItem): void {
    if (this.isBusy()) {
      return;
    }
    const choice = confirmDeleteDownload(item.name, this.sizeBytes()[item.id], this.isActive(item.id));
    if (!choice.proceed) {
      return;
    }
    if (choice.alsoReset && this.isActive(item.id)) {
      void this.resetApplied(item).then((ok) => {
        if (ok) {
          this.removeDownloadFolder(item, true);
        }
      });
      return;
    }
    this.removeDownloadFolder(item, false);
  }

  private resetApplied(item: CatalogItem): Promise<boolean> {
    if (this.isBusy() || !this.isActive(item.id)) {
      return Promise.resolve(false);
    }
    this.resettingId.set(item.id);
    return this.ipc
      .request(IPC_MESSAGE.SKIN_RESET, { id: item.id })
      .then((reply) => {
        if (!reply.ok) {
          this.setNote(item.id, reply.error ?? 'Reset failed.');
          return false;
        }
        const result = reply.payload as ApplyAttempt | undefined;
        this.setNote(item.id, result?.reason ?? 'Removed.');
        return this.loadout.refresh().then(() => {
          if (libraryFollowsLoadout(this.libraryFilter())) {
            this.loadLibrary();
          }
          return true;
        });
      })
      .catch((error: unknown) => {
        this.setNote(item.id, error instanceof Error ? error.message : 'Reset failed.');
        return false;
      })
      .finally(() => {
        this.resettingId.set(null);
        this.applyActivity.clear('skins', item.id);
      });
  }

  private removeDownloadFolder(item: CatalogItem, alsoReset: boolean): void {
    this.deletingId.set(item.id);
    this.ipc
      .request(IPC_MESSAGE.DOWNLOADS_DELETE, { kind: 'skins', id: item.id })
      .then((reply) => {
        if (!reply.ok) {
          this.setNote(item.id, reply.error ?? 'Delete failed.');
          return;
        }
        this.folders.update((map) => {
          const { [item.id]: _removed, ...rest } = map;
          return rest;
        });
        this.sizeBytes.update((sizes) => {
          const { [item.id]: _size, ...rest } = sizes;
          return rest;
        });
        this.setNote(
          item.id,
          alsoReset
            ? 'Reset in the game and removed from disk.'
            : this.isActive(item.id)
              ? 'Removed from disk. Still applied in the game.'
              : 'Removed from disk.',
        );
        if (this.isLibrary()) {
          this.loadLibrary();
        }
        this.downloadActivity.notifyInventoryChanged();
      })
      .catch((error: unknown) => {
        this.setNote(item.id, error instanceof Error ? error.message : 'Delete failed.');
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
      'skins',
      Object.keys(this.folders())
        .map((id) => Number(id))
        .filter((id) => this.isNew(id)),
    );
  }

  private setNote(id: number, message: string): void {
    this.cardNote.update((notes) => ({ ...notes, [id]: message }));
  }

  private loadTools(): void {
    if (this.toolsLoading()) {
      return;
    }
    this.toolsLoading.set(true);
    this.ipc
      .request(IPC_MESSAGE.SKINS_TOOLS)
      .then((reply) => {
        if (!reply.ok) {
          this.tools.set({
            ready: false,
            javaPath: null,
            jarPath: null,
            error: reply.error ?? 'Could not check Java / ffdec_lib.jar.',
          });
          return;
        }
        this.tools.set((reply.payload as FfdecTools | undefined) ?? null);
      })
      .catch((error: unknown) => {
        this.tools.set({
          ready: false,
          javaPath: null,
          jarPath: null,
          error: error instanceof Error ? error.message : 'Could not check Java / ffdec_lib.jar.',
        });
      })
      .finally(() => {
        this.toolsLoading.set(false);
      });
  }

  private loadLocal(): void {
    this.ipc.request(IPC_MESSAGE.DOWNLOADS_LIST, { kind: 'skins' }).then((reply) => {
      if (!reply.ok) {
        return;
      }
      const data = reply.payload as LocalDownloadList | undefined;
      const next: Record<number, string> = {};
      const sizes: Record<number, number> = {};
      const times: Record<number, string> = {};
      for (const item of data?.items ?? []) {
        next[item.id] = item.folder;
        if (item.sizeBytes !== undefined) {
          sizes[item.id] = item.sizeBytes;
        }
        if (item.downloadedUtc) {
          times[item.id] = item.downloadedUtc;
        }
      }
      this.folders.set(next);
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
      Object.keys(this.folders()).map((id) => Number(id)),
      this.loadout.activeSkinIds(),
      this.likes.skinIds(),
    );
    const token = ++this.loadToken;
    this.loading.set(true);
    this.targetsBusy.set(false);
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
      .request(IPC_MESSAGE.CATALOG_BY_IDS, { kind: 'skins', ids })
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
    this.targetsBusy.set(true);
    this.error.set(null);

    this.ipc
      .request(IPC_MESSAGE.CATALOG_SKINS, {
        page,
        query: this.query(),
        sort: this.sort(),
        categoryId: this.legendId(),
        authorId: this.authorFilter()?.id ?? 0,
      })
      .then((reply) => {
        if (token !== this.loadToken) {
          return;
        }
        if (!reply.ok) {
          this.error.set(reply.error ?? 'Could not load skins.');
          this.targetsBusy.set(false);
          return;
        }

        const data = reply.payload as CatalogPage | undefined;
        if (!data) {
          this.error.set('Empty catalog response.');
          this.targetsBusy.set(false);
          return;
        }

        this.items.set(data.items);
        this.page.set(data.page);
        if (rememberPage) {
          this.lastPage.set(data.page);
        }
        this.totalCount.set(data.totalCount);
        this.totalPages.set(this.computeTotalPages(data));
        this.targetsBusy.set(data.items.length > 0);
      })
      .catch((error: unknown) => {
        if (token !== this.loadToken) {
          return;
        }
        this.error.set(error instanceof Error ? error.message : 'Could not load skins.');
        this.targetsBusy.set(false);
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
