import { computed, inject, Injectable, signal } from '@angular/core';
import { ApplyAttempt } from '../ipc/contracts/apply.contracts';
import { Loadout, NamedConfig, NamedConfigList } from '../ipc/contracts/loadout.contracts';
import { IPC_MESSAGE } from '../ipc/ipc.constants';
import { IpcService } from '../ipc/ipc.service';

@Injectable({ providedIn: 'root' })
export class LoadoutService {
  private readonly ipc = inject(IpcService);

  readonly loadout = signal<Loadout>({ maps: [], music: [] });
  readonly configs = signal<NamedConfig[]>([]);
  readonly busy = signal(false);
  readonly activeMapIds = computed(
    () => new Set(this.loadout().maps.map((entry) => entry.modId)),
  );
  readonly activeMusicIds = computed(
    () => new Set(this.loadout().music.map((entry) => entry.modId)),
  );

  constructor() {
    this.refresh();
    this.refreshConfigs();
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

  rankedSafe(): Promise<{ ok: boolean; message: string }> {
    return this.runAction(IPC_MESSAGE.MODS_RANKED_SAFE);
  }

  reapply(): Promise<{ ok: boolean; message: string }> {
    return this.runAction(IPC_MESSAGE.MODS_REAPPLY);
  }

  refreshConfigs(): Promise<void> {
    return this.ipc.request(IPC_MESSAGE.CONFIGS_LIST).then((reply) => {
      if (!reply.ok) {
        return;
      }

      const payload = reply.payload as NamedConfigList | undefined;
      this.configs.set(payload?.configs ?? []);
    });
  }

  saveCurrent(name: string): Promise<{ ok: boolean; message: string }> {
    this.busy.set(true);
    return this.ipc
      .request(IPC_MESSAGE.CONFIGS_SAVE, { name })
      .then((reply) => {
        const message = reply.ok ? 'Saved.' : (reply.error ?? 'Save failed.');
        return this.refreshConfigs().then(() => ({ ok: reply.ok === true, message }));
      })
      .finally(() => {
        this.busy.set(false);
      });
  }

  loadConfig(id: string): Promise<{ ok: boolean; message: string }> {
    return this.runAction(IPC_MESSAGE.CONFIGS_LOAD, { id });
  }

  deleteConfig(id: string): Promise<{ ok: boolean; message: string }> {
    this.busy.set(true);
    return this.ipc
      .request(IPC_MESSAGE.CONFIGS_DELETE, { id })
      .then((reply) => {
        const message = reply.ok ? 'Deleted.' : (reply.error ?? 'Delete failed.');
        return this.refreshConfigs().then(() => ({ ok: reply.ok === true, message }));
      })
      .finally(() => {
        this.busy.set(false);
      });
  }

  private runAction(
    type: string,
    payload?: unknown,
  ): Promise<{ ok: boolean; message: string }> {
    this.busy.set(true);
    return this.ipc
      .request(type, payload)
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
