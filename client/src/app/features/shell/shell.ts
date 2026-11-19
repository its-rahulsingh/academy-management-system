import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AcademicYearService } from '../../core/academic-year.service';
import { Notify, errorMessage } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { Permission } from '../../core/models';
import { UI, initials } from '../../shared/ui';

interface NavItem {
  label: string;
  icon: string;
  link: string;
  permission?: string;
  exact?: boolean;
}

const ACADEMY_NAV: NavItem[] = [
  { label: 'Dashboard', icon: '▦', link: '/app/dashboard' },
  { label: 'Students', icon: '♙', link: '/app/students', permission: Permission.StudentsRead },
  { label: 'Admissions', icon: '✎', link: '/app/admissions', permission: Permission.AdmissionsManage },
  { label: 'Courses', icon: '▤', link: '/app/courses', permission: Permission.CoursesRead },
  { label: 'Faculty', icon: '♧', link: '/app/faculty', permission: Permission.FacultyRead },
  { label: 'Payments', icon: '▣', link: '/app/payments', permission: Permission.PaymentsRead },
  { label: 'Expenses', icon: '▥', link: '/app/expenses', permission: Permission.ExpensesRead },
  { label: 'Tests', icon: '☑', link: '/app/tests', permission: Permission.TestsRead },
  { label: 'Reports', icon: '◔', link: '/app/reports', permission: Permission.ReportsRead },
  { label: 'Users', icon: '⚙', link: '/app/users', permission: Permission.UsersManage },
  { label: 'Activity log', icon: '☰', link: '/app/audit', permission: Permission.AuditRead },
  { label: 'Academic years', icon: '◷', link: '/app/academic-years', permission: Permission.YearsManage },
  { label: 'Academy profile', icon: '◎', link: '/app/profile', permission: Permission.BrandingManage },
];

const BILLING_NAV: NavItem = { label: 'Subscription', icon: '◈', link: '/app/billing', permission: Permission.BillingManage };

const PLATFORM_NAV: NavItem[] = [
  { label: 'Overview', icon: '▦', link: '/platform', exact: true },
  { label: 'Academies', icon: '⌂', link: '/platform/academies' },
  { label: 'Plans', icon: '▤', link: '/platform/plans' },
  { label: 'Invoices', icon: '▣', link: '/platform/invoices' },
  { label: 'Activity log', icon: '☰', link: '/platform/audit' },
];

/** Frame around every signed-in screen: navigation, account controls, notices. Used by academies and by the owner. */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, UI],
  template: `
    <div class="shell">
      <aside class="sidebar no-print">
        <div class="side-brand">
          <app-brand-mark [name]="auth.tenant()?.name ?? 'AcademyFlow'" [logoUrl]="auth.tenant()?.logoUrl ?? null" />
          <div>
            <strong>{{ auth.tenant()?.name ?? 'AcademyFlow' }}</strong>
            @if (auth.isOwner()) {
              <small>Platform console</small>
            } @else if (auth.tenant()?.tagline) {
              <small>{{ auth.tenant()?.tagline }}</small>
            }
          </div>
        </div>
        <div class="sidebar-caption">{{ auth.isOwner() ? 'PLATFORM' : 'WORKSPACE' }}</div>
        <nav aria-label="Main">
          @for (item of nav(); track item.link) {
            <a class="nav-item" [routerLink]="item.link" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: item.exact ?? false }">
              <span class="nav-ico">{{ item.icon }}</span>{{ item.label }}
            </a>
          }
        </nav>
        <div class="sidebar-foot">
          <div class="user-chip">
            <div class="avatar">{{ userInitials() }}</div>
            <div>
              <strong>{{ auth.user()?.name }}</strong>
              <small>{{ auth.user()?.role }}</small>
            </div>
          </div>
          <button type="button" class="sign-out" (click)="openPassword()">⚿ &nbsp; Change password</button>
          <button type="button" class="sign-out" (click)="signOut()">↪ &nbsp; Sign out</button>
        </div>
      </aside>

      <main class="main">
        <header class="mobile-top no-print">
          <app-brand-mark [name]="auth.tenant()?.name ?? 'AcademyFlow'" [logoUrl]="auth.tenant()?.logoUrl ?? null" /><strong>{{ auth.tenant()?.name ?? 'AcademyFlow' }}</strong>
          <button type="button" (click)="signOut()">Sign out</button>
        </header>
        <div class="page">
          @if (years.selected(); as year) {
            <div class="year-bar no-print">
              <label for="academic-year">Academic year</label>
              <select id="academic-year" [ngModel]="years.selectedId()" (ngModelChange)="years.select($event)">
                @for (y of years.years(); track y.id) {
                  <option [value]="y.id">{{ y.name }}{{ y.isCurrent ? ' (current)' : y.isClosed ? ' (ended)' : ' (upcoming)' }}</option>
                }
              </select>
              <span class="year-dates">{{ year.startDate | day }} – {{ year.endDate | day }}</span>
              @if (!years.isCurrent() && years.currentId()) {
                <button type="button" class="text-button" (click)="years.select(years.currentId()!)">Back to current year</button>
              }
            </div>
            @if (year.isClosed) {
              <div class="alert no-print" role="status">
                <span>You are viewing {{ year.name }}, which has ended. New admissions are closed for it; outstanding fees can still be collected.</span>
              </div>
            }
          }
          @if (auth.access()?.inGrace) {
            <div class="alert no-print" role="status">
              <span>{{ auth.access()?.reason }}</span>
              @if (auth.can(billingPermission)) {
                <a class="text-button" routerLink="/app/billing">Pay now →</a>
              }
            </div>
          }
          @if (notify.message()) {
            <div class="success no-print" role="status">
              ✓ {{ notify.message() }} <button type="button" (click)="notify.clear()" aria-label="Dismiss">✕</button>
            </div>
          }
          @if (years.ready()) {
            <!-- Re-created when the year changes, so every screen reloads its data for the new year. -->
            @for (key of [years.selectedId()]; track key) {
              <router-outlet />
            }
          }
          <footer class="no-print">AcademyFlow <span>•</span> Secure academy workspace</footer>
        </div>
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
          <p class="helper">Your password was set by an administrator. Choose one only you know before continuing.</p>
        }
        <label for="current">Current password</label>
        <input id="current" type="password" autocomplete="current-password" [(ngModel)]="currentPassword" />
        <label for="new">New password (at least 12 characters)</label>
        <input id="new" type="password" autocomplete="new-password" [(ngModel)]="newPassword" />
        <label for="repeat">Repeat the new password</label>
        <input id="repeat" type="password" autocomplete="new-password" [(ngModel)]="repeatPassword" />
      </app-modal>
    }
  `,
})
export class ShellComponent {
  protected readonly auth = inject(AuthService);
  protected readonly notify = inject(Notify);
  protected readonly years = inject(AcademicYearService);
  private readonly router = inject(Router);

  protected readonly billingPermission = Permission.BillingManage;
  protected readonly passwordOpen = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected currentPassword = '';
  protected newPassword = '';
  protected repeatPassword = '';

  constructor() {
    // Only an academy with access has year-based screens; the owner and a locked academy skip straight on.
    if (this.auth.tenant() && this.auth.hasAccess()) void this.years.load();
    else this.years.ready.set(true);
  }

  protected readonly userInitials = computed(() => initials(this.auth.user()?.name));

  protected readonly nav = computed<NavItem[]>(() => {
    if (this.auth.isOwner()) return PLATFORM_NAV;
    // With a lapsed subscription the only useful destination is the page where it can be paid.
    const items = this.auth.hasAccess() ? [...ACADEMY_NAV, BILLING_NAV] : [BILLING_NAV];
    return items.filter(item => !item.permission || this.auth.can(item.permission));
  });

  protected openPassword(): void {
    this.currentPassword = this.newPassword = this.repeatPassword = '';
    this.error.set('');
    this.passwordOpen.set(true);
  }

  protected closePassword(): void {
    // A forced change cannot be dismissed; signing out is the only other way forward.
    if (!this.auth.session().mustChangePassword) this.passwordOpen.set(false);
  }

  protected async changePassword(): Promise<void> {
    if (this.newPassword.length < 12) return this.error.set('Password must contain at least 12 characters.');
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
    this.years.reset();
    await this.router.navigateByUrl('/login');
  }
}
