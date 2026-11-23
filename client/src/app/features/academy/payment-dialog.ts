import { Component, computed, inject, input, output, signal } from '@angular/core';
import { Api, FormState } from '../../core/api';
import { PAYMENT_METHODS, StudentLookup } from '../../core/models';
import { UI, today } from '../../shared/ui';

export interface PaymentSaved {
  id: string;
  receipt: string;
  /** False when the student has no email address, so no receipt email went out. */
  emailed: boolean;
}

/** The confirmation shown after recording a payment or refund. */
export function savedMessage(saved: PaymentSaved, refund: boolean): string {
  return `${refund ? 'Refund' : 'Payment'} recorded — receipt ${saved.receipt}. `
    + (saved.emailed
      ? 'Receipt emailed to the student.'
      : 'Receipt not emailed (no email on record, or this month’s emailed-receipt allowance is used up) — print it from the Payments page if needed.');
}

/**
 * Records a fee payment or a refund. Opened either for a known student (from the student's row)
 * or with a search box (from the Payments screen), so the whole student list is never downloaded.
 */
@Component({
  selector: 'app-payment-dialog',
  imports: [UI],
  template: `
    <app-modal
      [title]="refund() ? 'Refund to student' : 'Record payment'"
      [saveLabel]="refund() ? 'Record refund' : 'Record payment'"
      [danger]="refund()"
      [busy]="state.busy()"
      [error]="state.error()"
      (closed)="closed.emit()"
      (saved)="save()"
    >
      @if (student()) {
        <div class="summary-line">
          <strong>{{ selected()?.fullName }}</strong>
          <span>{{ selected()?.courseName }}</span>
        </div>
      } @else {
        <label for="search">Find student *</label>
        <input id="search" [ngModel]="search()" (ngModelChange)="find($event)" placeholder="Type a name, phone or email" autocomplete="off" />
        @if (matches().length) {
          <ul class="pick-list">
            @for (match of matches(); track match.id) {
              <li>
                <button type="button" [class.active]="match.id === selected()?.id" (click)="picked.set(match)">
                  <strong>{{ match.fullName }}</strong>
                  <small>{{ match.courseName }} · {{ match.academicYearName }} · due {{ match.pending | money }}</small>
                </button>
              </li>
            }
          </ul>
        } @else if (search().length >= 2) {
          <div class="helper">No matching students{{ refund() ? '' : ' with fees outstanding' }}.</div>
        }
      }

      @if (selected(); as s) {
        <div class="helper">
          {{ refund() ? 'Paid so far' : 'Outstanding' }}: <strong>{{ limit() | money: true }}</strong>
        </div>
        <div class="two-fields">
          <div>
            <label for="amount">Amount (₹) *</label>
            <input id="amount" type="number" min="0.01" step="0.01" [max]="limit()" [(ngModel)]="form.amount" />
          </div>
          <div>
            <label for="method">Method *</label>
            <select id="method" [(ngModel)]="form.method">
              @for (method of methods; track method) {
                <option>{{ method }}</option>
              }
            </select>
          </div>
        </div>
        <label for="date">Date</label>
        <input id="date" type="date" [max]="maxDate" [(ngModel)]="form.paidOn" />
        <label for="notes">{{ refund() ? 'Reason for refund' : 'Notes' }}</label>
        <textarea id="notes" rows="2" maxlength="1200" [(ngModel)]="form.notes"></textarea>
      }
    </app-modal>
  `,
})
export class PaymentDialogComponent {
  private readonly api = inject(Api);

  /** Preselected student; omit to show the search box. */
  readonly student = input<StudentLookup | null>(null);
  readonly refund = input(false);
  readonly closed = output<void>();
  readonly saved = output<PaymentSaved>();

  protected readonly state = new FormState();
  protected readonly methods = PAYMENT_METHODS;
  protected readonly maxDate = today();
  protected readonly search = signal('');
  protected readonly matches = signal<StudentLookup[]>([]);
  protected readonly picked = signal<StudentLookup | null>(null);
  protected readonly selected = computed(() => this.student() ?? this.picked());
  protected readonly limit = computed(() => (this.refund() ? this.selected()?.paid : this.selected()?.pending) ?? 0);
  protected form = { amount: 0, method: 'Cash', paidOn: today(), notes: '' };
  private searchTimer?: ReturnType<typeof setTimeout>;

  protected find(text: string): void {
    this.search.set(text);
    this.picked.set(null);
    clearTimeout(this.searchTimer);
    if (text.trim().length < 2) return this.matches.set([]);
    // Wait for a pause in typing so each keystroke does not hit the server.
    this.searchTimer = setTimeout(() => {
      this.api
        .get<StudentLookup[]>('students/lookup', { search: text, dueOnly: this.refund() ? null : true })
        .subscribe(matches => this.matches.set(matches));
    }, 250);
  }

  protected save(): void {
    const student = this.selected();
    if (!student) return this.state.fail('Choose a student first.');
    const amount = Number(this.form.amount);
    if (!(amount > 0)) return this.state.fail('Enter an amount greater than zero.');
    if (amount > this.limit()) return this.state.fail(this.refund() ? 'A refund cannot exceed what has been paid.' : 'Amount exceeds the outstanding fee.');

    const body = { studentId: student.id, amount, method: this.form.method, paidOn: this.form.paidOn, notes: this.form.notes };
    this.state.submit(this.api.post<PaymentSaved>(this.refund() ? 'payments/refunds' : 'payments', body), saved => this.saved.emit(saved));
  }
}
