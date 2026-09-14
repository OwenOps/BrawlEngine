import { Injectable, signal } from '@angular/core';
import { DownloadKind } from '../ipc/contracts/download.contracts';

const STORAGE_KEY = 'brawlengine.newDownloads.seen';
const FRESH_MS = 48 * 60 * 60 * 1000;

@Injectable({ providedIn: 'root' })
export class NewDownloadsService {
  private readonly seen = signal<ReadonlySet<string>>(readSeen());

  isNew(kind: DownloadKind, id: number, downloadedUtc: string | undefined): boolean {
    if (!isFresh(downloadedUtc) || this.seen().has(seenKey(kind, id))) {
      return false;
    }

    return true;
  }

  /** Download (or add zip) just finished — show New again. */
  forget(kind: DownloadKind, id: number): void {
    const key = seenKey(kind, id);
    if (!this.seen().has(key)) {
      return;
    }

    const next = new Set(this.seen());
    next.delete(key);
    this.persist(next);
  }

  /** Left On disk after seeing these cards. */
  markSeen(kind: DownloadKind, ids: number[]): void {
    if (ids.length === 0) {
      return;
    }

    const next = new Set(this.seen());
    let added = false;
    for (const id of ids) {
      const key = seenKey(kind, id);
      if (!next.has(key)) {
        next.add(key);
        added = true;
      }
    }

    if (added) {
      this.persist(next);
    }
  }

  private persist(next: Set<string>): void {
    this.seen.set(next);
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify([...next]));
    } catch {
      // Quota / private mode — New still works this session.
    }
  }
}

function seenKey(kind: DownloadKind, id: number): string {
  return kind + ':' + id;
}

function isFresh(downloadedUtc: string | undefined): boolean {
  if (!downloadedUtc) {
    return false;
  }

  const at = Date.parse(downloadedUtc);
  if (!Number.isFinite(at)) {
    return false;
  }

  const age = Date.now() - at;
  return age >= 0 && age < FRESH_MS;
}

function readSeen(): Set<string> {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return new Set();
    }

    const parsed = JSON.parse(raw) as unknown;
    if (!Array.isArray(parsed)) {
      return new Set();
    }

    return new Set(parsed.filter((row): row is string => typeof row === 'string'));
  } catch {
    return new Set();
  }
}
