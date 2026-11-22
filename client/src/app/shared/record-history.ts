import { Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { Api, errorMessage } from '../core/api';
import { AuditEntry } from '../core/models';
import { ChangeLine, actionLabel, changeLines, entityLabel, logTime } from './audit-format';
import { ModalComponent } from './ui';

interface RecordHistory {
  entityType: string;
  entityId: string;
  title: string | null;
  entries: AuditEntry[];
}

/**
 * "Who changed this and when": the activity log of one record, newest first, with related rows folded in
 * (a student's payments and discounts, an expense's bills, a faculty member's classes and pay).
 */
@Component({
  selector: 'app-record-history',
  imports: [ModalComponent],
  template: `
    <app-modal [title]="'History' + (data()?.title ? ' — ' + data()!.title : '')" saveLabel="" [wide]="true" [error]="error()" (closed)="closed.emit()">
      @if (!data() && !error()) {
        <p class="helper">Loading…</p>
      }
      @if (data(); as d) {
        @if (!d.entries.length) {
          <p class="helper">Nothing recorded for this {{ label(d.entityType).toLowerCase() }} yet.</p>
        }
        <ol class="history">
          @for (e of d.entries; track e.id) {
            <li [class]="'history-item ' + tone(e.action)">
              <div class="history-head">
                <strong>{{ action(e.action) }} · {{ label(e.entityType) }}</strong>
                <small>{{ time(e.createdAtUtc) }} · {{ e.userName || 'System' }}</small>
              </div>
              @for (c of lines(e); track c.field) {
                <div class="history-change"><b>{{ c.field }}</b> {{ c.text }}</div>
              }
            </li>
          }
        </ol>
      }
    </app-modal>
  `,
})
export class RecordHistoryComponent {
  private readonly api = inject(Api);
  readonly entityType = input.required<string>();
  readonly entityId = input.required<string>();
  readonly closed = output<void>();
  protected readonly data = signal<RecordHistory | null>(null);
  protected readonly error = signal('');

  constructor() {
    effect(() => {
      const query = { entityType: this.entityType(), entityId: this.entityId() };
      untracked(() =>
        this.api.get<RecordHistory>('audit/history', query).subscribe({
          next: d => this.data.set(d),
          error: e => this.error.set(errorMessage(e)),
        }),
      );
    });
  }

  protected label = entityLabel;
  protected action = actionLabel;
  protected time = logTime;

  protected lines(entry: AuditEntry): ChangeLine[] {
    return changeLines(entry.changes, 12);
  }

  protected tone(action: string): string {
    return action === 'Created' ? 'created' : action === 'Deleted' || action === 'Voided' ? 'removed' : 'changed';
  }
}
