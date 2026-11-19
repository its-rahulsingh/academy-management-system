import { Component, effect, inject, input, output, signal } from '@angular/core';
import { Api, Notify, errorMessage } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { Branding } from '../../core/models';
import { UI, readLogo } from '../../shared/ui';

/**
 * Logo and tagline editor. The Super Admin uses it for their own academy (`branding`);
 * the platform owner uses it from an academy's page (`platform/tenants/{id}/branding`).
 */
@Component({
  selector: 'app-branding-editor',
  imports: [UI],
  template: `
    @if (error()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }
    @if (branding(); as b) {
      <div class="branding-editor">
        <div class="branding-preview" aria-label="Preview">
          <app-brand-mark [name]="b.name" [logoUrl]="b.logoUrl" />
          <div>
            <strong>{{ b.name }}</strong>
            @if (tagline.trim()) {
              <small>{{ tagline.trim() }}</small>
            }
          </div>
        </div>

        <div>
          <label>Logo</label>
          <p class="helper">Shown beside the academy name for everyone who signs in, on the browser tab, and on the admission form. A square PNG or JPEG works best; it is resized automatically.</p>
          <div class="button-row tight">
            <label class="secondary file-button">
              {{ b.logoUrl ? 'Replace logo' : 'Upload logo' }}
              <input type="file" accept="image/png,image/jpeg,image/webp" (change)="upload($event)" [disabled]="busy()" />
            </label>
            @if (b.logoUrl) {
              <button type="button" class="secondary danger-text" (click)="removeLogo()" [disabled]="busy()">Remove logo</button>
            }
          </div>
        </div>

        <div>
          <label for="tagline">Tagline</label>
          <input id="tagline" [(ngModel)]="tagline" maxlength="120" placeholder="e.g. Excellence in coaching since 2010" />
          <p class="helper">Appears under the academy name. Leave it empty to show only the name.</p>
          <button type="button" class="primary" (click)="saveTagline()" [disabled]="busy() || tagline.trim() === b.tagline">Save tagline</button>
        </div>
      </div>
    } @else if (!error()) {
      <div class="empty">Loading…</div>
    }
  `,
})
export class BrandingEditorComponent {
  private readonly api = inject(Api);
  private readonly notify = inject(Notify);

  /** API path of the branding resource. */
  readonly endpoint = input.required<string>();
  /** Fired after any change, so the page can refresh what depends on it. */
  readonly changed = output<void>();

  protected readonly branding = signal<Branding | null>(null);
  protected readonly error = signal('');
  protected readonly busy = signal(false);
  protected tagline = '';

  constructor() {
    effect(() => this.load(this.endpoint()));
  }

  private load(endpoint = this.endpoint()): void {
    this.api.get<Branding>(endpoint).subscribe({
      next: b => {
        this.branding.set(b);
        this.tagline = b.tagline;
      },
      error: e => this.error.set(errorMessage(e)),
    });
  }

  private run(request: ReturnType<Api['put']>, message: string): void {
    this.busy.set(true);
    this.error.set('');
    request.subscribe({
      next: () => {
        this.busy.set(false);
        this.notify.success(message);
        this.load();
        this.changed.emit();
      },
      error: e => {
        this.busy.set(false);
        this.error.set(errorMessage(e));
      },
    });
  }

  protected async upload(event: Event): Promise<void> {
    const inputElement = event.target as HTMLInputElement;
    const file = inputElement.files?.[0];
    inputElement.value = '';
    if (!file) return;
    try {
      const dataUrl = await readLogo(file);
      this.run(this.api.put(`${this.endpoint()}/logo`, { dataUrl }), 'Logo updated');
    } catch (e) {
      this.error.set((e as Error).message);
    }
  }

  protected removeLogo(): void {
    this.run(this.api.delete(`${this.endpoint()}/logo`), 'Logo removed');
  }

  protected saveTagline(): void {
    this.run(this.api.put(this.endpoint(), { tagline: this.tagline.trim() }), 'Tagline saved');
  }
}

/** The Super Admin's "Academy profile" page. */
@Component({
  selector: 'app-branding-page',
  imports: [BrandingEditorComponent],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">ACADEMY WORKSPACE</div>
        <h1>Academy profile</h1>
        <p>How your academy appears to staff, students and parents.</p>
      </div>
    </div>
    <section class="surface">
      <app-branding-editor endpoint="branding" (changed)="auth.refresh()" />
    </section>
  `,
})
export class BrandingPageComponent {
  protected readonly auth = inject(AuthService);
}
