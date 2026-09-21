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
import { GameLocationState } from '../../core/game/game-location.state';
import { ApplyAttempt, CUSTOM_AUDIO_PROGRESS_ID } from '../../core/ipc/contracts/apply.contracts';
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
import { LikesService } from '../../core/likes/likes.service';
import { CrashesService } from '../../core/crashes/crashes.service';
import { GAMEBANANA_WAIT, LIBRARY_WAIT } from '../../core/ui/app-shell.constants';
import {
  MUSIC_SLOTS,
  asMusicSlot,
  slotHint,
} from './music-slot';
import { SOUND_CATEGORIES } from './sound-categories';

@Component({
  selector: 'app-musics-page',
  standalone: true,
  imports: [ThumbSrcPipe, ApplySelectedDialogComponent, AddModDialogComponent, AuthorCreditComponent],
  templateUrl: './musics-page.component.html',
  styleUrl: './musics-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MusicsPageComponent implements OnDestroy {
  private readonly ipc = inject(IpcService);
  private readonly loadout = inject(LoadoutService);
  private readonly likes = inject(LikesService);
  private readonly crashes = inject(CrashesService);
  readonly nsfw = inject(NsfwSettings);
  private readonly browser = inject(BrowserService);
  private readonly gameLocation = inject(GameLocationState);
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
  readonly lastPage = signal(1);
  readonly totalPages = signal(1);
  readonly totalCount = signal(0);
  readonly downloadingIds = signal<ReadonlySet<number>>(new Set());
  readonly applyingId = signal<number | null>(null);
  readonly resettingId = signal<number | null>(null);
  readonly deletingId = signal<number | null>(null);
  readonly importing = signal(false);
  readonly cardNote = signal<Record<number, string>>({});
  readonly tracks = signal<MusicTrack[]>([]);
  readonly otherChanges = signal<string[]>([]);
  readonly otherChangeCount = signal(0);
  readonly selectedTrack = signal('');
  readonly customNote = signal<string | null>(null);
  readonly audioUrl = signal('');
  readonly replacing = signal(false);
  readonly infoItem = signal<CatalogItem | null>(null);
  readonly applySelectedOpen = signal(false);
  readonly applySelectedRows = signal<ApplySelectedRow[]>([]);
  readonly applySelectedStatus = signal<string | null>(null);
  readonly applySelectedRunning = signal(false);
  readonly addOpen = signal(false);
  readonly addMode = signal<AddModMode>('zip');
  private readonly downloadedIds = signal<ReadonlySet<number>>(new Set());
  private readonly folders = signal<Record<number, string>>({});
  private readonly downloadedAt = signal<Record<number, string>>({});
  private readonly sizeBytes = signal<Record<number, number>>({});
  private readonly categoryById = signal<Record<number, string>>({});
  readonly isBusy = computed(
    () =>
      this.applyingId() !== null ||
      this.resettingId() !== null ||
      this.deletingId() !== null ||
      this.importing() ||
      this.replacing() ||
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
    let list = this.items().map((item) => this.withKnownCategory(item));
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
    if (!this.isLibrary() && !this.nsfw.show()) {
      list = list.filter((item) => !item.nsfw);
    }
    const author = this.authorFilter();
    if (author) {
      list = list.filter((item) => matchesAuthor(item, author));
    }
    return pinLibraryCards(list, this.likes.soundIds(), (id) => this.isNew(id));
  });
  readonly trackGroups = computed(() =>
    MUSIC_SLOTS.map((slot) => ({
      id: slot.id,
      label: slot.label,
      tracks: this.tracks().filter((track) => track.slot === slot.id),
    })).filter((group) => group.tracks.length > 0),
  );
  readonly selectedSlotHint = computed(() => {
    const file = this.selectedTrack();
    const meta = this.tracks().find((track) => track.fileName === file);
    if (!meta) {
      return '';
    }
    return slotHint(asMusicSlot(meta.slot));
  });
  readonly changedTracks = computed(() => this.tracks().filter((track) => track.changed));
  readonly convertStatus = computed(() => {
    if (!this.replacing()) {
      return this.customNote();
    }
    const live = this.applyActivity.labelFor('sounds', CUSTOM_AUDIO_PROGRESS_ID);
    if (live && live !== 'Applying…') {
      return live;
    }
    return this.customNote() ?? 'Replacing…';
  });

  constructor() {
    this.loadLocal();
    effect(() => {
      this.gameLocation.mp3Path();
      untracked(() => this.loadTracks());
    });
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

  isActive(id: number): boolean {
    return this.loadout.activeMusicIds().has(id);
  }

  isDownloading(id: number): boolean {
    return this.downloadingIds().has(id);
  }

  isDownloaded(id: number): boolean {
    return this.downloadedIds().has(id) || this.isActive(id);
  }

  isNew(id: number): boolean {
    return this.newDownloads.isNew('sounds', id, this.downloadedAt()[id]);
  }

  isLiked(id: number): boolean {
    return this.likes.has('sounds', id);
  }

  toggleLike(item: CatalogItem, event: Event): void {
    event.stopPropagation();
    const reload = this.libraryFilter() === 'liked';
    this.likes.toggle('sounds', item).then(() => {
      if (reload) {
        this.loadLibrary();
      }
    });
  }

  isCrashTagged(id: number): boolean {
    return this.crashes.has('sounds', id);
  }

  crashNote(id: number): string | null {
    return this.crashes.note('sounds', id);
  }

  toggleCrash(item: CatalogItem, event: Event): void {
    event.stopPropagation();
    if (this.crashes.has('sounds', item.id)) {
      void this.crashes.toggle('sounds', item.id);
      return;
    }
    const note = this.crashes.askNote();
    if (note === undefined) {
      return;
    }
    void this.crashes.toggle('sounds', item.id, note);
  }

  downloadLabel(id: number): string {
    const percent = this.downloadActivity.percentFor('sounds', id);
    return percent === null ? 'Downloading…' : `Downloading ${percent}%…`;
  }

  initial(name: string): string {
    return name.trim().charAt(0).toUpperCase() || '?';
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
    this.browser.openDownloads('sounds');
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
    this.browser.importZip('sounds', id).then((status) => {
      if (status === 'ok') {
        this.setNote(id, 'Copied from disk. You can Apply.');
        this.newDownloads.forget('sounds', id);
        this.loadLocal();
        this.downloadActivity.notifyInventoryChanged();
      } else if (status === 'fail') {
        this.setNote(id, 'Could not copy those files.');
      }
    }).finally(() => this.importing.set(false));
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

  reloadTracks(): void {
    this.loadTracks();
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
      .request(IPC_MESSAGE.SOUND_DOWNLOAD, { id })
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
        this.newDownloads.forget('sounds', id);
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
        this.downloadActivity.clear('sounds', id);
        this.downloadActivity.notifyInventoryChanged();
      });
  }

  cancelDownload(item: CatalogItem): void {
    this.downloadActivity.cancel('sounds', item.id);
  }

  apply(item: CatalogItem): void {
    if (this.isBusy()) {
      return;
    }
    void this.applyOne(item.id, item.category);
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
    this.ipc.request(IPC_MESSAGE.CATALOG_BY_IDS, { kind: 'sounds', ids }).then((reply) => {
      if (!this.applySelectedOpen()) {
        return;
      }
      const data = reply.ok ? (reply.payload as CatalogPage | undefined) : undefined;
      this.applySelectedRows.set(
        applySelectedRows(ids, data?.items ?? [], this.crashes.ids('sounds')),
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
    const rows = this.applySelectedRows();
    const names = new Map(rows.map((row) => [row.id, row.name]));
    const categories = new Map(rows.map((row) => [row.id, row.category]));
    const failed: string[] = [];
    let okCount = 0;
    try {
      for (let index = 0; index < ids.length; index++) {
        const id = ids[index];
        this.applySelectedStatus.set(
          `Applying ${index + 1} of ${ids.length}: ${names.get(id) ?? id}`,
        );
        const ok = await this.applyOne(id, categories.get(id) ?? '', false);
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
      this.loadTracks();
      if (failed.length === 0) {
        this.applySelectedOpen.set(false);
      }
    } finally {
      this.applySelectedRunning.set(false);
    }
  }

  private applyOne(id: number, category: string, refreshLoadout = true): Promise<boolean> {
    this.applyingId.set(id);
    return this.ipc
      .request(IPC_MESSAGE.MUSIC_APPLY, {
        id,
        category,
        target: this.selectedTrack() || undefined,
      })
      .then((reply) => {
        if (!reply.ok) {
          this.setNote(id, reply.error ?? 'Apply failed.');
          return false;
        }
        const result = reply.payload as ApplyAttempt | undefined;
        this.setNote(id, result?.reason ?? 'Applied.');
        if (refreshLoadout) {
          void this.loadout.refresh().then(() => {
            if (libraryFollowsLoadout(this.libraryFilter())) {
              this.loadLibrary();
            }
          });
          this.loadTracks();
        }
        return true;
      })
      .catch((error: unknown) => {
        const message = error instanceof Error ? error.message : 'Apply failed.';
        this.setNote(id, message);
        return false;
      })
      .finally(() => {
        this.applyingId.set(null);
        this.applyActivity.clear('sounds', id);
      });
  }

  reset(item: CatalogItem): void {
    void this.resetApplied(item);
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

  restoreTrack(): void {
    const target = this.selectedTrack();
    if (!target || this.isBusy()) {
      return;
    }

    if (!window.confirm('Restore this theme to vanilla Brawlhalla audio?')) {
      return;
    }

    this.replacing.set(true);
    this.customNote.set('Restoring vanilla…');
    this.ipc
      .request(IPC_MESSAGE.MUSIC_RESTORE, { target })
      .then((reply) => {
        if (!reply.ok) {
          this.customNote.set(reply.error ?? 'Restore failed.');
          return;
        }
        const result = reply.payload as ApplyAttempt | undefined;
        this.customNote.set(result?.reason ?? 'Restored vanilla.');
      })
      .catch((error: unknown) => {
        this.customNote.set(error instanceof Error ? error.message : 'Restore failed.');
      })
      .finally(() => {
        this.replacing.set(false);
        this.applyActivity.clear('sounds', CUSTOM_AUDIO_PROGRESS_ID);
        this.loadTracks();
      });
  }

  restoreAllAudio(): void {
    if (this.isBusy()) {
      return;
    }

    if (
      !window.confirm(
        'Restore ALL game audio to vanilla? Custom themes and applied sound packs will be removed from the game. Maps and skins stay.',
      )
    ) {
      return;
    }

    this.replacing.set(true);
    this.customNote.set('Restoring all vanilla audio…');
    this.ipc
      .request(IPC_MESSAGE.MUSIC_RESTORE_ALL)
      .then((reply) => {
        if (!reply.ok) {
          this.customNote.set(reply.error ?? 'Restore failed.');
          return;
        }
        const result = reply.payload as ApplyAttempt | undefined;
        this.customNote.set(result?.reason ?? 'Restored all vanilla audio.');
        return this.loadout.refresh();
      })
      .catch((error: unknown) => {
        this.customNote.set(error instanceof Error ? error.message : 'Restore failed.');
      })
      .finally(() => {
        this.replacing.set(false);
        this.applyActivity.clear('sounds', CUSTOM_AUDIO_PROGRESS_ID);
        this.loadTracks();
      });
  }

  restoreListed(fileName: string): void {
    this.selectedTrack.set(fileName);
    this.restoreTrack();
  }

  changedDetail(track: MusicTrack): string {
    const when = this.formatChangedAt(track.changedAt);
    if (track.source && when) {
      return 'from ' + track.source + ' · ' + when;
    }
    if (track.source) {
      return 'from ' + track.source;
    }
    if (when) {
      return when;
    }
    return 'differs from backup';
  }

  private formatChangedAt(iso: string | null | undefined): string {
    if (!iso) {
      return '';
    }
    const at = Date.parse(iso);
    if (Number.isNaN(at)) {
      return '';
    }
    return new Date(at).toLocaleString();
  }

  private runReplace(url: string | undefined): void {
    const target = this.selectedTrack();
    if (!target || this.isBusy()) {
      return;
    }

    this.replacing.set(true);
    this.customNote.set(url ? 'Downloading the MP3…' : 'Choose a file…');
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
        this.applyActivity.clear('sounds', CUSTOM_AUDIO_PROGRESS_ID);
        this.loadTracks();
      });
  }

  /** Frees disk space. If the pack is applied, a second confirm can also Reset it in the game. */
  deleteDownload(id: number): void {
    if (this.isBusy()) {
      return;
    }
    const item = this.shownItems().find((row) => row.id === id);
    const choice = confirmDeleteDownload(
      item?.name ?? 'this sound',
      this.sizeBytes()[id],
      this.isActive(id),
    );
    if (!choice.proceed) {
      return;
    }
    if (choice.alsoReset && item && this.isActive(id)) {
      void this.resetApplied(item).then((ok) => {
        if (ok) {
          this.removeDownloadFolder(id, true);
        }
      });
      return;
    }
    this.removeDownloadFolder(id, false);
  }

  private resetApplied(item: CatalogItem): Promise<boolean> {
    if (this.isBusy() || !this.isActive(item.id)) {
      return Promise.resolve(false);
    }
    this.resettingId.set(item.id);
    return this.ipc
      .request(IPC_MESSAGE.MUSIC_RESET, { id: item.id })
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
          this.loadTracks();
          return true;
        });
      })
      .catch((error: unknown) => {
        this.setNote(item.id, error instanceof Error ? error.message : 'Reset failed.');
        return false;
      })
      .finally(() => {
        this.resettingId.set(null);
        this.applyActivity.clear('sounds', item.id);
      });
  }

  private removeDownloadFolder(id: number, alsoReset: boolean): void {
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
        this.setNote(
          id,
          alsoReset
            ? 'Reset in the game and removed from disk.'
            : this.isActive(id)
              ? 'Removed from disk. Still applied in the game.'
              : 'Removed from disk.',
        );
        if (this.isLibrary()) {
          this.loadLibrary();
        }
        this.downloadActivity.notifyInventoryChanged();
      })
      .catch((error: unknown) => {
        this.setNote(id, error instanceof Error ? error.message : 'Delete failed.');
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
      'sounds',
      [...this.downloadedIds()].filter((id) => this.isNew(id)),
    );
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
      const times: Record<number, string> = {};
      for (const item of items) {
        folders[item.id] = item.folder;
        if (item.sizeBytes !== undefined) {
          sizes[item.id] = item.sizeBytes;
        }
        if (item.downloadedUtc) {
          times[item.id] = item.downloadedUtc;
        }
      }
      this.folders.set(folders);
      this.sizeBytes.set(sizes);
      this.downloadedAt.set(times);
      if (this.isLibrary()) {
        this.loadLibrary();
      }
    });
  }

  private loadTracks(): void {
    this.ipc.request(IPC_MESSAGE.MUSIC_TRACKS).then((reply) => {
      if (!reply.ok) {
        this.tracks.set([]);
        this.otherChanges.set([]);
        this.otherChangeCount.set(0);
        this.customNote.set(reply.error ?? 'Could not list game audio tracks.');
        return;
      }

      const data = reply.payload as MusicTracks | undefined;
      const list = data?.tracks ?? [];
      this.tracks.set(list);
      this.otherChanges.set(data?.otherChanges ?? []);
      this.otherChangeCount.set(data?.otherCount ?? 0);
      if (list.length > 0) {
        const names = list.map((track) => track.fileName);
        if (!this.selectedTrack() || !names.includes(this.selectedTrack())) {
          const mainMenu = list.find((track) => track.label.startsWith('Menu (main)'));
          const menu = mainMenu ?? list.find((track) => track.slot === 'menu');
          this.selectedTrack.set((menu ?? list[0]).fileName);
        }
      }
    });
  }

  private loadLibrary(): void {
    const ids = libraryIds(
      this.libraryFilter(),
      this.downloadedIds(),
      this.loadout.activeMusicIds(),
      this.likes.soundIds(),
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
        this.rememberCategories(list);
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
      .request(IPC_MESSAGE.CATALOG_SOUNDS, {
        page,
        query: this.query(),
        sort: this.sort(),
        categoryId: this.categoryId(),
        authorId: this.authorFilter()?.id ?? 0,
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

        this.rememberCategories(data.items);
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
        this.error.set(error instanceof Error ? error.message : 'Could not load sounds.');
      })
      .finally(() => {
        if (token === this.loadToken) {
          this.loading.set(false);
        }
      });
  }

  private rememberCategories(list: CatalogItem[]): void {
    this.categoryById.update((map) => {
      const next = { ...map };
      for (const item of list) {
        if (item.category && item.category !== 'Sounds') {
          next[item.id] = item.category;
        }
      }
      return next;
    });
  }

  private withKnownCategory(item: CatalogItem): CatalogItem {
    const known = this.categoryById()[item.id];
    if (!known || item.category === known) {
      return item;
    }
    if (item.category && item.category !== 'Sounds') {
      return item;
    }
    return { ...item, category: known };
  }

  private computeTotalPages(data: CatalogPage): number {
    if (data.totalCount > 0 && data.pageSize > 0) {
      return Math.max(1, Math.ceil(data.totalCount / data.pageSize));
    }
    return data.complete ? data.page : data.page + 1;
  }
}
