import { Component, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Api, FormState, Notify, PagedList, errorMessage } from '../../core/api';
import { Paged, Plan, TenantRow } from '../../core/models';
import { UI } from '../../shared/ui';

@Component({
  selector: 'app-academies',
  imports: [RouterLink, UI],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">PLATFORM</div>
        <h1>Academies</h1>
        <p>Every academy on the platform and the state of its subscription.</p>
      </div>
      <button type="button" class="primary" (click)="open()">+ New academy</button>
    </div>

    @if (list.error() || error()) {
      <div class="alert" role="alert">{{ list.error() || error() }}</div>
    }

    <div class="filter-bar">
      <input placeholder="⌕  Search name, code or contact email…" [ngModel]="search()" (ngModelChange)="onSearch($event)" />
      <select [ngModel]="status()" (ngModelChange)="status.set($event); list.reload()" aria-label="Status">
        <option>All</option><option>Active</option><option>Suspended</option>
      </select>
    </div>

    <div class="surface table-scroll">
      <table>
        <thead><tr><th>ACADEMY</th><th>PLAN</th><th>SUBSCRIPTION</th><th>PAID THROUGH</th><th>STUDENTS</th><th>USERS</th><th>STATUS</th><th></th></tr></thead>
        <tbody>
          @for (t of list.items(); track t.id) {
            <tr>
              <td><strong>{{ t.name }}</strong><small>code: {{ t.code }} · {{ t.contactEmail }}</small></td>
              <td>{{ t.subscription?.planName ?? '—' }}<small>{{ t.subscription ? (t.subscription.price | money) + ' / ' + t.subscription.interval.toLowerCase() : '' }}</small></td>
              <td>
                @if (t.subscription; as s) {
                  <app-status [value]="s.status" />
                } @else {
                  <app-status value="None" />
                }
              </td>
              <td>{{ t.subscription?.currentPeriodEnd | day }}</td>
              <td>{{ t.students }}</td>
              <td>{{ t.users }}</td>
              <td><app-status [value]="t.status" /></td>
              <td class="row-actions"><a class="table-action" [routerLink]="['/platform/academies', t.id]">Manage →</a></td>
            </tr>
          } @empty {
            <tr><td colspan="8" class="empty">{{ list.loading() ? 'Loading…' : 'No academies yet. Create the first one.' }}</td></tr>
          }
        </tbody>
      </table>
      <app-pager [page]="list.page()" [total]="list.total()" (pageChange)="list.load($event)" />
    </div>

    @if (form.open()) {
      <app-modal title="New academy" saveLabel="Create academy" [wide]="true" [busy]="form.busy()" [error]="form.error()" (closed)="form.close()" (saved)="save()">
        <h3 class="section-title">Academy</h3>
        <div class="two-fields">
          <div><label for="name">Academy name *</label><input id="name" [(ngModel)]="model.name" (ngModelChange)="suggestCode()" maxlength="150" /></div>
          <div>
            <label for="code">Academy code *</label>
            <input id="code" [(ngModel)]="model.code" (ngModelChange)="codeEdited = true" maxlength="40" autocapitalize="none" />
          </div>
        </div>
        <div class="helper">Staff type this code when they sign in. Lowercase letters, digits and hyphens; it cannot be changed later.</div>
        <div class="two-fields">
          <div>
            <label for="plan">Plan *</label>
            <select id="plan" [(ngModel)]="model.planId">
              <option value="" disabled>Select plan</option>
              @for (p of plans(); track p.id) {
                <option [value]="p.id">{{ p.name }} — {{ p.price | money }} / {{ p.interval.toLowerCase() }}</option>
              }
            </select>
          </div>
          <div><label for="trial">Free trial (days)</label><input id="trial" type="number" min="0" max="365" [(ngModel)]="model.trialDays" /></div>
        </div>
        <div class="helper">The first invoice is raised a few days before the trial ends. Use 0 to bill straight away.</div>
        <label for="phone">Contact phone</label>
        <input id="phone" [(ngModel)]="model.contactPhone" maxlength="32" />

        <h3 class="section-title">Super Admin login</h3>
        <div class="two-fields">
          <div><label for="adminName">Full name *</label><input id="adminName" [(ngModel)]="model.superAdmin.name" maxlength="140" /></div>
          <div><label for="adminEmail">Email *</label><input id="adminEmail" type="email" [(ngModel)]="model.superAdmin.email" /></div>
        </div>
        <label for="adminPassword">Initial password * (at least 5 characters)</label>
        <input id="adminPassword" type="password" autocomplete="new-password" [(ngModel)]="model.superAdmin.password" />
        <div class="helper">Share the code, email and password privately. They must choose their own password at first sign-in, and can then create their own staff logins.</div>
      </app-modal>
    }
  `,
})
export class AcademiesComponent {
  private readonly api = inject(Api);
  private readonly notify = inject(Notify);
  private readonly router = inject(Router);

  /** Set by the overview's "New academy" shortcut (?new=1). */
  readonly openNew = input<string>(undefined, { alias: 'new' });

  protected readonly search = signal('');
  protected readonly status = signal('All');
  protected readonly plans = signal<Plan[]>([]);
  protected readonly error = signal('');
  protected readonly form = new FormState();
  protected model = this.blank();
  protected codeEdited = false;
  private searchTimer?: ReturnType<typeof setTimeout>;

  protected readonly list = new PagedList<TenantRow>((page, pageSize) =>
    this.api.get<Paged<TenantRow>>('platform/tenants', { page, pageSize, search: this.search(), status: this.status() }),
  );

  constructor() {
    this.list.load();
    this.api.get<Plan[]>('platform/plans').subscribe({ next: plans => this.plans.set(plans.filter(p => p.isActive)), error: e => this.error.set(errorMessage(e)) });
    effect(() => {
      if (this.openNew()) this.open();
    });
  }

  protected onSearch(text: string): void {
    this.search.set(text);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.list.reload(), 300);
  }

  private blank() {
    return { name: '', code: '', planId: '', trialDays: 14, contactPhone: '', superAdmin: { name: '', email: '', password: '' } };
  }

  protected open(): void {
    this.model = this.blank();
    this.codeEdited = false;
    this.form.show();
  }

  /** Proposes a code from the name until the owner types their own. */
  protected suggestCode(): void {
    if (this.codeEdited) return;
    this.model.code = this.model.name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 40);
  }

  protected save(): void {
    const m = this.model;
    if (!m.name.trim()) return this.form.fail('Enter the academy name.');
    if (!/^[a-z0-9][a-z0-9-]{1,38}[a-z0-9]$/.test(m.code.trim().toLowerCase())) return this.form.fail('Code must be 3-40 characters: lowercase letters, digits and hyphens.');
    if (!m.planId) return this.form.fail('Select a plan.');
    if (!m.superAdmin.name.trim() || !m.superAdmin.email.trim()) return this.form.fail("Enter the Super Admin's name and email.");
    if (m.superAdmin.password.length < 5) return this.form.fail('Password must contain at least 5 characters.');

    this.form.submit(this.api.post<{ id: string }>('platform/tenants', m), created => {
      this.notify.success(`${m.name} created`);
      void this.router.navigate(['/platform/academies', created.id]);
    });
  }
}
