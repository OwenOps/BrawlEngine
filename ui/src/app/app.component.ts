import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  OnDestroy,
  computed,
  inject,
  signal,
} from '@angular/core';
import { AppInfo, AppUpdate } from './core/ipc/contracts/app-info.contracts';
import { ApplyGuard, GameLocation } from './core/ipc/contracts/game.contracts';
import { IPC_MESSAGE } from './core/ipc/ipc.constants';
import { IpcService } from './core/ipc/ipc.service';
import { LoadoutService } from './core/loadout/loadout.service';
import { APP_TABS, AppTabId } from './core/navigation/app-tabs.config';
import {
  APP_SHELL_TEXT,
  CATALOG_HINT,
  CATALOG_HINT_STORAGE_KEY,
  CONTACT_DISCORD,
  GAMEBANANA_GAME_URL,
  RESET_WAIT,
  SOURCE_REPO_URL,
  UPDATE_DISMISS_STORAGE_KEY,
} from './core/ui/app-shell.constants';
import { BrowserService } from './core/browser/browser.service';
import { GameLocationState } from './core/game/game-location.state';
import { ApplyActivityService } from './core/apply/apply-activity.service';
import { DownloadActivityService } from './core/download/download-activity.service';
import { THEME_PRESETS, ThemeName, ThemeService } from './core/theme/theme.service';
import { MapsPageComponent } from './features/maps/maps-page.component';
import { MusicsPageComponent } from './features/musics/musics-page.component';
import { SkinsPageComponent } from './features/skins/skins-page.component';

@Component({
  selector: 'app-root',
  imports: [MapsPageComponent, MusicsPageComponent, SkinsPageComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppComponent implements OnDestroy {
  private readonly ipc = inject(IpcService);
  private readonly loadout = inject(LoadoutService);
  private readonly browser = inject(BrowserService);
  private readonly gameLocation = inject(GameLocationState);
  readonly downloadActivity = inject(DownloadActivityService);
  readonly applyActivity = inject(ApplyActivityService);
  readonly theme = inject(ThemeService);
  readonly themeNames = Object.keys(THEME_PRESETS) as ThemeName[];
  private runningTimer: ReturnType<typeof setInterval> | undefined;
  private discordTimer: ReturnType<typeof setTimeout> | undefined;

  readonly tabs = APP_TABS;
  readonly activeTabId = signal<AppTabId>(APP_TABS[0].id);
  readonly visitedTabIds = signal<ReadonlySet<AppTabId>>(new Set([APP_TABS[0].id]));
  readonly hostStatus = signal<string>(APP_SHELL_TEXT.hostChecking);
  readonly hostConnected = signal<boolean | null>(null);
  readonly gamePath = signal<string>(APP_SHELL_TEXT.gameLooking);
  readonly gameFound = signal(false);
  readonly mp3Path = signal<string>('Looking for mp3…');
  readonly hasMp3 = signal(false);
  readonly runningMessage = this.gameLocation.runningMessage;
  readonly picking = signal(false);
  readonly actionBusy = signal(false);
  readonly actionMessage = signal<string | null>(null);
  readonly madeBy = signal<string | null>(null);
  readonly appVersion = signal<string | null>(null);
  readonly update = signal<AppUpdate | null>(null);
  readonly discordCopied = signal(false);
  readonly discordUser = CONTACT_DISCORD;
  readonly resetDialogOpen = signal(false);
  readonly resetDeleteDownloads = signal(false);
  readonly resetRunning = signal(false);
  readonly resetWait = RESET_WAIT;
  readonly catalogHint = CATALOG_HINT;
  readonly catalogHintOpen = signal(localStorage.getItem(CATALOG_HINT_STORAGE_KEY) !== '1');
  readonly configName = signal('');
  readonly configs = this.loadout.configs;
  readonly shellBusy = computed(
    () => this.picking() || this.actionBusy() || this.loadout.busy() || this.resetRunning(),
  );
  readonly hasSidebarActivity = computed(
    () => this.downloadActivity.active().length > 0 || this.applyActivity.active().length > 0,
  );
  readonly hasLoadout = computed(() => {
    const current = this.loadout.loadout();
    return current.maps.length > 0 || current.music.length > 0 || current.skins.length > 0;
  });

  constructor() {
    this.ipc.request(IPC_MESSAGE.PING).then((reply) => {
      this.hostConnected.set(reply.ok === true);
      this.hostStatus.set(
        reply.ok ? 'Host connected' : (reply.error ?? 'Host unavailable'),
      );
      const info = reply.payload as AppInfo | undefined;
      this.madeBy.set(info?.madeBy?.trim() || null);
      this.appVersion.set(info?.version?.trim() || null);
    });
    this.refreshUpdate();
    this.refreshGame();
    this.refreshRunning();
    this.runningTimer = setInterval(() => this.refreshRunning(), 2000);
  }

  ngOnDestroy(): void {
    if (this.runningTimer !== undefined) {
      clearInterval(this.runningTimer);
    }
    if (this.discordTimer !== undefined) {
      clearTimeout(this.discordTimer);
    }
  }

  @HostListener('document:visibilitychange')
  onVisibility(): void {
    if (!document.hidden) {
      this.refreshRunning();
    }
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.resetDialogOpen() && !this.resetRunning()) {
      this.closeResetDialog();
    } else if (this.catalogHintOpen()) {
      this.dismissCatalogHint();
    }
  }

  dismissCatalogHint(): void {
    localStorage.setItem(CATALOG_HINT_STORAGE_KEY, '1');
    this.catalogHintOpen.set(false);
  }

  selectTheme(name: ThemeName): void {
    this.theme.setTheme(name);
  }

  tabAccentVar(id: AppTabId): string {
    const varName = id === 'musics' ? 'accent-music' : `accent-${id}`;
    return `var(--${varName})`;
  }

  selectTab(id: AppTabId): void {
    this.activeTabId.set(id);
    if (!this.visitedTabIds().has(id)) {
      this.visitedTabIds.update((ids) => new Set(ids).add(id));
    }
  }

  chooseFolder(): void {
    this.picking.set(true);
    this.ipc.request(IPC_MESSAGE.GAME_PICK).then((reply) => {
      this.picking.set(false);
      this.applyGame(reply.payload as GameLocation | undefined, reply.error, 'game');
    });
  }

  chooseMp3Folder(): void {
    this.picking.set(true);
    this.ipc.request(IPC_MESSAGE.MUSIC_PICK).then((reply) => {
      this.picking.set(false);
      this.applyGame(reply.payload as GameLocation | undefined, reply.error, 'music');
    });
  }

  resetAll(): void {
    if (this.shellBusy()) {
      return;
    }
    this.resetDeleteDownloads.set(false);
    this.resetDialogOpen.set(true);
  }

  closeResetDialog(): void {
    if (this.resetRunning()) {
      return;
    }
    this.resetDialogOpen.set(false);
  }

  confirmResetAll(): void {
    if (this.shellBusy() || this.resetRunning()) {
      return;
    }

    const deleteDownloads = this.resetDeleteDownloads();
    this.resetRunning.set(true);
    this.actionMessage.set(null);
    this.loadout
      .resetAll(deleteDownloads)
      .then((result) => {
        if (deleteDownloads && result.ok) {
          this.downloadActivity.notifyInventoryChanged();
        }
        this.actionMessage.set(result.message);
      })
      .catch((error: unknown) => {
        this.actionMessage.set(error instanceof Error ? error.message : 'Request failed.');
      })
      .finally(() => {
        this.resetRunning.set(false);
        this.resetDialogOpen.set(false);
      });
  }

  setResetDeleteDownloads(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.resetDeleteDownloads.set(input.checked);
  }

  rankedSafe(): void {
    this.runLoadoutAction(() => this.loadout.rankedSafe());
  }

  reapply(): void {
    this.runLoadoutAction(() => this.loadout.reapply());
  }

  setConfigName(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.configName.set(input.value);
  }

  saveConfig(): void {
    const name = this.configName().trim();
    if (!name) {
      this.actionMessage.set('Enter a name to save the current loadout.');
      return;
    }

    this.runLoadoutAction(() => this.loadout.saveCurrent(name));
  }

  loadConfig(id: string): void {
    this.runLoadoutAction(() => this.loadout.loadConfig(id));
  }

  deleteConfig(id: string): void {
    this.runLoadoutAction(() => this.loadout.deleteConfig(id));
  }

  openGameBanana(): void {
    this.browser.open(GAMEBANANA_GAME_URL);
  }

  openSource(): void {
    this.browser.open(SOURCE_REPO_URL);
  }

  openUpdate(): void {
    const url = this.update()?.url?.trim();
    if (url) {
      this.browser.open(url);
    }
  }

  dismissUpdate(): void {
    const latest = this.update()?.latest?.trim();
    if (latest) {
      localStorage.setItem(UPDATE_DISMISS_STORAGE_KEY, latest);
    }
    this.update.set(null);
  }

  copyDiscord(): void {
    void navigator.clipboard.writeText(CONTACT_DISCORD).then(() => {
      this.discordCopied.set(true);
      if (this.discordTimer !== undefined) {
        clearTimeout(this.discordTimer);
      }
      this.discordTimer = setTimeout(() => this.discordCopied.set(false), 1500);
    });
  }

  openDownloads(): void {
    this.browser.openDownloads('mods');
  }

  chooseDownloadsFolder(): void {
    this.picking.set(true);
    this.browser.pickDownloadsFolder().finally(() => {
      this.picking.set(false);
      this.downloadActivity.notifyInventoryChanged();
    });
  }

  resetDownloadsFolder(): void {
    if (this.shellBusy() || this.downloadActivity.usingDefaultFolder()) {
      return;
    }
    this.picking.set(true);
    this.browser.resetDownloadsFolder().finally(() => {
      this.picking.set(false);
      this.downloadActivity.notifyInventoryChanged();
    });
  }

  private runLoadoutAction(action: () => Promise<{ ok: boolean; message: string }>): void {
    if (this.shellBusy()) {
      return;
    }

    this.actionBusy.set(true);
    this.actionMessage.set(null);
    action()
      .then((result) => {
        this.actionMessage.set(result.message);
      })
      .catch((error: unknown) => {
        this.actionMessage.set(error instanceof Error ? error.message : 'Request failed.');
      })
      .finally(() => {
        this.actionBusy.set(false);
      });
  }

  private refreshUpdate(): void {
    this.ipc.request(IPC_MESSAGE.APP_UPDATE).then((reply) => {
      if (!reply.ok) {
        return;
      }
      const data = reply.payload as AppUpdate | undefined;
      const latest = data?.latest?.trim();
      if (!data?.available || !latest) {
        return;
      }
      if (localStorage.getItem(UPDATE_DISMISS_STORAGE_KEY) === latest) {
        return;
      }
      this.update.set({ available: true, latest, url: data.url ?? null });
    });
  }

  private refreshGame(): void {
    this.ipc.request(IPC_MESSAGE.GAME_GET).then((reply) => {
      this.applyGame(reply.payload as GameLocation | undefined, reply.error, 'game');
    });
  }

  private refreshRunning(): void {
    if (document.hidden) {
      return;
    }
    this.ipc.request(IPC_MESSAGE.GAME_RUNNING).then((reply) => {
      const status = reply.payload as ApplyGuard | undefined;
      this.gameLocation.runningMessage.set(
        status?.running ? (status.message ?? 'Brawlhalla is open. Apply waits until it closes.') : null,
      );
    });
  }

  private applyGame(
    location: GameLocation | undefined,
    error: string | undefined,
    kind: 'game' | 'music',
  ): void {
    if (location?.found && location.path) {
      const mapArt = location.hasMapArt ? '' : ' (mapArt folder missing)';
      this.gamePath.set(location.path + mapArt);
      this.gameFound.set(location.hasMapArt);
    } else if (kind === 'game' && error) {
      this.gamePath.set(error);
      this.gameFound.set(false);
    } else if (!location?.found) {
      this.gamePath.set('Brawlhalla not found. Choose the game folder.');
      this.gameFound.set(false);
    }

    if (location?.hasMp3 && location.mp3Path) {
      this.hasMp3.set(true);
      this.mp3Path.set(location.mp3Path);
      this.gameLocation.mp3Path.set(location.mp3Path);
    } else {
      this.hasMp3.set(false);
      this.gameLocation.mp3Path.set(null);
      this.mp3Path.set(
        kind === 'music' && error
          ? error
          : 'Audio folder not found. Set audio folder (audio\\pc).',
      );
    }
  }
}
