import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Api, PagedList } from '../../core/api';
import { AuditEntry, Paged } from '../../core/models';
import { ChangeLine, actionLabel, changeLines, entityLabel } from '../../shared/audit-format';
import { RecordHistoryComponent } from '../../shared/record-history';
import { UI } from '../../shared/ui';
import { ActivityDashboardComponent } from './activity-dashboard';

/**
 * Who did what. For an academy: a dashboard (numbers, flags, charts) and the full log; clicking a record in the
 * log opens its history. The owner sees only the platform's own log.
 */
@Component({
  selector: 'app-audit',
  imports: [UI, ActivityDashboardComponent, RecordHistoryComponent],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">{{ platform ? 'PLATFORM' : 'ACADEMY WORKSPACE' }}</div>
        <h1>Activity log</h1>
        <p>{{ platform ? 'Actions taken on the platform itself: academies, plans and owner accounts.' : 'What happened, who did it, and what needs a second look.' }}</p>
      </div>
    </div>

    @if (!platform) {
      <div class="tabs" role="tablist">
        <button type="button" role="tab" [class.active]="tab() === 'dashboard'" [attr.aria-selected]="tab() === 'dashboard'" (click)="tab.set('dashboard')">Dashboard</button>
        <button type="button" role="tab" [class.active]="tab() === 'log'" [attr.aria-selected]="tab() === 'log'" (click)="showLog()">Full log</button>
      </div>
    }

    @if (!platform && tab() === 'dashboard') {
      <app-activity-dashboard />
    } @else {
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
                <td><app-status [value]="action(entry.action)" /></td>
                <td>
                  @if (!platform && entry.entityId) {
                    <button type="button" class="text-button" (click)="history.set(entry)" title="See this record's history">{{ label(entry.entityType) }} ↗</button>
                  } @else {
                    {{ label(entry.entityType) }}
                  }
                </td>
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
    }

    @if (history(); as h) {
      <app-record-history [entityType]="h.entityType" [entityId]="h.entityId" (closed)="history.set(null)" />
    }
  `,
})
export class AuditComponent {
  private readonly api = inject(Api);
  protected readonly platform = inject(ActivatedRoute).snapshot.data['platform'] === true;
  protected readonly entityTypes = ['Student', 'StudentPayment', 'FeeAdjustment', 'Expense', 'ExpenseProof', 'Faculty', 'FacultyPayout', 'FacultySession', 'Course', 'AcademyTest', 'TestResult', 'AppUser', 'SubscriptionInvoice'];
  protected readonly tab = signal<'dashboard' | 'log'>('dashboard');
  protected readonly search = signal('');
  protected readonly entityType = signal('');
  protected readonly history = signal<AuditEntry | null>(null);
  private searchTimer?: ReturnType<typeof setTimeout>;
  private logLoaded = false;

  protected readonly list = new PagedList<AuditEntry>((page, pageSize) =>
    this.api.get<Paged<AuditEntry>>(this.platform ? 'platform/audit' : 'audit', { page, pageSize, search: this.search(), entityType: this.entityType() }),
  );

  constructor() {
    if (this.platform) this.showLog();
  }

  protected showLog(): void {
    this.tab.set('log');
    if (!this.logLoaded) {
      this.logLoaded = true;
      this.list.load();
    }
  }

  protected onSearch(text: string): void {
    this.search.set(text);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.list.reload(), 300);
  }

  protected label = entityLabel;
  protected action = actionLabel;

  protected time(value: string): string {
    return new Date(value).toLocaleString('en-IN', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' });
  }

  protected changes(entry: AuditEntry): ChangeLine[] {
    return changeLines(entry.changes);
  }
}
