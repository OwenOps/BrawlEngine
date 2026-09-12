import { inject, Injectable, NgZone } from '@angular/core';
import { photinoExternal } from './photino';

export interface IpcEnvelope {
  id: string;
  type: string;
  ok?: boolean;
  error?: string;
  payload?: unknown;
}

/** Folder pickers and downloads can wait a long time; catalogs must not spin forever. */
const NO_TIMEOUT = new Set<string>([
  'game.pick',
  'music.pick',
  'folder.open',
  'downloads.open',
  'browser.open',
  'mod.download',
  'sound.download',
  'skin.download',
  'downloads.cancel',
  'mod.apply',
  'mod.reset',
  'music.apply',
  'music.reset',
  'music.replace',
  'mods.resetAll',
  'mods.reapply',
  'mods.rankedSafe',
  'configs.load',
]);

@Injectable({ providedIn: 'root' })
export class IpcService {
  private readonly zone = inject(NgZone);
  private readonly pending = new Map<string, (msg: IpcEnvelope) => void>();
  private readonly pushListeners = new Map<string, Set<(msg: IpcEnvelope) => void>>();
  private listening = false;

  /** Subscribes to unsolicited messages the host sends outside the request/response flow (e.g. download progress). */
  on(type: string, handler: (msg: IpcEnvelope) => void): () => void {
    if (!this.hasHost()) {
      return () => {};
    }

    this.ensureListener();
    let handlers = this.pushListeners.get(type);
    if (!handlers) {
      handlers = new Set();
      this.pushListeners.set(type, handlers);
    }
    handlers.add(handler);
    return () => handlers!.delete(handler);
  }

  request(type: string, payload?: unknown): Promise<IpcEnvelope> {
    if (!this.hasHost()) {
      return Promise.resolve({
        id: '',
        type,
        ok: false,
        error: 'Photino host is not available (open the UI from the C# app).',
      });
    }

    this.ensureListener();
    const id = crypto.randomUUID();
    const envelope: IpcEnvelope = { id, type, payload };
    const timeoutMs = this.timeoutMsFor(type);

    return new Promise((resolve) => {
      let settled = false;
      const finish = (msg: IpcEnvelope): void => {
        if (settled) {
          return;
        }
        settled = true;
        this.pending.delete(id);
        resolve(msg);
      };

      this.pending.set(id, finish);
      photinoExternal()!.sendMessage(JSON.stringify(envelope));

      if (timeoutMs > 0) {
        window.setTimeout(() => {
          finish({
            id,
            type,
            ok: false,
            error: 'Timed out waiting for the host. Check your connection and Retry.',
          });
        }, timeoutMs);
      }
    });
  }

  private timeoutMsFor(type: string): number {
    if (NO_TIMEOUT.has(type)) {
      return 0;
    }
    if (type === 'catalog.byIds') {
      return 90000;
    }
    if (type.startsWith('catalog.')) {
      return 60000;
    }
    return 25000;
  }

  private hasHost(): boolean {
    return photinoExternal() !== undefined;
  }

  private ensureListener(): void {
    if (this.listening) {
      return;
    }
    this.listening = true;
    photinoExternal()!.receiveMessage((raw: string) => {
      this.zone.run(() => {
        let msg: IpcEnvelope;
        try {
          msg = JSON.parse(raw) as IpcEnvelope;
        } catch {
          console.error('IPC receive parse error:', raw);
          return;
        }
        const resolve = this.pending.get(msg.id);
        if (resolve) {
          resolve(msg);
          return;
        }

        const handlers = this.pushListeners.get(msg.type);
        if (handlers) {
          for (const handler of handlers) {
            handler(msg);
          }
        }
      });
    });
  }
}
