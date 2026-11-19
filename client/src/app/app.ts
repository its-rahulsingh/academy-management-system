import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { BrandingService } from './core/branding';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  template: `<router-outlet />`,
})
export class App {
  // Created here so the tab icon and title follow whoever is signed in, on every page.
  private readonly branding = inject(BrandingService);
}
