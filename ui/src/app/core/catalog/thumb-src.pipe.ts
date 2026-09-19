import { Pipe, PipeTransform, inject } from '@angular/core';
import { DomSanitizer, SafeUrl } from '@angular/platform-browser';

/** Photino `app://library/...` is not in Angular's default img allow-list. */
@Pipe({
  name: 'thumbSrc',
  standalone: true,
})
export class ThumbSrcPipe implements PipeTransform {
  private readonly sanitizer = inject(DomSanitizer);

  transform(url: string | null): SafeUrl | string | null {
    if (!url) {
      return null;
    }

    if (url.startsWith('app:')) {
      return this.sanitizer.bypassSecurityTrustUrl(url);
    }

    return url;
  }
}
