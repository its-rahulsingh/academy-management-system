import { Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, FormState, Notify, errorMessage } from '../../core/api';
import { AppUser, Invoice, PAYMENT_METHODS, Plan, SubscriptionStatus, TenantDetail } from '../../core/models';
import { UI } from '../../shared/ui';

type Dialog = 'details' | 'subscription' | 'superAdmin' | 'password' | 'markPaid' | null;

@Component({
  selector: 'app-academy-detail',
  imports: [RouterLink, UI],
  template: `
    @if (error()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }

    @if (tenant(); as t) {
      <div class="page-header">
        <div>
          <a class="text-button" routerLink="/platform/academies">← Academies</a>
          <h1>{{ t.name }}</h1>
          <p>Sign-in code <strong>{{ t.code }}</strong> · created {{ t.createdAtUtc | day }}</p>
        </div>
        <div class="page-actions">
          <button type="button" class="secondary" (click)="openDetails(t)">Edit details</button>
          @if (t.status === 'Active') {
            <button type="button" class="secondary danger-text" (click)="suspending.set(true)">Suspend</button>
          } @else {
            <button type="button" class="primary" (click)="activate(t)">Reactivate</button>
          }
        </div>
      </div>

      @if (t.status === 'Suspended') {
        <div class="alert">Suspended {{ t.suspendedAtUtc | day }} — {{ t.suspensionReason }}. Nobody at this academy can use the product.</div>
      }

      <div class="stats four">
        <article class="stat purple"><div class="stat-top">PLAN</div><div class="stat-value small-value">{{ t.subscription?.planName ?? 'None' }}</div><small>{{ t.subscription ? (t.subscription.price | money) + ' / ' + t.subscription.interval.toLowerCase() : 'Assign a plan to give access' }}</small></article>
        <article class="stat white"><div class="stat-top">SUBSCRIPTION</div><div class="stat-value small-value">{{ t.subscription?.status ?? '—' }}</div><small>through {{ t.subscription?.currentPeriodEnd | day }}</small></article>
        <article class="stat white"><div class="stat-top">STUDENTS</div><div class="stat-value">{{ t.usage.students }}</div><small>{{ t.usage.courses }} courses</small></article>
        <article class="stat white"><div class="stat-top">ACTIVE USERS</div><div class="stat-value">{{ t.usage.users }}</div></article>
      </div>

      <section class="surface">
        <div class="surface-title">
          <h2>Subscription</h2>
          <div class="button-row tight">
            <button type="button" class="secondary" (click)="openSubscription(t)">{{ t.subscription ? 'Change plan or dates' : 'Assign plan' }}</button>
            @if (t.subscription) {
              <button type="button" class="secondary" (click)="raiseInvoice(t)">Raise next invoice</button>
            }
          </div>
        </div>
        <div class="table-scroll">
          <table>
            <thead><tr><th>INVOICE</th><th>PERIOD</th><th>DUE</th><th>AMOUNT</th><th>STATUS</th><th></th></tr></thead>
            <tbody>
              @for (invoice of t.invoices; track invoice.id) {
                <tr>
                  <td><strong>{{ invoice.invoiceNumber }}</strong><small>{{ invoice.notes }}</small></td>
                  <td>{{ invoice.periodStart | day }} – {{ invoice.periodEnd | day }}</td>
                  <td>{{ invoice.dueOn | day }}</td>
                  <td>{{ invoice.amount | money: true }}</td>
                  <td><app-status [value]="invoice.status" /></td>
                  <td class="row-actions">
                    @if (invoice.status === 'Open') {
                      <button type="button" class="table-action" (click)="openMarkPaid(invoice)">Mark paid</button>
                      <button type="button" class="table-action danger-text" (click)="voidingInvoice.set(invoice)">Void</button>
                    } @else if (invoice.status === 'Paid') {
                      <small>{{ invoice.paymentMethod }} · {{ invoice.paidAtUtc | day }}</small>
                    }
                  </td>
                </tr>
              } @empty {
                <tr><td colspan="6" class="empty">No invoices yet.</td></tr>
              }
            </tbody>
          </table>
        </div>
      </section>

      <section class="surface">
        <div class="surface-title">
          <h2>Logins</h2>
          <button type="button" class="secondary" (click)="openSuperAdmin()">+ Add Super Admin</button>
        </div>
        <p class="muted">The academy manages its own Admin, Accountant and Staff logins. You can add a Super Admin or reset a password if they are locked out.</p>
        <div class="table-scroll">
          <table>
            <thead><tr><th>USER</th><th>ROLE</th><th>LAST SIGN-IN</th><th>STATUS</th><th></th></tr></thead>
            <tbody>
              @for (u of t.users; track u.id) {
                <tr [class.dimmed]="!u.active">
                  <td><strong>{{ u.displayName }}</strong><small>{{ u.email }}</small></td>
                  <td>{{ u.role }}</td>
                  <td>{{ u.lastLoginAtUtc | day }}</td>
                  <td><app-status [value]="u.active ? 'Active' : 'Disabled'" /></td>
                  <td class="row-actions"><button type="button" class="table-action" (click)="openPassword(u)">Reset password</button></td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>

      @switch (dialog()) {
        @case ('details') {
          <app-modal title="Edit academy details" [busy]="form.busy()" [error]="form.error()" (closed)="close()" (saved)="saveDetails(t)">
            <label for="name">Academy name *</label>
            <input id="name" [(ngModel)]="details.name" maxlength="150" />
            <label for="contactName">Contact person</label>
            <input id="contactName" [(ngModel)]="details.contactName" maxlength="150" />
            <div class="two-fields">
              <div><label for="contactEmail">Contact email</label><input id="contactEmail" type="email" [(ngModel)]="details.contactEmail" /></div>
              <div><label for="contactPhone">Contact phone</label><input id="contactPhone" [(ngModel)]="details.contactPhone" maxlength="32" /></div>
            </div>
          </app-modal>
        }
        @case ('subscription') {
          <app-modal title="Subscription" [busy]="form.busy()" [error]="form.error()" (closed)="close()" (saved)="saveSubscription(t)">
            <label for="plan">Plan *</label>
            <select id="plan" [(ngModel)]="subscription.planId" (ngModelChange)="planChanged()">
              <option value="" disabled>Select plan</option>
              @for (p of plans(); track p.id) {
                <option [value]="p.id">{{ p.name }} — {{ p.price | money }} / {{ p.interval.toLowerCase() }}</option>
              }
            </select>
            <div class="two-fields">
              <div><label for="price">Price charged (₹)</label><input id="price" type="number" min="0" step="0.01" [(ngModel)]="subscription.price" /></div>
              <div>
                <label for="status">Status</label>
                <select id="status" [(ngModel)]="subscription.status">
                  @for (s of statuses; track s) {
                    <option>{{ s }}</option>
                  }
                </select>
              </div>
            </div>
            <label for="end">Access paid through</label>
            <input id="end" type="date" [(ngModel)]="subscription.currentPeriodEnd" />
            <div class="helper">Extend this date to give extra time without an invoice. Set the price to 0 for a free academy: it is never invoiced.</div>
          </app-modal>
        }
        @case ('superAdmin') {
          <app-modal title="Add Super Admin" saveLabel="Create login" [busy]="form.busy()" [error]="form.error()" (closed)="close()" (saved)="saveSuperAdmin(t)">
            <label for="saName">Full name *</label>
            <input id="saName" [(ngModel)]="superAdmin.name" maxlength="140" />
            <label for="saEmail">Email *</label>
            <input id="saEmail" type="email" [(ngModel)]="superAdmin.email" />
            <label for="saPassword">Initial password * (at least 12 characters)</label>
            <input id="saPassword" type="password" autocomplete="new-password" [(ngModel)]="superAdmin.password" />
          </app-modal>
        }
        @case ('password') {
          <app-modal [title]="'Reset password — ' + passwordFor()?.displayName" saveLabel="Set password" [busy]="form.busy()" [error]="form.error()" (closed)="close()" (saved)="savePassword(t)">
            <label for="newPassword">New password * (at least 12 characters)</label>
            <input id="newPassword" type="password" autocomplete="new-password" [(ngModel)]="newPassword" />
            <div class="helper">Re-enables the account if it was disabled. The user must choose their own password at next sign-in.</div>
          </app-modal>
        }
        @case ('markPaid') {
          <app-modal [title]="'Mark ' + payingInvoice()?.invoiceNumber + ' as paid'" saveLabel="Mark paid" [busy]="form.busy()" [error]="form.error()" (closed)="close()" (saved)="saveMarkPaid()">
            <p class="helper">Use this when the academy paid you directly. Access is extended to {{ payingInvoice()?.periodEnd | day }}.</p>
            <label for="method">Received by *</label>
            <select id="method" [(ngModel)]="payment.method">
              @for (m of methods; track m) {
                <option>{{ m }}</option>
              }
            </select>
            <label for="reference">Reference (UTR, cheque number…)</label>
            <input id="reference" [(ngModel)]="payment.reference" maxlength="100" />
          </app-modal>
        }
      }

      @if (suspending()) {
        <app-reason-dialog
          [title]="'Suspend ' + t.name"
          description="Everyone at this academy is locked out immediately. Their data is kept and access returns when you reactivate."
          confirmLabel="Suspend academy"
          [busy]="form.busy()"
          [error]="form.error()"
          (cancelled)="suspending.set(false)"
          (confirmed)="suspend(t, $event)"
        />
      }
      @if (voidingInvoice(); as invoice) {
        <app-reason-dialog
          [title]="'Void ' + invoice.invoiceNumber"
          description="The invoice is cancelled and can no longer be paid. Raise a new one if payment is still due."
          confirmLabel="Void invoice"
          [busy]="form.busy()"
          [error]="form.error()"
          (cancelled)="voidingInvoice.set(null)"
          (confirmed)="voidInvoice(invoice, $event)"
        />
      }
    } @else if (!error()) {
      <div class="empty">Loading…</div>
    }
  `,
})
export class AcademyDetailComponent {
  private readonly api = inject(Api);
  private readonly notify = inject(Notify);

  /** Academy id from the route. */
  readonly id = input.required<string>();

  protected readonly methods = PAYMENT_METHODS;
  protected readonly statuses: SubscriptionStatus[] = ['Trialing', 'Active', 'PastDue', 'Cancelled'];
  protected readonly tenant = signal<TenantDetail | null>(null);
  protected readonly plans = signal<Plan[]>([]);
  protected readonly error = signal('');
  protected readonly dialog = signal<Dialog>(null);
  protected readonly form = new FormState();
  protected readonly suspending = signal(false);
  protected readonly voidingInvoice = signal<Invoice | null>(null);
  protected readonly passwordFor = signal<AppUser | null>(null);
  protected readonly payingInvoice = signal<Invoice | null>(null);

  protected details = { name: '', contactName: '', contactEmail: '', contactPhone: '' };
  protected subscription = { planId: '', price: 0, status: 'Active' as SubscriptionStatus, currentPeriodEnd: '' };
  protected superAdmin = { name: '', email: '', password: '' };
  protected newPassword = '';
  protected payment = { method: 'Bank Transfer', reference: '' };

  constructor() {
    effect(() => this.load(this.id()));
    this.api.get<Plan[]>('platform/plans').subscribe({ next: plans => this.plans.set(plans), error: e => this.error.set(errorMessage(e)) });
  }

  private load(id = this.id()): void {
    this.api.get<TenantDetail>(`platform/tenants/${id}`).subscribe({ next: t => this.tenant.set(t), error: e => this.error.set(errorMessage(e)) });
  }

  private show(dialog: Dialog): void {
    this.form.show();
    this.dialog.set(dialog);
  }

  protected close(): void {
    this.form.close();
    this.dialog.set(null);
  }

  /** Runs a save, then closes the dialog, confirms and reloads. */
  private run(request: ReturnType<Api['post']>, message: string): void {
    this.form.submit(request, () => {
      this.dialog.set(null);
      this.suspending.set(false);
      this.voidingInvoice.set(null);
      this.notify.success(message);
      this.load();
    });
  }

  protected openDetails(t: TenantDetail): void {
    this.details = { name: t.name, contactName: t.contactName, contactEmail: t.contactEmail, contactPhone: t.contactPhone };
    this.show('details');
  }

  protected saveDetails(t: TenantDetail): void {
    if (!this.details.name.trim()) return this.form.fail('Enter the academy name.');
    this.run(this.api.put(`platform/tenants/${t.id}`, this.details), 'Details saved');
  }

  protected openSubscription(t: TenantDetail): void {
    const s = t.subscription;
    this.subscription = s
      ? { planId: s.planId, price: s.price, status: s.status, currentPeriodEnd: s.currentPeriodEnd }
      : { planId: '', price: 0, status: 'Active', currentPeriodEnd: '' };
    this.show('subscription');
  }

  protected planChanged(): void {
    const plan = this.plans().find(p => p.id === this.subscription.planId);
    if (plan) this.subscription.price = plan.price;
  }

  protected saveSubscription(t: TenantDetail): void {
    if (!this.subscription.planId) return this.form.fail('Select a plan.');
    if (!this.subscription.currentPeriodEnd) return this.form.fail('Choose the date access is paid through.');
    this.run(this.api.put(`platform/tenants/${t.id}/subscription`, this.subscription), 'Subscription updated');
  }

  protected raiseInvoice(t: TenantDetail): void {
    this.api.post<{ invoiceNumber: string }>(`platform/tenants/${t.id}/invoices`).subscribe({
      next: invoice => {
        this.error.set('');
        this.notify.success(`Invoice ${invoice.invoiceNumber} raised and emailed`);
        this.load();
      },
      error: e => this.error.set(errorMessage(e)),
    });
  }

  protected openMarkPaid(invoice: Invoice): void {
    this.payingInvoice.set(invoice);
    this.payment = { method: 'Bank Transfer', reference: '' };
    this.show('markPaid');
  }

  protected saveMarkPaid(): void {
    const invoice = this.payingInvoice();
    if (invoice) this.run(this.api.post(`platform/invoices/${invoice.id}/mark-paid`, this.payment), `${invoice.invoiceNumber} marked as paid`);
  }

  protected voidInvoice(invoice: Invoice, reason: string): void {
    this.run(this.api.post(`platform/invoices/${invoice.id}/void`, { reason }), `${invoice.invoiceNumber} voided`);
  }

  protected openSuperAdmin(): void {
    this.superAdmin = { name: '', email: '', password: '' };
    this.show('superAdmin');
  }

  protected saveSuperAdmin(t: TenantDetail): void {
    if (!this.superAdmin.name.trim() || !this.superAdmin.email.trim()) return this.form.fail('Enter a name and email.');
    if (this.superAdmin.password.length < 12) return this.form.fail('Password must contain at least 12 characters.');
    this.run(this.api.post(`platform/tenants/${t.id}/super-admins`, this.superAdmin), 'Super Admin login created');
  }

  protected openPassword(user: AppUser): void {
    this.passwordFor.set(user);
    this.newPassword = '';
    this.show('password');
  }

  protected savePassword(t: TenantDetail): void {
    const user = this.passwordFor();
    if (!user) return;
    if (this.newPassword.length < 12) return this.form.fail('Password must contain at least 12 characters.');
    this.run(this.api.put(`platform/tenants/${t.id}/users/${user.id}/password`, { password: this.newPassword }), 'Password reset');
  }

  protected suspend(t: TenantDetail, reason: string): void {
    this.run(this.api.post(`platform/tenants/${t.id}/suspend`, { reason }), `${t.name} suspended`);
  }

  protected activate(t: TenantDetail): void {
    this.api.post(`platform/tenants/${t.id}/activate`).subscribe({
      next: () => {
        this.notify.success(`${t.name} reactivated`);
        this.load();
      },
      error: e => this.error.set(errorMessage(e)),
    });
  }
}
