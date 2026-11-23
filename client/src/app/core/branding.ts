import { DOCUMENT } from '@angular/common';
import { Injectable, effect, inject } from '@angular/core';
import { AuthService } from './auth.service';

const DEFAULT_TITLE = 'Ojas';

/**
 * Puts the signed-in academy's logo on the browser tab and its name in the tab title.
 * Signed out, or for the platform owner, the tab shows Ojas's own icon.
 */
@Injectable({ providedIn: 'root' })
export class BrandingService {
  private readonly document = inject(DOCUMENT);
  private readonly auth = inject(AuthService);
  private readonly defaultIcon = this.icon().getAttribute('href') ?? '';

  constructor() {
    effect(() => {
      const tenant = this.auth.tenant();
      this.document.title = tenant?.name ?? DEFAULT_TITLE;
      this.setIcon(tenant?.logoUrl ?? this.defaultIcon);
    });
  }

  private setIcon(href: string): void {
    const link = this.icon();
    if (link.getAttribute('href') !== href) link.setAttribute('href', href);
  }

  private icon(): HTMLLinkElement {
    let link = this.document.querySelector<HTMLLinkElement>('link[rel="icon"]');
    if (!link) {
      link = this.document.createElement('link');
      link.rel = 'icon';
      this.document.head.appendChild(link);
    }
    return link;
  }
}
