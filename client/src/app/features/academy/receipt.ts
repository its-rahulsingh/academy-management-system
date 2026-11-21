import { Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, errorMessage } from '../../core/api';
import { Receipt } from '../../core/models';
import { UI } from '../../shared/ui';

/** A receipt laid out for printing. The print stylesheet hides the navigation so only the receipt reaches paper. */
@Component({
  selector: 'app-receipt',
  imports: [RouterLink, UI],
  template: `
    <div class="page-header no-print">
      <div>
        @if (portal()) {
          <a class="text-button" routerLink="/student">← My fees</a>
        } @else {
          <a class="text-button" routerLink="/app/payments">← Payments</a>
        }
        <h1>Receipt</h1>
      </div>
      <button type="button" class="primary" (click)="print()" [disabled]="!receipt()">🖶 Print</button>
    </div>

    @if (error()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }

    @if (receipt(); as r) {
      <article class="receipt surface">
        <header>
          <div>
            <h2>{{ r.academyName }}</h2>
            <p>{{ r.kind === 'Refund' ? 'Refund voucher' : 'Fee receipt' }}</p>
          </div>
          <div class="receipt-number">
            <strong>{{ r.receipt }}</strong>
            <span>{{ r.paidOn | day }}</span>
          </div>
        </header>

        @if (r.status === 'Voided') {
          <div class="alert">VOIDED — {{ r.voidReason }}</div>
        }

        <dl>
          <div><dt>{{ r.kind === 'Refund' ? 'Refunded to' : 'Received from' }}</dt><dd>{{ r.studentName }}</dd></div>
          <div><dt>Course</dt><dd>{{ r.courseName }}</dd></div>
          @if (r.studentPhone) {
            <div><dt>Phone</dt><dd>{{ r.studentPhone }}</dd></div>
          }
          <div><dt>Method</dt><dd>{{ r.method }}</dd></div>
          @if (r.notes) {
            <div><dt>Notes</dt><dd>{{ r.notes }}</dd></div>
          }
        </dl>

        <div class="receipt-amount">
          <span>{{ r.kind === 'Refund' ? 'Amount refunded' : 'Amount received' }}</span>
          <strong>{{ r.amount | money: true }}</strong>
        </div>

        <table class="compact">
          <tbody>
            <tr><td>Total paid to date</td><td>{{ r.totalPaid | money: true }}</td></tr>
            <tr><td><strong>Balance due</strong></td><td><strong>{{ r.balanceDue | money: true }}</strong></td></tr>
          </tbody>
        </table>

        <footer>
          <span>Recorded by {{ r.recordedBy ?? '—' }}</span>
          <span>Authorised signature: ____________________</span>
        </footer>
      </article>
    }
  `,
})
export class ReceiptComponent {
  private readonly api = inject(Api);
  /** Payment id from the route. */
  readonly id = input.required<string>();
  /** True inside the student portal, where a student sees only their own receipts. */
  readonly portal = input(false);
  protected readonly receipt = signal<Receipt | null>(null);
  protected readonly error = signal('');

  constructor() {
    effect(() => {
      const path = this.portal() ? `me/student/receipts/${this.id()}` : `payments/${this.id()}/receipt`;
      this.api.get<Receipt>(path).subscribe({
        next: receipt => this.receipt.set(receipt),
        error: e => this.error.set(errorMessage(e)),
      });
    });
  }

  protected print(): void {
    window.print();
  }
}
