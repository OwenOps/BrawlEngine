import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { DownloadKind } from '../ipc/contracts/download.contracts';
import {
  AddModMode,
  gameBananaExampleUrl,
  readGameBananaItemId,
} from './gamebanana-id';

@Component({
  selector: 'app-add-mod-dialog',
  standalone: true,
  templateUrl: './add-mod-dialog.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AddModDialogComponent {
  readonly open = input(false);
  readonly mode = input<AddModMode>('zip');
  readonly kind = input<DownloadKind>('maps');
  readonly picked = output<number>();
  readonly closed = output<void>();

  readonly typed = signal('');
  readonly error = signal<string | null>(null);
  readonly example = computed(() => gameBananaExampleUrl(this.kind()));
  readonly heading = computed(() =>
    this.mode() === 'zip' ? 'Add zip' : 'Download from URL',
  );
  readonly hint = computed(() =>
    this.mode() === 'zip'
      ? 'Paste the GameBanana URL or id so the folder name matches. A local zip has no catalog thumbnail.'
      : 'Paste the GameBanana page. This downloads the archive and the card image.',
  );
  readonly confirmLabel = computed(() =>
    this.mode() === 'zip' ? 'Choose files…' : 'Download',
  );

  constructor() {
    effect(() => {
      if (!this.open()) {
        return;
      }

      untracked(() => {
        this.typed.set('');
        this.error.set(null);
      });
    });
  }

  setTyped(event: Event): void {
    this.typed.set((event.target as HTMLInputElement).value);
  }

  confirm(): void {
    const result = readGameBananaItemId(this.typed(), this.kind());
    if ('error' in result) {
      this.error.set(result.error);
      return;
    }

    this.picked.emit(result.id);
  }

  requestClose(): void {
    this.closed.emit();
  }
}
