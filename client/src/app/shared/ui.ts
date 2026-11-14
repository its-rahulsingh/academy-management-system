import { Component, Pipe, PipeTransform, computed, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

const INR = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 0 });
const INR_EXACT = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', minimumFractionDigits: 2 });

/** ₹ amount. Whole rupees by default; pass `true` where paise matter (receipts, invoices). */
@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  transform(value: number | null | undefined, exact = false): string {
    return (exact ? INR_EXACT : INR).format(Number(value) || 0);
  }
}

/** Date as "05 Aug 2026". Accepts plain dates and timestamps; plain dates are not shifted by time zone. */
@Pipe({ name: 'day' })
export class DayPipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    if (!value) return '—';
    const isPlainDate = value.length === 10;
    return new Date(isPlainDate ? `${value}T00:00:00Z` : value).toLocaleDateString('en-IN', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      timeZone: isPlainDate ? 'UTC' : undefined,
    });
  }
}

/** Today's date in the browser's time zone as yyyy-MM-dd, the format date inputs and the API use. */
export function today(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
}

export function initials(name: string | null | undefined): string {
  return (name || 'A')
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map(part => part[0]?.toUpperCase() ?? '')
    .join('');
}

/** Dialog frame: title, scrolling body, error line, Cancel and Save buttons. */
@Component({
  selector: 'app-modal',
  template: `
    <div class="modal-backdrop" (click)="closed.emit()">
      <section class="modal" [class.modal-wide]="wide()" role="dialog" aria-modal="true" [attr.aria-label]="title()" (click)="$event.stopPropagation()">
        <header class="modal-header">
          <h2>{{ title() }}</h2>
          <button type="button" (click)="closed.emit()" aria-label="Close">✕</button>
        </header>
        <div class="modal-body">
          @if (error()) {
            <div class="alert" role="alert">{{ error() }}</div>
          }
          <ng-content />
        </div>
        <footer class="modal-footer">
          <button type="button" class="secondary" (click)="closed.emit()">{{ saveLabel() ? 'Cancel' : 'Close' }}</button>
          @if (saveLabel()) {
            <button type="button" class="primary" [class.danger]="danger()" [disabled]="busy()" (click)="saved.emit()">
              {{ busy() ? 'Saving…' : saveLabel() }}
            </button>
          }
        </footer>
      </section>
    </div>
  `,
})
export class ModalComponent {
  readonly title = input.required<string>();
  readonly error = input('');
  readonly busy = input(false);
  /** Empty string hides the save button, leaving a read-only dialog. */
  readonly saveLabel = input('Save');
  readonly danger = input(false);
  readonly wide = input(false);
  readonly closed = output<void>();
  readonly saved = output<void>();
}

/** Asks for the mandatory reason before a correction (void, suspend) is applied. */
@Component({
  selector: 'app-reason-dialog',
  imports: [ModalComponent, FormsModule],
  template: `
    <app-modal [title]="title()" [saveLabel]="confirmLabel()" [danger]="true" [busy]="busy()" [error]="error()" (closed)="cancelled.emit()" (saved)="submit()">
      <p class="helper">{{ description() }}</p>
      <label for="reason">Reason *</label>
      <textarea id="reason" rows="3" maxlength="500" [ngModel]="reason()" (ngModelChange)="reason.set($event)" placeholder="Why is this being done?"></textarea>
    </app-modal>
  `,
})
export class ReasonDialogComponent {
  readonly title = input.required<string>();
  readonly description = input('');
  readonly confirmLabel = input('Confirm');
  readonly busy = input(false);
  readonly error = input('');
  readonly confirmed = output<string>();
  readonly cancelled = output<void>();
  protected readonly reason = signal('');

  protected submit(): void {
    if (this.reason().trim().length >= 3) this.confirmed.emit(this.reason().trim());
  }
}

/** Previous / next controls for a server-paged table. */
@Component({
  selector: 'app-pager',
  template: `
    @if (total() > pageSize()) {
      <nav class="pager" aria-label="Pages">
        <span>{{ first() }}–{{ last() }} of {{ total() }}</span>
        <button type="button" class="secondary" [disabled]="page() <= 1" (click)="pageChange.emit(page() - 1)">← Previous</button>
        <button type="button" class="secondary" [disabled]="last() >= total()" (click)="pageChange.emit(page() + 1)">Next →</button>
      </nav>
    }
  `,
})
export class PagerComponent {
  readonly page = input.required<number>();
  readonly pageSize = input(25);
  readonly total = input.required<number>();
  readonly pageChange = output<number>();
  protected readonly first = computed(() => (this.page() - 1) * this.pageSize() + 1);
  protected readonly last = computed(() => Math.min(this.total(), this.page() * this.pageSize()));
}

/** Coloured label for a status word. */
@Component({
  selector: 'app-status',
  template: `<span class="pill" [class.paid]="tone() === 'good'" [class.partial]="tone() === 'info'" [class.bad]="tone() === 'bad'">{{ value() }}</span>`,
})
export class StatusComponent {
  readonly value = input.required<string>();
  protected readonly tone = computed(() => {
    switch (this.value()) {
      case 'Paid':
      case 'Active':
      case 'Posted':
      case 'Sent':
        return 'good';
      case 'Partial':
      case 'Trialing':
      case 'Open':
        return 'info';
      case 'Voided':
      case 'Void':
      case 'Suspended':
      case 'Cancelled':
      case 'Expired':
      case 'Inactive':
      case 'Disabled':
        return 'bad';
      default:
        return 'warn';
    }
  });
}

/** Everything a typical screen's template needs, so components import one list. */
export const UI = [FormsModule, MoneyPipe, DayPipe, ModalComponent, ReasonDialogComponent, PagerComponent, StatusComponent] as const;
