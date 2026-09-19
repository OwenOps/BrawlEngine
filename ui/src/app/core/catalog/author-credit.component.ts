import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { CatalogItem } from '../ipc/contracts/catalog.contracts';
import { formatCount } from './format-count';

@Component({
  selector: 'app-author-credit',
  standalone: true,
  templateUrl: './author-credit.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuthorCreditComponent {
  readonly item = input.required<CatalogItem>();
  readonly showCategory = input(false);
  readonly profile = output<string>();
  readonly more = output<CatalogItem>();
  readonly formatCount = formatCount;

  openProfile(): void {
    const url = this.item().authorUrl?.trim();
    if (url) {
      this.profile.emit(url);
    }
  }

  openMore(): void {
    this.more.emit(this.item());
  }
}
