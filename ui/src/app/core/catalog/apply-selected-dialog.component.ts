import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { GameLocationState } from '../game/game-location.state';
import { ApplySelectedRow, defaultApplySelected } from './apply-selected';

@Component({
  selector: 'app-apply-selected-dialog',
  standalone: true,
  templateUrl: './apply-selected-dialog.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ApplySelectedDialogComponent {
  private readonly game = inject(GameLocationState);

  readonly open = input(false);
  readonly heading = input('Apply selected');
  readonly rows = input<ApplySelectedRow[]>([]);
  readonly applying = input(false);
  readonly status = input<string | null>(null);
  readonly apply = output<number[]>();
  readonly closed = output<void>();

  readonly runningMessage = this.game.runningMessage;
  readonly selected = signal<ReadonlySet<number>>(new Set());

  constructor() {
    effect(() => {
      if (!this.open()) {
        return;
      }

      const rows = this.rows();
      untracked(() => this.selected.set(defaultApplySelected(rows)));
    });
  }

  isChecked(id: number): boolean {
    return this.selected().has(id);
  }

  toggle(row: ApplySelectedRow, event: Event): void {
    if (!row.canApply || this.applying()) {
      return;
    }

    const checked = (event.target as HTMLInputElement).checked;
    this.selected.update((ids) => {
      const next = new Set(ids);
      if (checked) {
        next.add(row.id);
      } else {
        next.delete(row.id);
      }
      return next;
    });
  }

  confirm(): void {
    if (this.applying() || this.selected().size === 0) {
      return;
    }

    this.apply.emit([...this.selected()]);
  }

  requestClose(): void {
    if (this.applying()) {
      return;
    }

    this.closed.emit();
  }
}
