import { Injectable, inject } from '@angular/core';
import { DownloadKind } from '../ipc/contracts/download.contracts';
import { IPC_MESSAGE } from '../ipc/ipc.constants';
import { IpcService } from '../ipc/ipc.service';

@Injectable({ providedIn: 'root' })
export class BrowserService {
  private readonly ipc = inject(IpcService);

  open(url: string): void {
    void this.ipc.request(IPC_MESSAGE.BROWSER_OPEN, { url });
  }

  openSkinImages(name: string): void {
    const query = name.trim();
    if (!query) {
      return;
    }

    this.open(
      'https://www.google.com/search?tbm=isch&q=' +
        encodeURIComponent(query + ' Brawlhalla skin'),
    );
  }

  openFolder(path: string): Promise<string | null> {
    return this.ipc.request(IPC_MESSAGE.FOLDER_OPEN, { path }).then((reply) => {
      if (reply.ok) {
        return null;
      }
      return reply.error ?? 'Could not open that folder.';
    });
  }

  openDownloads(kind: DownloadKind): void {
    void this.ipc.request(IPC_MESSAGE.DOWNLOADS_OPEN, { kind });
  }

  pickDownloadsFolder(): Promise<boolean> {
    return this.ipc.request(IPC_MESSAGE.DOWNLOADS_PICK).then((reply) => reply.ok === true);
  }

  resetDownloadsFolder(): Promise<boolean> {
    return this.ipc.request(IPC_MESSAGE.DOWNLOADS_RESET).then((reply) => reply.ok === true);
  }

  importZip(kind: DownloadKind, id: number): Promise<'ok' | 'cancel' | 'fail'> {
    return this.ipc.request(IPC_MESSAGE.DOWNLOADS_IMPORT, { kind, id }).then((reply) => {
      if (!reply.ok) {
        return 'fail';
      }
      const result = reply.payload as { cancelled?: boolean } | undefined;
      return result?.cancelled ? 'cancel' : 'ok';
    });
  }
}
