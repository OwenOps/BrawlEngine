import { NgComponentOutlet } from '@angular/common';
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
import { APP_SHELL_TEXT } from './core/ui/app-shell.constants';

@Component({
  selector: 'app-root',
  imports: [NgComponentOutlet],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppComponent implements OnDestroy {
  private readonly ipc = inject(IpcService);
  private readonly loadout = inject(LoadoutService);
  private runningTimer: ReturnType<typeof setInterval> | undefined;

  readonly tabs = APP_TABS;
  readonly activeTabId = signal<AppTabId>(APP_TABS[0].id);
  readonly hostStatus = signal<string>(APP_SHELL_TEXT.hostChecking);
  readonly gamePath = signal<string>(APP_SHELL_TEXT.gameLooking);
  readonly runningMessage = signal<string | null>(null);
  readonly picking = signal(false);
  readonly actionBusy = signal(false);
  readonly actionMessage = signal<string | null>(null);
  readonly shellBusy = computed(
    () => this.picking() || this.actionBusy() || this.loadout.busy(),
  );
  readonly activeTabComponent = computed(
    () =>
      APP_TABS.find((tab) => tab.id === this.activeTabId())?.component ??
      APP_TABS[0].component,
  );

  constructor() {
    this.ipc.request(IPC_MESSAGE.PING).then((reply) => {
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

  selectTab(id: AppTabId): void {
    this.activeTabId.set(id);
  }

  chooseFolder(): void {
    this.picking.set(true);
    this.ipc.request(IPC_MESSAGE.GAME_PICK).then((reply) => {
      this.picking.set(false);
      this.applyGame(reply.payload as GameLocation | undefined, reply.error);
    });
  }

  resetAll(): void {
    this.runLoadoutAction(() => this.loadout.resetAll());
  }

  reapply(): void {
    this.runLoadoutAction(() => this.loadout.reapply());
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
      this.applyGame(reply.payload as GameLocation | undefined, reply.error);
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

  private applyGame(location: GameLocation | undefined, error?: string): void {
    if (error) {
      this.gamePath.set(error);
      return;
    }
    if (!location?.found || !location.path) {
      this.gamePath.set('Brawlhalla not found. Choose the game folder.');
      return;
    }
    const mapArt = location.hasMapArt ? '' : ' (mapArt folder missing)';
    this.gamePath.set(location.path + mapArt);
  }
}
