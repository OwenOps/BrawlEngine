import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  computed,
  inject,
  signal,
} from '@angular/core';
import { ApplyGuard, GameLocation } from './core/ipc/contracts/game.contracts';
import { IPC_MESSAGE } from './core/ipc/ipc.constants';
import { IpcService } from './core/ipc/ipc.service';
import { LoadoutService } from './core/loadout/loadout.service';
import { APP_TABS, AppTabId } from './core/navigation/app-tabs.config';
import { APP_SHELL_TEXT, GAMEBANANA_GAME_URL } from './core/ui/app-shell.constants';
import { BrowserService } from './core/browser/browser.service';
import { GameLocationState } from './core/game/game-location.state';
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
  readonly theme = inject(ThemeService);
  readonly themeNames = Object.keys(THEME_PRESETS) as ThemeName[];
  private runningTimer: ReturnType<typeof setInterval> | undefined;

  readonly tabs = APP_TABS;
  readonly activeTabId = signal<AppTabId>(APP_TABS[0].id);
  readonly visitedTabIds = signal<ReadonlySet<AppTabId>>(new Set([APP_TABS[0].id]));
  readonly hostStatus = signal<string>(APP_SHELL_TEXT.hostChecking);
  readonly hostConnected = signal<boolean | null>(null);
  readonly gamePath = signal<string>(APP_SHELL_TEXT.gameLooking);
  readonly gameFound = signal(false);
  readonly mp3Path = signal<string>('Looking for mp3…');
  readonly hasMp3 = signal(false);
  readonly runningMessage = signal<string | null>(null);
  readonly picking = signal(false);
  readonly actionBusy = signal(false);
  readonly actionMessage = signal<string | null>(null);
  readonly configName = signal('');
  readonly configs = this.loadout.configs;
  readonly shellBusy = computed(
    () => this.picking() || this.actionBusy() || this.loadout.busy(),
  );
  readonly hasLoadout = computed(() => {
    const current = this.loadout.loadout();
    return current.maps.length > 0 || current.music.length > 0;
  });

  constructor() {
    this.ipc.request(IPC_MESSAGE.PING).then((reply) => {
      this.hostConnected.set(reply.ok === true);
      this.hostStatus.set(
        reply.ok ? 'Host connected' : (reply.error ?? 'Host unavailable'),
      );
    });
    this.refreshGame();
    this.refreshRunning();
    this.runningTimer = setInterval(() => this.refreshRunning(), 2000);
  }

  ngOnDestroy(): void {
    if (this.runningTimer !== undefined) {
      clearInterval(this.runningTimer);
    }
  }

  selectTheme(name: ThemeName): void {
    this.theme.setTheme(name);
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
    this.runLoadoutAction(() => this.loadout.resetAll());
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

  private refreshGame(): void {
    this.ipc.request(IPC_MESSAGE.GAME_GET).then((reply) => {
      this.applyGame(reply.payload as GameLocation | undefined, reply.error, 'game');
    });
  }

  private refreshRunning(): void {
    this.ipc.request(IPC_MESSAGE.GAME_RUNNING).then((reply) => {
      const status = reply.payload as ApplyGuard | undefined;
      this.runningMessage.set(
        status?.running ? (status.message ?? 'Close Brawlhalla first.') : null,
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
          : 'Audio folder not found. Set music folder (audio\\pc).',
      );
    }
  }
}
