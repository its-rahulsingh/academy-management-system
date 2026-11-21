import { Component, effect, inject, signal, untracked } from '@angular/core';
import { Api, errorMessage } from '../../core/api';
import { PortalPayments, PortalSession } from '../../core/models';
import { UI } from '../../shared/ui';
import { clock } from './teacher-dashboard';
import { portalRefresh } from './teacher-shell';

type Tab = 'unpaid' | 'paid' | 'payouts' | 'rates';

/** What the academy owes the faculty member and what it has paid them. */
@Component({
  selector: 'app-teacher-payments',
  imports: [UI],
  template: `
    @if (error()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }

    @if (data(); as d) {
      @if (d.faculty.payType === 'Package') {
        <div class="teacher-stats">
          <article class="tstat dark">
            <div class="tstat-top">PACKAGE<span>▣</span></div>
            <div class="tstat-value">{{ d.faculty.agreedPayout | money }}</div>
            <small>Agreed with the academy</small>
          </article>
          <article class="tstat sage">
            <div class="tstat-top">RECEIVED<span>✓</span></div>
            <div class="tstat-value">{{ d.received | money }}</div>
            <small>{{ d.payouts.length }} {{ d.payouts.length === 1 ? 'payment' : 'payments' }}</small>
          </article>
          <article class="tstat rose">
            <div class="tstat-top">BALANCE<span>!</span></div>
            <div class="tstat-value">{{ d.unpaid | money }}</div>
            <small>Still to be paid</small>
          </article>
          <article class="tstat sky">
            <div class="tstat-top">LAST PAYMENT<span>₹</span></div>
            @if (d.lastPayout; as last) {
              <div class="tstat-value">{{ last.amount | money }}</div>
              <small>{{ last.paidOn | day }} · {{ last.method }}</small>
            } @else {
              <div class="tstat-value">—</div>
              <small>No payment yet</small>
            }
          </article>
        </div>
        <section class="teacher-panel">
          <h2 class="teacher-h2">Payments received</h2>
          @for (p of d.payouts; track p.id) {
            <article class="session-row">
              <div class="session-date"><b>{{ p.paidOn.slice(8, 10) }}</b><small>{{ shortMonth(p.paidOn) }}</small></div>
              <div class="session-main"><strong>{{ p.method }}</strong><small>{{ p.notes || (p.paidOn | day) }}</small></div>
              <div class="session-pay"><b class="positive">{{ p.amount | money: true }}</b></div>
            </article>
          } @empty {
            <div class="teacher-empty"><div class="art" aria-hidden="true">₹</div><strong>No payments received yet</strong></div>
          }
          <p class="muted gap-top">You are paid a package, so your classes are recorded as attendance and are not priced one by one.</p>
        </section>
      } @else {
      <div class="teacher-stats">
        <article class="tstat rose">
          <div class="tstat-top">UNPAID<span>!</span></div>
          <div class="tstat-value">{{ d.unpaid | money }}</div>
          <small>{{ d.faculty.payType === 'Hourly' ? d.unpaidSessions + ' approved sessions' : 'Package balance' }}</small>
        </article>
        <article class="tstat sage">
          <div class="tstat-top">RECEIVED<span>✓</span></div>
          <div class="tstat-value">{{ d.received | money }}</div>
          <small>{{ d.payouts.length }} {{ d.payouts.length === 1 ? 'payment' : 'payments' }}</small>
        </article>
        <article class="tstat peach">
          <div class="tstat-top">WAITING APPROVAL<span>⧗</span></div>
          <div class="tstat-value">{{ d.waitingAmount | money }}</div>
          <small>{{ d.waitingSessions }} {{ d.waitingSessions === 1 ? 'session' : 'sessions' }}</small>
        </article>
        <article class="tstat dark">
          <div class="tstat-top">LAST PAYMENT<span>▣</span></div>
          @if (d.lastPayout; as last) {
            <div class="tstat-value">{{ last.amount | money }}</div>
            <small>{{ last.paidOn | day }} · {{ last.method }}</small>
          } @else {
            <div class="tstat-value">—</div>
            <small>No payment yet</small>
          }
        </article>
      </div>

      <section class="teacher-panel">
        <div class="segmented wide" role="tablist">
          @for (t of tabs; track t.key) {
            <button type="button" role="tab" [class.active]="tab() === t.key" [attr.aria-selected]="tab() === t.key" (click)="tab.set(t.key)">
              {{ t.label }} <span class="count">{{ count(t.key) }}</span>
            </button>
          }
        </div>

        @switch (tab()) {
          @case ('unpaid') {
            @for (s of d.unpaidItems; track s.id) {
              <article class="session-row">
                <div class="session-date"><b>{{ s.taughtOn.slice(8, 10) }}</b><small>{{ shortMonth(s.taughtOn) }}</small></div>
                <div class="session-main">
                  <strong>{{ timeLabel(s) }}</strong>
                  <small>{{ topicOf(s) || '—' }}</small>
                </div>
                <div class="session-pay"><b>{{ s.amount | money: true }}</b><app-status value="Approved · unpaid" /></div>
              </article>
            } @empty {
              <div class="teacher-empty"><div class="art" aria-hidden="true">✓</div><strong>Nothing unpaid</strong><p>Every approved session has been paid.</p></div>
            }
          }
          @case ('paid') {
            @for (s of d.paidItems; track s.id) {
              <article class="session-row">
                <div class="session-date"><b>{{ s.taughtOn.slice(8, 10) }}</b><small>{{ shortMonth(s.taughtOn) }}</small></div>
                <div class="session-main">
                  <strong>{{ timeLabel(s) }}</strong>
                  <small>{{ topicOf(s) || '—' }}</small>
                </div>
                <div class="session-pay"><b>{{ s.amount | money: true }}</b><app-status value="Paid" /></div>
              </article>
            } @empty {
              <div class="teacher-empty"><div class="art" aria-hidden="true">▣</div><strong>No paid sessions yet</strong></div>
            }
          }
          @case ('payouts') {
            @for (p of d.payouts; track p.id) {
              <article class="session-row">
                <div class="session-date"><b>{{ p.paidOn.slice(8, 10) }}</b><small>{{ shortMonth(p.paidOn) }}</small></div>
                <div class="session-main"><strong>{{ p.method }}</strong><small>{{ p.notes || (p.paidOn | day) }}</small></div>
                <div class="session-pay"><b class="positive">{{ p.amount | money: true }}</b></div>
              </article>
            } @empty {
              <div class="teacher-empty"><div class="art" aria-hidden="true">₹</div><strong>No payments received yet</strong></div>
            }
          }
          @case ('rates') {
            <article class="session-row">
              <div class="session-main">
                <strong>Current: {{ d.faculty.payType === 'Hourly' ? (d.faculty.hourlyRate | money) + ' per hour' : 'Package of ' + (d.faculty.agreedPayout | money) }}</strong>
                <small>Set by the academy. A new rate applies to sessions logged after it changes.</small>
              </div>
            </article>
            @for (r of d.rates; track r.hourlyRate) {
              <article class="session-row">
                <div class="session-main"><strong>{{ r.hourlyRate | money }} / hour</strong><small>{{ r.from | day }} – {{ r.to | day }}</small></div>
                <div class="session-pay"><b>{{ r.sessions }}</b><small>approved sessions</small></div>
              </article>
            }
          }
        }
      </section>
      }
    }
  `,
})
export class TeacherPaymentsComponent {
  private readonly api = inject(Api);
  protected readonly data = signal<PortalPayments | null>(null);
  protected readonly error = signal('');
  protected readonly tab = signal<Tab>('unpaid');
  protected readonly tabs: { key: Tab; label: string }[] = [
    { key: 'unpaid', label: 'Unpaid' },
    { key: 'paid', label: 'Paid sessions' },
    { key: 'payouts', label: 'Payments received' },
    { key: 'rates', label: 'Rates' },
  ];

  constructor() {
    effect(() => {
      portalRefresh();
      untracked(() =>
        this.api.get<PortalPayments>('me/faculty/payments').subscribe({
          next: d => {
            this.error.set('');
            this.data.set(d);
          },
          error: e => this.error.set(errorMessage(e)),
        }),
      );
    });
  }

  protected count(tab: Tab): number {
    const d = this.data();
    if (!d) return 0;
    return { unpaid: d.unpaidItems.length, paid: d.paidItems.length, payouts: d.payouts.length, rates: Math.max(1, d.rates.length) }[tab];
  }

  protected topicOf(s: PortalSession): string {
    return [s.subjectName, s.topic].filter(Boolean).join(' — ');
  }

  protected timeLabel(s: PortalSession): string {
    return s.startTime ? `${clock(s.startTime)} – ${clock(s.endTime)} · ${s.minutes} min` : `${s.minutes} min`;
  }

  protected shortMonth(date: string): string {
    return new Date(`${date}T00:00:00Z`).toLocaleDateString('en-IN', { month: 'short', timeZone: 'UTC' });
  }
}
