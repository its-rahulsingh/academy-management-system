import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Api, PagedList } from '../../core/api';
import { AuditEntry, Paged } from '../../core/models';
import { UI } from '../../shared/ui';

const ENTITY_LABELS: Record<string, string> = {
  StudentPayment: 'Payment',
  FeeAdjustment: 'Fee adjustment',
  FacultyPayout: 'Faculty payout',
  AcademyTest: 'Test',
  TestResult: 'Test result',
  AppUser: 'User',
  SubscriptionInvoice: 'Invoice',
  Tenant: 'Academy',
};

interface Change {
  field: string;
  text: string;
}

/** Who did what, newest first. Shared by academies (their own log) and the owner (platform actions). */
@Component({
  selector: 'app-audit',
  imports: [UI],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">{{ platform ? 'PLATFORM' : 'ACADEMY WORKSPACE' }}</div>
        <h1>Activity log</h1>
        <p>{{ platform ? 'Actions taken on the platform itself: academies, plans and owner accounts.' : 'Every record created, changed or voided, and by whom.' }}</p>
      </div>
    </div>

    @if (list.error()) {
      <div class="alert" role="alert">{{ list.error() }}</div>
    }

    @if (!platform) {
      <div class="filter-bar">
        <input placeholder="⌕  Search by user or action…" [ngModel]="search()" (ngModelChange)="onSearch($event)" />
        <select [ngModel]="entityType()" (ngModelChange)="entityType.set($event); list.reload()" aria-label="Record type">
          <option value="">All records</option>
          @for (type of entityTypes; track type) {
            <option [value]="type">{{ label(type) }}</option>
          }
        </select>
      </div>
    }

    <div class="surface table-scroll">
      <table>
        <thead><tr><th>WHEN</th><th>WHO</th><th>ACTION</th><th>RECORD</th><th>DETAILS</th></tr></thead>
        <tbody>
          @for (entry of list.items(); track entry.id) {
            <tr>
              <td>{{ time(entry.createdAtUtc) }}</td>
              <td><strong>{{ entry.userName }}</strong></td>
              <td><app-status [value]="entry.action" /></td>
              <td>{{ label(entry.entityType) }}</td>
              <td class="audit-changes">
                @for (change of changes(entry); track change.field) {
                  <div><b>{{ change.field }}</b> {{ change.text }}</div>
                }
              </td>
            </tr>
          } @empty {
            <tr><td colspan="5" class="empty">{{ list.loading() ? 'Loading…' : 'Nothing recorded yet.' }}</td></tr>
          }
        </tbody>
      </table>
      <app-pager [page]="list.page()" [total]="list.total()" (pageChange)="list.load($event)" />
    </div>
  `,
})
export class AuditComponent {
  private readonly api = inject(Api);
  protected readonly platform = inject(ActivatedRoute).snapshot.data['platform'] === true;
  protected readonly entityTypes = ['Student', 'StudentPayment', 'FeeAdjustment', 'Expense', 'Faculty', 'FacultyPayout', 'Course', 'AcademyTest', 'TestResult', 'AppUser', 'SubscriptionInvoice'];
  protected readonly search = signal('');
  protected readonly entityType = signal('');
  private searchTimer?: ReturnType<typeof setTimeout>;

  protected readonly list = new PagedList<AuditEntry>((page, pageSize) =>
    this.api.get<Paged<AuditEntry>>(this.platform ? 'platform/audit' : 'audit', { page, pageSize, search: this.search(), entityType: this.entityType() }),
  );

  constructor() {
    this.list.load();
  }

  protected onSearch(text: string): void {
    this.search.set(text);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.list.reload(), 300);
  }

  protected label(entityType: string): string {
    return ENTITY_LABELS[entityType] ?? entityType;
  }

  protected time(value: string): string {
    return new Date(value).toLocaleString('en-IN', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' });
  }

  /** Turns the stored JSON into short "field: old → new" lines, hiding internal ids and empty values. */
  protected changes(entry: AuditEntry): Change[] {
    if (!entry.changes) return [];
    let parsed: Record<string, unknown>;
    try {
      parsed = JSON.parse(entry.changes);
    } catch {
      return [];
    }

    return Object.entries(parsed)
      .filter(([field, value]) => !/Id$|AtUtc$/.test(field) && value !== null && value !== '')
      .slice(0, 6)
      .map(([field, value]) => {
        if (value && typeof value === 'object' && 'new' in value) {
          const change = value as { old: unknown; new: unknown };
          return { field, text: `${change.old ?? '—'} → ${change.new ?? '—'}` };
        }
        return { field, text: String(value) };
      });
  }
}
