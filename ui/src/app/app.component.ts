import { NgComponentOutlet } from '@angular/common';
import { Component, OnDestroy, OnInit, Type } from '@angular/core';
import { MapsPage } from './features/maps/maps.page';
import { MusicsPage } from './features/musics/musics.page';
import { ApplyGuard, GameLocation } from './core/ipc/contracts/game.contracts';
import { IPC_MESSAGE } from './core/ipc/ipc.constants';
import { IpcService } from './core/ipc/ipc.service';
import {
  APP_TABS,
  AppTabDef,
  AppTabId,
} from './core/navigation/app-tabs.config';
import { APP_SHELL_TEXT } from './core/ui/app-shell.constants';

@Component({
  selector: 'app-root',
  imports: [NgComponentOutlet],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent implements OnInit, OnDestroy {
  tabs = APP_TABS;
  activeTabId: AppTabId = APP_TABS[0].id;
  hostStatus: string = APP_SHELL_TEXT.hostChecking;
  gamePath: string = APP_SHELL_TEXT.gameLooking;
  runningMessage: string | null = null;
  picking = false;
  private runningTimer: ReturnType<typeof setInterval> | undefined;

  constructor(private readonly ipc: IpcService) {}

  ngOnInit(): void {
    this.ipc.request(IPC_MESSAGE.PING).then((reply) => {
      this.hostStatus = reply.ok
        ? 'Host connected'
        : (reply.error ?? 'Host unavailable');
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

  chooseFolder(): void {
    this.picking = true;
    this.ipc.request(IPC_MESSAGE.GAME_PICK).then((reply) => {
      this.picking = false;
      this.applyGame(reply.payload as GameLocation | undefined, reply.error);
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
      this.runningMessage = status?.running
        ? (status.message ?? 'Close Brawlhalla first.')
        : null;
    });
  }

  private applyGame(location: GameLocation | undefined, error?: string): void {
    if (error) {
      this.gamePath = error;
      return;
    }
    if (!location?.found || !location.path) {
      this.gamePath = 'Brawlhalla not found. Choose the game folder.';
      return;
    }
    const mapArt = location.hasMapArt ? '' : ' (mapArt folder missing)';
    this.gamePath = location.path + mapArt;
  }

  get activeTabComponent(): Type<unknown> {
    return (
      APP_TABS.find((tab) => tab.id === this.activeTabId)?.component ??
      APP_TABS[0].component
    );
  }
}
