import { Component, OnDestroy, OnInit } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { IpcService } from './ipc/ipc.service';

export interface GameLocation {
  path: string | null;
  found: boolean;
  source: string;
  hasMapArt: boolean;
  cancelled?: boolean;
  error?: string;
}

export interface ApplyGuard {
  allowed: boolean;
  running: boolean;
  message: string | null;
}

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent implements OnInit, OnDestroy {
  hostStatus = 'Checking host…';
  gamePath = 'Looking for Brawlhalla…';
  runningMessage: string | null = null;
  picking = false;
  private runningTimer: ReturnType<typeof setInterval> | undefined;

  constructor(private readonly ipc: IpcService) {}

  ngOnInit(): void {
    this.ipc.request('ping').then((reply) => {
      this.hostStatus = reply.ok ? 'Host connected' : (reply.error ?? 'Host unavailable');
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
    this.ipc.request('game.pick').then((reply) => {
      this.picking = false;
      this.applyGame(reply.payload as GameLocation | undefined, reply.error);
    });
  }

  private refreshGame(): void {
    this.ipc.request('game.get').then((reply) => {
      this.applyGame(reply.payload as GameLocation | undefined, reply.error);
    });
  }

  private refreshRunning(): void {
    this.ipc.request('game.running').then((reply) => {
      const status = reply.payload as ApplyGuard | undefined;
      this.runningMessage = status?.running ? (status.message ?? 'Close Brawlhalla first.') : null;
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
}
