import { computed, inject, Injectable, signal } from '@angular/core';
import { ApplyAttempt } from '../ipc/contracts/apply.contracts';
import { Loadout } from '../ipc/contracts/loadout.contracts';
import { IPC_MESSAGE } from '../ipc/ipc.constants';
import { IpcService } from '../ipc/ipc.service';

@Injectable({ providedIn: 'root' })
export class LoadoutService {
  private readonly ipc = inject(IpcService);

  readonly loadout = signal<Loadout>({ maps: [], music: [] });
  readonly busy = signal(false);
  readonly activeMapIds = computed(
    () => new Set(this.loadout().maps.map((entry) => entry.modId)),
  );
  readonly activeMusicIds = computed(
    () => new Set(this.loadout().music.map((entry) => entry.modId)),
  );

  constructor() {
    this.refresh();
  }

  refresh(): Promise<void> {
    return this.ipc.request(IPC_MESSAGE.LOADOUT_GET).then((reply) => {
      if (!reply.ok) {
        return;
      }

      const payload = reply.payload as Loadout | undefined;
      this.loadout.set({
        maps: payload?.maps ?? [],
        music: payload?.music ?? [],
      });
    });
  }

  resetAll(): Promise<{ ok: boolean; message: string }> {
    return this.runAction(IPC_MESSAGE.MODS_RESET_ALL);
  }

  reapply(): Promise<{ ok: boolean; message: string }> {
    return this.runAction(IPC_MESSAGE.MODS_REAPPLY);
  }

  private runAction(type: string): Promise<{ ok: boolean; message: string }> {
    this.busy.set(true);
    return this.ipc
      .request(type)
      .then((reply) => {
        const result = reply.payload as ApplyAttempt | undefined;
        const message = reply.ok
          ? (result?.reason ?? 'Done.')
          : (reply.error ?? 'Request failed.');
        return this.refresh().then(() => ({ ok: reply.ok === true, message }));
      })
      .finally(() => {
        this.busy.set(false);
      });
  }
}
