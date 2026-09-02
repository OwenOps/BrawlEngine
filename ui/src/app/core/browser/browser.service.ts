import { Injectable, inject } from '@angular/core';
import { IPC_MESSAGE } from '../ipc/ipc.constants';
import { IpcService } from '../ipc/ipc.service';

@Injectable({ providedIn: 'root' })
export class BrowserService {
  private readonly ipc = inject(IpcService);

  open(url: string): void {
    void this.ipc.request(IPC_MESSAGE.BROWSER_OPEN, { url });
  }

  openFolder(path: string): void {
    void this.ipc.request(IPC_MESSAGE.FOLDER_OPEN, { path });
  }
}
