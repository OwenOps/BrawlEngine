import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import {
  PlayerProfile,
  PlayerRanked,
  PlayerSearchMatch,
  PlayerSearchPage,
  StatsRecent,
  StatsSaved,
} from '../../core/ipc/contracts/stats.contracts';
import { IPC_MESSAGE } from '../../core/ipc/ipc.constants';
import { IpcService } from '../../core/ipc/ipc.service';
import { regionName, tierKey, tierName } from './ranked-tiers';

@Component({
  selector: 'app-stats-page',
  standalone: true,
  templateUrl: './stats-page.component.html',
  styleUrl: './stats-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatsPageComponent implements OnInit, OnDestroy {
  private readonly ipc = inject(IpcService);
  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  private copiedTimer: ReturnType<typeof setTimeout> | undefined;
  private loadToken = 0;
  private lastQuery = '';
  private opened: PlayerSearchMatch | null = null;

  readonly queryInput = signal('');
  readonly matches = signal<PlayerSearchMatch[]>([]);
  readonly recents = signal<StatsRecent[]>([]);
  readonly pins = signal<StatsRecent[]>([]);
  readonly mine = signal<StatsRecent | null>(null);
  readonly profile = signal<PlayerProfile | null>(null);
  readonly loading = signal(false);
  readonly searched = signal(false);
  readonly error = signal<string | null>(null);
  readonly warning = signal<string | null>(null);
  readonly logPath = signal<string | null>(null);
  readonly copied = signal(false);
  readonly gameMode = signal('1v1');
  readonly regionFilter = signal('ALL');
  readonly rankFilter = signal('all');
  readonly sort = signal<'rating' | 'rank' | 'name' | 'region'>('rating');
  readonly sortAsc = signal(false);
  readonly page = signal(1);
  readonly totalPages = signal(1);
  readonly feed = signal<'saved' | 'board'>('board');
  readonly modes = ['1v1', '2v2', '3v3'] as const;
  readonly regions = ['ALL', 'EU', 'US-E', 'US-W', 'BRZ', 'SEA', 'AUS', 'JPS', 'SA', 'ME'] as const;
  readonly ranks = ['all', 'diamond', 'platinum', 'gold', 'silver', 'bronze', 'tin'] as const;
  readonly sorts = [
    { id: 'rating' as const, label: 'Rating' },
    { id: 'rank' as const, label: 'Rank' },
    { id: 'region' as const, label: 'Region' },
    { id: 'name' as const, label: 'Name' },
  ];
  readonly shown = computed(() => {
    const query = this.queryInput().trim();
    if (query.length >= 2) {
      return this.matches();
    }
    return this.sortRows(this.matches(), this.sort(), this.sortAsc());
  });
  readonly isBoard = computed(() => this.queryInput().trim().length === 0);
  readonly isSaved = computed(() => this.isBoard() && this.feed() === 'saved');
  readonly wait = 'Looking up players…';
  readonly profileWait = 'Loading player…';

  ngOnInit(): void {
    this.ipc
      .request(IPC_MESSAGE.STATS_RECENTS)
      .then((reply) => {
        if (!reply.ok) {
          this.search();
          return;
        }
        const saved = (reply.payload as StatsSaved | undefined) ?? { recents: [], pins: [], mine: null };
        this.recents.set(saved.recents ?? []);
        this.pins.set(saved.pins ?? []);
        this.mine.set(saved.mine ?? null);
        if ((saved.pins?.length ?? 0) > 0 || saved.mine) {
          this.feed.set('saved');
          return;
        }
        this.search();
      })
      .catch(() => this.search());
  }

  ngOnDestroy(): void {
    if (this.searchTimer !== undefined) {
      clearTimeout(this.searchTimer);
    }
    if (this.copiedTimer !== undefined) {
      clearTimeout(this.copiedTimer);
    }
    this.loadToken++;
  }

  onQueryInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.queryInput.set(input.value);
    this.page.set(1);
    if (this.searchTimer !== undefined) {
      clearTimeout(this.searchTimer);
    }
    this.searchTimer = setTimeout(() => this.search(), 250);
  }

  onSearchKey(event: KeyboardEvent): void {
    if (event.key !== 'Enter') {
      return;
    }
    event.preventDefault();
    if (this.searchTimer !== undefined) {
      clearTimeout(this.searchTimer);
    }
    this.search();
  }

  retry(): void {
    if (this.opened) {
      this.openProfile(this.opened, true);
      return;
    }
    this.search(true);
  }

  get hasOpened(): boolean {
    return this.opened !== null;
  }

  onGameMode(event: Event): void {
    const mode = (event.target as HTMLSelectElement).value;
    if (this.gameMode() === mode) {
      return;
    }
    this.gameMode.set(mode);
    this.page.set(1);
    this.search(true);
  }

  onRegion(event: Event): void {
    const region = (event.target as HTMLSelectElement).value;
    if (this.regionFilter() === region) {
      return;
    }
    this.regionFilter.set(region);
    this.page.set(1);
    this.search(true);
  }

  onSort(event: Event): void {
    this.sort.set((event.target as HTMLSelectElement).value as 'rating' | 'rank' | 'name' | 'region');
  }

  /** Lowest also flips the ladder pages, so page 1 is the bottom of the picked rank. */
  setSortDir(ascending: boolean): void {
    if (this.sortAsc() === ascending) {
      return;
    }
    this.sortAsc.set(ascending);
    this.page.set(1);
    this.search(true);
  }

  onRank(event: Event): void {
    const rank = (event.target as HTMLSelectElement).value;
    if (this.rankFilter() === rank) {
      return;
    }
    this.rankFilter.set(rank);
    this.page.set(1);
    this.search(true);
  }

  setFeed(feed: 'saved' | 'board'): void {
    this.feed.set(feed);
    this.opened = null;
    this.profile.set(null);
    this.error.set(null);
    this.warning.set(null);
    this.logPath.set(null);
    if (feed === 'board' && this.queryInput().trim().length === 0) {
      this.search(true);
    }
  }

  rankLabel(rank: string): string {
    if (rank === 'all') {
      return 'All';
    }
    return rank.charAt(0).toUpperCase() + rank.slice(1);
  }

  isPinned(id: number): boolean {
    return this.pins().some((row) => row.id === id);
  }

  isMine(id: number): boolean {
    return this.mine()?.id === id;
  }

  togglePin(event: Event, id: number, name: string): void {
    event.stopPropagation();
    event.preventDefault();
    this.ipc
      .request(IPC_MESSAGE.STATS_PIN, { id, name })
      .then((reply) => {
        if (reply.ok) {
          this.pins.set((reply.payload as StatsRecent[] | undefined) ?? []);
        }
      })
      .catch(() => undefined);
  }

  setAsMine(id: number, name: string): void {
    const next = this.isMine(id) ? { id: 0, name: '' } : { id, name };
    this.ipc
      .request(IPC_MESSAGE.STATS_MINE, next)
      .then((reply) => {
        if (reply.ok) {
          this.mine.set((reply.payload as StatsRecent | null | undefined) ?? null);
        }
      })
      .catch(() => undefined);
  }

  toggleMine(event: Event, id: number, name: string): void {
    event.stopPropagation();
    event.preventDefault();
    this.setAsMine(id, name);
  }

  playTime(seconds: number | null | undefined): string | null {
    if (seconds == null || seconds <= 0) {
      return null;
    }
    const hours = Math.floor(seconds / 3600);
    if (hours <= 0) {
      return Math.max(1, Math.round(seconds / 60)) + ' min';
    }
    return hours + 'h';
  }

  withNames(teammates: { name: string }[] | null | undefined): string | null {
    const names = (teammates ?? []).map((row) => row.name).filter((name) => name.length > 0);
    return names.length === 0 ? null : 'with ' + names.join(', ');
  }

  goToPage(page: number): void {
    const next = Math.min(this.totalPages(), Math.max(1, page));
    if (next === this.page()) {
      return;
    }
    this.page.set(next);
    this.search(true);
  }

  jumpToPage(event: Event): void {
    const input = event.target as HTMLInputElement;
    const parsed = Number.parseInt(input.value, 10);
    this.goToPage(Number.isFinite(parsed) ? parsed : this.page());
  }

  regionLabel(region: string): string {
    return region === 'ALL' ? 'All' : region;
  }

  openRecent(row: StatsRecent): void {
    this.openProfile(this.asMatch(row));
  }

  openProfile(match: PlayerSearchMatch, force = false): void {
    if (!force && this.profile()?.id === match.id && !this.error()) {
      return;
    }

    const token = ++this.loadToken;
    this.opened = match;
    this.profile.set(null);
    this.copied.set(false);
    this.loading.set(true);
    this.error.set(null);
    this.warning.set(null);
    this.logPath.set(null);
    this.ipc
      .request(IPC_MESSAGE.STATS_PLAYER, { id: match.id, name: match.name })
      .then((reply) => {
        if (token !== this.loadToken) {
          return;
        }
        if (!reply.ok) {
          this.error.set(reply.error ?? 'Could not reach Brawlhalla. Retry.');
          return;
        }
        const card = (reply.payload as PlayerProfile | undefined) ?? null;
        this.profile.set(card);
        if (card) {
          this.rememberLocal(card.id, card.name);
        }
      })
      .catch((error: unknown) => {
        if (token !== this.loadToken) {
          return;
        }
        this.error.set(error instanceof Error ? error.message : 'Could not reach Brawlhalla. Retry.');
      })
      .finally(() => {
        if (token === this.loadToken) {
          this.loading.set(false);
        }
      });
  }

  backToResults(): void {
    this.loadToken++;
    this.opened = null;
    this.profile.set(null);
    this.copied.set(false);
    this.loading.set(false);
    this.error.set(null);
    this.warning.set(null);
    this.logPath.set(null);
  }

  copyId(id: number): void {
    void navigator.clipboard.writeText(String(id)).then(() => {
      this.copied.set(true);
      if (this.copiedTimer !== undefined) {
        clearTimeout(this.copiedTimer);
      }
      this.copiedTimer = setTimeout(() => this.copied.set(false), 1500);
    });
  }

  winrate(wins: number | null | undefined, gamesOrLosses: number | null | undefined, losses = false): string {
    const w = wins ?? 0;
    const other = gamesOrLosses ?? 0;
    const total = losses ? w + other : other;
    if (total <= 0) {
      return '—';
    }
    return Math.round((w / total) * 100) + '%';
  }

  ranked(mode: string): PlayerRanked {
    return (
      this.profile()?.ranked.find((row) => row.gameMode === mode) ?? {
        gameMode: mode,
        region: null,
        tier: null,
        rating: null,
        peakRating: null,
        rank: null,
        wins: null,
        losses: null,
        teammates: null,
      }
    );
  }

  tier(tier: string | null, rating: number | null): string | null {
    return tierName(tier, rating);
  }

  tierKey(tier: string | null, rating: number | null): string | null {
    return tierKey(tier, rating);
  }

  cardClass(tier: string | null, rating: number | null, base: string): string {
    const key = this.tierKey(tier, rating);
    return key ? base + ' t-' + key : base;
  }

  region(region: string | null): string {
    return regionName(region);
  }

  private search(force = false): void {
    const query = this.queryInput().trim();
    if (query.length === 0 && this.feed() === 'saved') {
      this.loadToken++;
      this.lastQuery = '';
      this.opened = null;
      this.profile.set(null);
      this.copied.set(false);
      this.loading.set(false);
      this.error.set(null);
      this.warning.set(null);
      this.logPath.set(null);
      return;
    }

    if (!this.isDirectLookup(query) && query.length === 1) {
      return;
    }

    const key = [
      query,
      this.gameMode(),
      this.regionFilter(),
      query.length >= 2 ? 'all' : this.rankFilter(),
      this.sortAsc(),
      this.page(),
    ].join('|');
    if (!force && key === this.lastQuery && !this.error() && !this.opened) {
      return;
    }

    const token = ++this.loadToken;
    this.lastQuery = key;
    this.opened = null;
    this.profile.set(null);
    this.copied.set(false);
    this.loading.set(true);
    this.error.set(null);
    this.warning.set(null);
    this.logPath.set(null);
    this.ipc
      .request(IPC_MESSAGE.STATS_SEARCH, {
        query,
        gameMode: this.gameMode(),
        region: this.regionFilter(),
        tier: this.rankFilter(),
        ascending: this.sortAsc(),
        page: this.page(),
      })
      .then((reply) => {
        if (token !== this.loadToken) {
          return;
        }
        if (!reply.ok) {
          this.error.set(reply.error ?? 'Could not reach Brawlhalla. Retry.');
          this.matches.set([]);
          this.searched.set(true);
          return;
        }
        const page = reply.payload as PlayerSearchPage | undefined;
        const list = page?.matches ?? [];
        this.matches.set(list);
        this.totalPages.set(Math.max(1, page?.totalPages ?? 1));
        this.warning.set(page?.warning?.trim() ? page.warning : null);
        this.logPath.set(page?.logPath?.trim() ? page.logPath : null);
        this.searched.set(true);
      })
      .catch((error: unknown) => {
        if (token !== this.loadToken) {
          return;
        }
        this.error.set(error instanceof Error ? error.message : 'Could not reach Brawlhalla. Retry.');
        this.matches.set([]);
        this.searched.set(true);
      })
      .finally(() => {
        if (token === this.loadToken && !this.opened) {
          this.loading.set(false);
        }
      });
  }

  private sortRows(
    rows: PlayerSearchMatch[],
    sort: 'rating' | 'rank' | 'name' | 'region',
    ascending: boolean,
  ): PlayerSearchMatch[] {
    const copy = rows.slice();
    copy.sort((a, b) => {
      if (sort === 'name') {
        return a.name.localeCompare(b.name);
      }
      if (sort === 'region') {
        return (a.region ?? '').localeCompare(b.region ?? '') || (b.rating ?? -1) - (a.rating ?? -1);
      }
      if (sort === 'rank') {
        return (a.rank ?? 999999) - (b.rank ?? 999999);
      }
      return (b.rating ?? -1) - (a.rating ?? -1);
    });
    return ascending ? copy.reverse() : copy;
  }

  private rememberLocal(id: number, name: string): void {
    this.recents.update((rows) => [{ id, name }, ...rows.filter((row) => row.id !== id)].slice(0, 8));
  }

  private asMatch(row: StatsRecent): PlayerSearchMatch {
    return {
      id: row.id,
      name: row.name,
      gameMode: '',
      region: null,
      tier: null,
      rating: null,
      peakRating: null,
      rank: null,
      wins: null,
      losses: null,
      teammates: null,
    };
  }

  private isDirectLookup(query: string): boolean {
    return this.looksLikeBrawlhallaId(query) || this.looksLikeSteamId(query);
  }

  private looksLikeBrawlhallaId(query: string): boolean {
    return this.parseBrawlhallaId(query) > 0;
  }

  private parseBrawlhallaId(query: string): number {
    const hash = query.lastIndexOf('#');
    if (hash >= 0) {
      return this.parseIdDigits(query.slice(hash + 1).trim());
    }
    const player = query.toLowerCase().lastIndexOf('/player/');
    if (player >= 0) {
      return this.parseIdDigits(query.slice(player + 8));
    }
    return this.parseIdDigits(query);
  }

  private parseIdDigits(token: string): number {
    let digits = '';
    for (const ch of token.trim()) {
      if (ch < '0' || ch > '9') {
        break;
      }
      digits += ch;
    }
    if (digits.length === 0 || digits.length > 10) {
      return 0;
    }
    const id = Number.parseInt(digits, 10);
    return id > 0 ? id : 0;
  }

  private looksLikeSteamId(query: string): boolean {
    if (query.length < 15 || query.length > 20 || !query.startsWith('765')) {
      return false;
    }
    for (const ch of query) {
      if (ch < '0' || ch > '9') {
        return false;
      }
    }
    return true;
  }
}
