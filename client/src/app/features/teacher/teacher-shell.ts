import { Component, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Notify, errorMessage } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { UI } from '../../shared/ui';

/** Tells the portal pages to fetch their data again (the refresh button in the top bar). */
export const portalRefresh = signal(0);

const PORTAL_NAV = {
  teacher: [
    { label: 'Dashboard', icon: '▦', link: '/teacher', exact: true },
    { label: 'My payments', icon: '▣', link: '/teacher/payments', exact: false },
  ],
  student: [
    { label: 'My dashboard', icon: '▦', link: '/student', exact: true },
    { label: 'Report card', icon: '☑', link: '/student/report-card', exact: false },
  ],
};

/**
 * Frame around the faculty and student portals. Deliberately simpler than the staff workspace: a couple of pages,
 * no sidebar, built to be used on a phone.
 */
@Component({
  selector: 'app-portal-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, UI],
  template: `
    <div class="teacher">
      <header class="teacher-top no-print">
        <div class="teacher-brand">
          <app-brand-mark [name]="auth.tenant()?.name ?? 'AcademyFlow'" [logoUrl]="auth.tenant()?.logoUrl ?? null" />
          <div>
            <strong>{{ auth.tenant()?.name }}</strong>
            @if (auth.tenant()?.tagline) {
              <small>{{ auth.tenant()?.tagline }}</small>
            }
          </div>
        </div>
        <nav class="teacher-nav" aria-label="Portal">
          @for (item of links(); track item.link) {
            <a [routerLink]="item.link" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: item.exact }"><span aria-hidden="true">{{ item.icon }}</span> {{ item.label }}</a>
          }
          <button type="button" class="round" (click)="refresh()" title="Refresh" aria-label="Refresh">⟳</button>
          <button type="button" class="round" (click)="menuOpen.set(!menuOpen())" title="Account" aria-label="Account" [attr.aria-expanded]="menuOpen()">⚙</button>
          @if (menuOpen()) {
            <div class="teacher-menu" role="menu">
              <div class="who"><strong>{{ auth.user()?.name }}</strong><small>{{ auth.user()?.email }}</small></div>
              <button type="button" role="menuitem" (click)="openPassword()">⚿ Change password</button>
              <button type="button" role="menuitem" (click)="signOut()">↪ Sign out</button>
            </div>
          }
        </nav>
      </header>

      <main class="teacher-page">
        @if (notify.message()) {
          <div class="success no-print" role="status">
            ✓ {{ notify.message() }} <button type="button" (click)="notify.clear()" aria-label="Dismiss">✕</button>
          </div>
        }
        <router-outlet />
      </main>
    </div>

    @if (passwordOpen() || auth.session().mustChangePassword) {
      <app-modal
        [title]="auth.session().mustChangePassword ? 'Choose your own password' : 'Change password'"
        saveLabel="Change password"
        [busy]="busy()"
        [error]="error()"
        (closed)="closePassword()"
        (saved)="changePassword()"
      >
        @if (auth.session().mustChangePassword) {
          <p class="helper">Your academy set this password for you. Choose one only you know before continuing.</p>
        }
        <label for="current">Current password</label>
        <input id="current" type="password" autocomplete="current-password" [(ngModel)]="currentPassword" />
        <label for="new">New password (at least 5 characters)</label>
        <input id="new" type="password" autocomplete="new-password" [(ngModel)]="newPassword" />
        <label for="repeat">Repeat the new password</label>
        <input id="repeat" type="password" autocomplete="new-password" [(ngModel)]="repeatPassword" />
      </app-modal>
    }
  `,
})
export class PortalShellComponent {
  /** Which portal this is; set from the route's data. */
  readonly nav = input<keyof typeof PORTAL_NAV>('teacher');
  protected readonly links = computed(() => PORTAL_NAV[this.nav()]);
  protected readonly auth = inject(AuthService);
  protected readonly notify = inject(Notify);
  private readonly router = inject(Router);

  protected readonly menuOpen = signal(false);
  protected readonly passwordOpen = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected currentPassword = '';
  protected newPassword = '';
  protected repeatPassword = '';

  protected refresh(): void {
    portalRefresh.update(n => n + 1);
  }

  protected openPassword(): void {
    this.menuOpen.set(false);
    this.currentPassword = this.newPassword = this.repeatPassword = '';
    this.error.set('');
    this.passwordOpen.set(true);
  }

  protected closePassword(): void {
    if (!this.auth.session().mustChangePassword) this.passwordOpen.set(false);
  }

  protected async changePassword(): Promise<void> {
    if (this.newPassword.length < 5) return this.error.set('Password must contain at least 5 characters.');
    if (this.newPassword !== this.repeatPassword) return this.error.set('The two new passwords do not match.');
    this.busy.set(true);
    this.error.set('');
    try {
      await this.auth.changePassword(this.currentPassword, this.newPassword);
      this.passwordOpen.set(false);
      this.notify.success('Password changed');
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async signOut(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }
}
