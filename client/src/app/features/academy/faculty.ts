import { Component, computed, inject, signal } from '@angular/core';
import { Api, FormState, Notify, PagedList, errorMessage } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { Faculty, PAYMENT_METHODS, Paged, Payout, Permission } from '../../core/models';
import { UI, today } from '../../shared/ui';

@Component({
  selector: 'app-faculty',
  imports: [UI],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">ACADEMY WORKSPACE</div>
        <h1>Faculty</h1>
        <p>Agreed payouts, what has been paid and what is owed.</p>
      </div>
      <div class="page-actions">
        @if (can(permission.PayoutsWrite)) {
          <button type="button" class="secondary" (click)="openPayout()">+ Record payout</button>
        }
        @if (can(permission.FacultyWrite)) {
          <button type="button" class="primary" (click)="openForm()">+ Add faculty</button>
        }
      </div>
    </div>

    @if (error()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }

    <div class="stats four">
      <article class="stat white"><div class="stat-top">FACULTY</div><div class="stat-value">{{ faculty().length }}</div></article>
      <article class="stat white"><div class="stat-top">AGREED</div><div class="stat-value">{{ agreed() | money }}</div></article>
      <article class="stat green"><div class="stat-top">PAID</div><div class="stat-value">{{ paid() | money }}</div></article>
      <article class="stat pink"><div class="stat-top">OWED</div><div class="stat-value">{{ agreed() - paid() | money }}</div></article>
    </div>

    <div class="surface table-scroll">
      <table>
        <thead><tr><th>FACULTY</th><th>SPECIALISATION</th><th>AGREED</th><th>PAID</th><th>OWED</th><th>STATUS</th><th></th></tr></thead>
        <tbody>
          @for (f of faculty(); track f.id) {
            <tr [class.dimmed]="!f.active">
              <td><strong>{{ f.fullName }}</strong><small>{{ f.phone || f.email || '—' }}</small></td>
              <td>{{ f.specialization || '—' }}</td>
              <td>{{ f.agreedPayout | money }}</td>
              <td class="positive">{{ f.paid | money }}</td>
              <td class="negative">{{ f.agreedPayout - f.paid | money }}</td>
              <td><app-status [value]="f.active ? 'Active' : 'Inactive'" /></td>
              <td class="row-actions">
                @if (can(permission.PayoutsWrite) && f.agreedPayout - f.paid > 0) {
                  <button type="button" class="table-action" (click)="openPayout(f)">Pay</button>
                }
                @if (can(permission.FacultyWrite)) {
                  <button type="button" class="table-action" (click)="openForm(f)">Edit →</button>
                }
              </td>
            </tr>
          } @empty {
            <tr><td colspan="7" class="empty">No faculty added yet.</td></tr>
          }
        </tbody>
      </table>
    </div>

    @if (can(permission.PayoutsRead)) {
      <section class="surface gap-top">
        <div class="surface-title"><h2>Payout history</h2></div>
        <div class="table-scroll">
          <table>
            <thead><tr><th>FACULTY</th><th>DATE</th><th>METHOD</th><th>AMOUNT</th><th>STATUS</th><th></th></tr></thead>
            <tbody>
              @for (p of payouts.items(); track p.id) {
                <tr [class.dimmed]="p.status === 'Voided'">
                  <td><strong>{{ p.facultyName }}</strong><small>{{ p.status === 'Voided' ? 'Voided: ' + p.voidReason : p.notes }}</small></td>
                  <td>{{ p.paidOn | day }}</td>
                  <td>{{ p.method }}</td>
                  <td>{{ p.amount | money: true }}</td>
                  <td><app-status [value]="p.status" /></td>
                  <td class="row-actions">
                    @if (p.status === 'Posted' && can(permission.LedgerCorrect)) {
                      <button type="button" class="table-action danger-text" (click)="voidTarget.set(p)">Void</button>
                    }
                  </td>
                </tr>
              } @empty {
                <tr><td colspan="6" class="empty">No payouts recorded yet.</td></tr>
              }
            </tbody>
          </table>
        </div>
        <app-pager [page]="payouts.page()" [total]="payouts.total()" (pageChange)="payouts.load($event)" />
      </section>
    }

    @if (form.open()) {
      <app-modal [title]="model.id ? 'Edit faculty member' : 'Add faculty member'" [busy]="form.busy()" [error]="form.error()" (closed)="form.close()" (saved)="save()">
        <label for="name">Full name *</label>
        <input id="name" [(ngModel)]="model.fullName" maxlength="150" />
        <div class="two-fields">
          <div><label for="email">Email</label><input id="email" type="email" [(ngModel)]="model.email" /></div>
          <div><label for="phone">Phone</label><input id="phone" [(ngModel)]="model.phone" maxlength="32" /></div>
        </div>
        <div class="two-fields">
          <div><label for="agreed">Agreed payout (₹) *</label><input id="agreed" type="number" min="0" step="0.01" [(ngModel)]="model.agreedPayout" /></div>
          <div><label for="spec">Specialisation</label><input id="spec" [(ngModel)]="model.specialization" placeholder="e.g. Mathematics" /></div>
        </div>
        @if (model.id) {
          <label class="check"><input type="checkbox" [(ngModel)]="model.active" /> Currently teaching</label>
        }
      </app-modal>
    }

    @if (payout.open()) {
      <app-modal title="Record faculty payout" saveLabel="Record payout" [busy]="payout.busy()" [error]="payout.error()" (closed)="payout.close()" (saved)="savePayout()">
        <label for="who">Faculty member *</label>
        <select id="who" [(ngModel)]="payoutModel.facultyId">
          <option value="" disabled>Select faculty</option>
          @for (f of faculty(); track f.id) {
            <option [value]="f.id">{{ f.fullName }} — owed {{ f.agreedPayout - f.paid | money }}</option>
          }
        </select>
        <div class="two-fields">
          <div><label for="amount">Amount (₹) *</label><input id="amount" type="number" min="0.01" step="0.01" [(ngModel)]="payoutModel.amount" /></div>
          <div>
            <label for="method">Method *</label>
            <select id="method" [(ngModel)]="payoutModel.method">
              @for (m of methods; track m) {
                <option>{{ m }}</option>
              }
            </select>
          </div>
        </div>
        <label for="date">Date</label>
        <input id="date" type="date" [(ngModel)]="payoutModel.paidOn" />
        <label for="notes">Notes</label>
        <textarea id="notes" rows="2" maxlength="1200" [(ngModel)]="payoutModel.notes"></textarea>
      </app-modal>
    }

    @if (voidTarget(); as target) {
      <app-reason-dialog
        title="Void payout"
        [description]="'Payout to ' + target.facultyName + ' stays on record, marked as voided.'"
        confirmLabel="Void payout"
        [busy]="voiding.busy()"
        [error]="voiding.error()"
        (cancelled)="voidTarget.set(null)"
        (confirmed)="confirmVoid(target, $event)"
      />
    }
  `,
})
export class FacultyComponent {
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(Notify);

  protected readonly permission = Permission;
  protected readonly methods = PAYMENT_METHODS;
  protected readonly faculty = signal<Faculty[]>([]);
  protected readonly error = signal('');
  protected readonly agreed = computed(() => this.faculty().reduce((sum, f) => sum + f.agreedPayout, 0));
  protected readonly paid = computed(() => this.faculty().reduce((sum, f) => sum + f.paid, 0));
  protected readonly payouts = new PagedList<Payout>((page, pageSize) => this.api.get<Paged<Payout>>('payouts', { page, pageSize }));

  protected readonly form = new FormState();
  protected model = this.blank();
  protected readonly payout = new FormState();
  protected payoutModel = { facultyId: '', amount: 0, method: 'Bank Transfer', paidOn: today(), notes: '' };
  protected readonly voidTarget = signal<Payout | null>(null);
  protected readonly voiding = new FormState();

  constructor() {
    this.load();
  }

  protected can(permission: string): boolean {
    return this.auth.can(permission);
  }

  private load(): void {
    this.api.get<Faculty[]>('faculty').subscribe({ next: f => this.faculty.set(f), error: e => this.error.set(errorMessage(e)) });
    if (this.can(Permission.PayoutsRead)) this.payouts.load();
  }

  private blank() {
    return { id: '', fullName: '', email: '', phone: '', specialization: '', agreedPayout: 0, active: true };
  }

  protected openForm(f?: Faculty): void {
    this.model = f
      ? { id: f.id, fullName: f.fullName, email: f.email, phone: f.phone, specialization: f.specialization, agreedPayout: f.agreedPayout, active: f.active }
      : this.blank();
    this.form.show();
  }

  protected save(): void {
    const { id, ...body } = this.model;
    if (!body.fullName.trim()) return this.form.fail('Enter a name.');
    this.form.submit(id ? this.api.put(`faculty/${id}`, body) : this.api.post('faculty', body), () => {
      this.notify.success('Faculty member saved');
      this.load();
    });
  }

  protected openPayout(f?: Faculty): void {
    this.payoutModel = { facultyId: f?.id ?? '', amount: f ? f.agreedPayout - f.paid : 0, method: 'Bank Transfer', paidOn: today(), notes: '' };
    this.payout.show();
  }

  protected savePayout(): void {
    if (!this.payoutModel.facultyId) return this.payout.fail('Select a faculty member.');
    if (!(Number(this.payoutModel.amount) > 0)) return this.payout.fail('Enter an amount greater than zero.');
    this.payout.submit(this.api.post('payouts', this.payoutModel), () => {
      this.notify.success('Payout recorded');
      this.load();
    });
  }

  protected confirmVoid(target: Payout, reason: string): void {
    this.voiding.submit(this.api.post(`payouts/${target.id}/void`, { reason }), () => {
      this.voidTarget.set(null);
      this.notify.success('Payout voided');
      this.load();
    });
  }
}
