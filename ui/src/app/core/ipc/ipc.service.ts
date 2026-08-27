import { Injectable, NgZone } from '@angular/core';
import { photinoExternal } from './photino';

export interface IpcEnvelope {
  id: string;
  type: string;
  ok?: boolean;
  error?: string;
  payload?: unknown;
}

@Injectable({ providedIn: 'root' })
export class IpcService {
  private readonly pending = new Map<string, (msg: IpcEnvelope) => void>();
  private listening = false;

  constructor(private readonly zone: NgZone) {}

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

    return new Promise((resolve) => {
      this.pending.set(id, resolve);
      photinoExternal()!.sendMessage(JSON.stringify(envelope));
    });
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
          return;
        }
        const resolve = this.pending.get(msg.id);
        if (resolve) {
          this.pending.delete(msg.id);
          resolve(msg);
        }
      });
    });
  }
}
