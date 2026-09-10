import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  OnDestroy,
  computed,
  inject,
  signal,
} from '@angular/core';
import { scrollMainToTop } from '../../core/ui/scroll-main';
import { BrowserService } from '../../core/browser/browser.service';
import { formatBytes, confirmDeleteDownload } from '../../core/download/format-bytes';
import { DownloadActivityService } from '../../core/download/download-activity.service';
import {
  CATALOG_SORTS,
  CatalogItem,
  CatalogPage,
  CatalogSort,
} from '../../core/ipc/contracts/catalog.contracts';
import { DownloadResult, LocalDownloadList } from '../../core/ipc/contracts/download.contracts';
import { IPC_MESSAGE } from '../../core/ipc/ipc.constants';
import { IpcService } from '../../core/ipc/ipc.service';

@Component({
  selector: 'app-skins-page',
  standalone: true,
  templateUrl: './skins-page.component.html',
  styleUrl: './skins-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SkinsPageComponent implements OnDestroy {
  private readonly ipc = inject(IpcService);
  private readonly browser = inject(BrowserService);
  readonly downloadActivity = inject(DownloadActivityService);
  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  private loadToken = 0;

  readonly sorts = CATALOG_SORTS;
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
  readonly deletingId = signal<number | null>(null);
  readonly cardNote = signal<Record<number, string>>({});
  readonly folders = signal<Record<number, string>>({});
  private readonly sizeBytes = signal<Record<number, number>>({});
  readonly infoItem = signal<CatalogItem | null>(null);
  readonly isBusy = computed(() => this.downloadingId() !== null || this.deletingId() !== null);

  constructor() {
    this.loadLocal();
    this.load(1);
  }

  ngOnDestroy(): void {
    if (this.searchTimer !== undefined) {
      clearTimeout(this.searchTimer);
    }
  }

  isDownloaded(id: number): boolean {
    return this.folders()[id] !== undefined;
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

  initial(name: string): string {
    return name.trim().charAt(0).toUpperCase() || '?';
  }

  onQueryInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.queryInput.set(input.value);
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
    this.browser.openDownloads('skins');
  }

  download(item: CatalogItem): void {
    if (this.isBusy()) {
      return;
    }
    this.downloadingId.set(item.id);
    this.ipc
      .request(IPC_MESSAGE.SKIN_DOWNLOAD, { id: item.id })
      .then((reply) => {
        if (!reply.ok) {
          this.setNote(item.id, reply.error ?? 'Download failed.');
          return;
        }
        const result = reply.payload as DownloadResult | undefined;
        if (result?.folder) {
          this.folders.update((map) => ({ ...map, [item.id]: result.folder }));
        }
        this.setNote(item.id, 'Downloaded. Open the folder and copy files into Brawlhalla yourself.');
        this.loadLocal();
      })
      .catch((error: unknown) => {
        this.setNote(item.id, error instanceof Error ? error.message : 'Download failed.');
      })
      .finally(() => {
        this.downloadingId.set(null);
        this.downloadActivity.clear('skins', item.id);
      });
  }

  openFolder(item: CatalogItem): void {
    const path = this.folders()[item.id];
    if (!path) {
      return;
    }
    this.browser.openFolder(path);
  }

  deleteDownload(item: CatalogItem): void {
    if (this.isBusy()) {
      return;
    }
    if (!confirmDeleteDownload(item.name, this.sizeBytes()[item.id])) {
      return;
    }
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
        this.setNote(item.id, 'Removed from disk.');
      })
      .catch((error: unknown) => {
        this.setNote(item.id, error instanceof Error ? error.message : 'Delete failed.');
      })
      .finally(() => {
        this.deletingId.set(null);
      });
  }

  private setNote(id: number, message: string): void {
    this.cardNote.update((notes) => ({ ...notes, [id]: message }));
  }

  private loadLocal(): void {
    this.ipc.request(IPC_MESSAGE.DOWNLOADS_LIST, { kind: 'skins' }).then((reply) => {
      if (!reply.ok) {
        return;
      }
      const data = reply.payload as LocalDownloadList | undefined;
      const next: Record<number, string> = {};
      const sizes: Record<number, number> = {};
      for (const item of data?.items ?? []) {
        next[item.id] = item.folder;
        if (item.sizeBytes !== undefined) {
          sizes[item.id] = item.sizeBytes;
        }
      }
      this.folders.set(next);
      this.sizeBytes.set(sizes);
    });
  }

  private load(page: number): void {
    const token = ++this.loadToken;
    this.loading.set(true);
    this.error.set(null);

    this.ipc
      .request(IPC_MESSAGE.CATALOG_SKINS, {
        page,
        query: this.query(),
        sort: this.sort(),
      })
      .then((reply) => {
        if (token !== this.loadToken) {
          return;
        }
        if (!reply.ok) {
          this.error.set(reply.error ?? 'Could not load skins.');
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
        this.error.set(error instanceof Error ? error.message : 'Could not load skins.');
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
