import { Injectable, signal } from '@angular/core';

/** Short confirmation after Apply, Download, Reset, and the other finished actions. */
@Injectable({ providedIn: 'root' })
export class NoticeService {
  readonly message = signal<string | null>(null);
  private timer: ReturnType<typeof setTimeout> | undefined;

  show(message: string): void {
    const text = message.trim();
    if (text.length === 0) {
      return;
    }
    this.message.set(text);
    if (this.timer !== undefined) {
      clearTimeout(this.timer);
    }
    this.timer = setTimeout(() => this.message.set(null), 4500);
  }
}
