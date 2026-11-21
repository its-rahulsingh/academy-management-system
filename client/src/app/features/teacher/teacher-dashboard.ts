import { Component, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { Api, FormState, Notify, errorMessage } from '../../core/api';
import { PortalOverview, PortalSession, PortalSessionList } from '../../core/models';
import { UI, today } from '../../shared/ui';
import { ClockPanelComponent } from './clock-panel';
import { portalRefresh } from './teacher-shell';

type View = 'all' | 'waiting' | 'approved' | 'paid' | 'removed';

/** "2026-11" for the month a date falls in. */
function monthOf(date: string): string {
  return date.slice(0, 7);
}

function shiftMonth(month: string, by: number): string {
  const [y, m] = month.split('-').map(Number);
  const d = new Date(y, m - 1 + by, 1);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
}

/** "14:05" → minutes since midnight. */
function toMinutes(time: string): number | null {
  const match = /^(\d{1,2}):(\d{2})/.exec(time ?? '');
  return match ? Number(match[1]) * 60 + Number(match[2]) : null;
}

/** "14:05:00" → "2:05 pm". */
export function clock(time: string | null): string {
  const total = toMinutes(time ?? '');
  if (total === null) return '';
  const h = Math.floor(total / 60);
  const m = total % 60;
  return `${((h + 11) % 12) + 1}:${String(m).padStart(2, '0')} ${h < 12 ? 'am' : 'pm'}`;
}

/** 175 → "2 h 55 min". */
export function duration(minutes: number): string {
  if (minutes < 60) return `${minutes} min`;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return m ? `${h} h ${m} min` : `${h} h`;
}

/** The faculty portal's home: clock in and out, this month at a glance, and the session list. */
@Component({
  selector: 'app-teacher-dashboard',
  imports: [UI, ClockPanelComponent],
  template: `
    @if (error()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }

    @if (overview(); as o) {
      <section class="teacher-hero">
        <div>
          <div class="kicker">FACULTY DASHBOARD</div>
          <h1>Hello, {{ o.faculty.fullName }}</h1>
          <p>
            @if (o.faculty.payType === 'Hourly') {
              {{ o.faculty.hourlyRate | money }} per hour ·
            } @else {
              Package {{ o.faculty.agreedPayout | money }} ·
            }
            Lifetime earnings {{ o.totals.earned | money }}
          </p>
        </div>
        <div class="hero-art" aria-hidden="true"><span>✎</span></div>
      </section>

      <app-clock-panel [subjects]="activeSubjects()" (changed)="load()" />

      <div class="teacher-stats">
        <article class="tstat dark">
          <div class="tstat-top">TIME · {{ monthLabel().toUpperCase() }}<span>◷</span></div>
          <div class="tstat-value">{{ duration(o.thisMonth.minutes) }}</div>
          <small>{{ o.thisMonth.sessions }} approved {{ o.thisMonth.sessions === 1 ? 'session' : 'sessions' }}</small>
        </article>
        <article class="tstat peach">
          <div class="tstat-top">WAITING APPROVAL<span>⧗</span></div>
          <div class="tstat-value">{{ o.totals.waitingSessions }}</div>
          <small>{{ duration(o.thisMonth.waitingMinutes) }} this month · {{ o.totals.waitingAmount | money }}</small>
        </article>
        <article class="tstat sky">
          <div class="tstat-top">PAYMENT PENDING<span>▣</span></div>
          <div class="tstat-value">{{ o.totals.owed | money }}</div>
          <small>
            @if (o.faculty.payType === 'Hourly') {
              {{ o.totals.unpaidSessions }} approved, not yet paid
            } @else {
              Package balance
            }
          </small>
        </article>
        <article class="tstat sage">
          <div class="tstat-top">EARNED · {{ monthLabel().toUpperCase() }}<span>₹</span></div>
          <div class="tstat-value">{{ o.thisMonth.earned | money }}</div>
          <small>Approved sessions</small>
        </article>
      </div>

      @if (o.subjects.length) {
        <h2 class="teacher-h2">My subjects</h2>
        <div class="teacher-subjects">
          @for (s of o.subjects; track s.id) {
            <article class="subject-card" [class.dimmed]="!s.active">
              <div class="subject-head">
                <span class="mini-avatar">{{ s.name.slice(0, 2).toUpperCase() }}</span>
                <div><strong>{{ s.name }}</strong><small>{{ s.courses.length ? s.courses.join(', ') : 'Not in a course yet' }}</small></div>
              </div>
              <div class="subject-foot"><b>{{ duration(s.minutesThisMonth) }}</b> this month</div>
            </article>
          }
        </div>
      }

      <section class="teacher-panel">
        <div class="teacher-toolbar">
          <div class="segmented" role="tablist">
            @for (t of tabs; track t.view) {
              <button type="button" role="tab" [class.active]="view() === t.view" [attr.aria-selected]="view() === t.view" (click)="setView(t.view)">
                {{ t.label }} <span class="count">{{ counts()[t.view] }}</span>
              </button>
            }
          </div>
          <div class="month-nav">
            <button type="button" (click)="moveMonth(-1)" aria-label="Previous month">‹</button>
            <strong>{{ monthLabel() }}</strong>
            <button type="button" (click)="moveMonth(1)" [disabled]="month() >= thisMonth" aria-label="Next month">›</button>
          </div>
        </div>
        <div class="teacher-toolbar">
          <input class="search" type="search" placeholder="Search subject or topic" [ngModel]="search()" (ngModelChange)="search.set($event); loadSessions()" />
          <button type="button" class="secondary" [class.on]="view() === 'removed'" (click)="setView(view() === 'removed' ? 'all' : 'removed')">
            ⌫ Withdrawn &amp; rejected <span class="count">{{ counts().removed }}</span>
          </button>
        </div>

        @for (s of sessions(); track s.id) {
          <article class="session-row" [class.dimmed]="s.state === 'Withdrawn' || s.state === 'Rejected'">
            <div class="session-date"><b>{{ s.taughtOn.slice(8, 10) }}</b><small>{{ shortMonth(s.taughtOn) }}</small></div>
            <div class="session-main">
              <strong>
                @if (s.startTime) {
                  {{ clock(s.startTime) }} – {{ clock(s.endTime) }} ·
                }
                {{ s.minutes }} min
              </strong>
              <small>{{ topicOf(s) || (s.loggedByStaff ? 'Logged by the academy' : 'No topic') }}</small>
              @if (s.note) {
                <small class="note">{{ s.state === 'Rejected' ? 'Reason: ' : '' }}{{ s.note }}</small>
              }
            </div>
            <div class="session-pay"><b>{{ s.amount | money: true }}</b><app-status [value]="stateLabel(s)" /></div>
            @if (s.state === 'Waiting') {
              <button type="button" class="table-action danger-text" (click)="withdrawTarget.set(s)">Withdraw</button>
            }
          </article>
        } @empty {
          <div class="teacher-empty">
            <div class="art" aria-hidden="true">✎</div>
            <strong>{{ view() === 'removed' ? 'Nothing withdrawn or rejected' : 'No sessions this month' }}</strong>
            <p>Tap <b>Clock in</b> when a class starts and <b>Clock out</b> when it ends.</p>
          </div>
        }
        @if (truncated()) {
          <p class="muted">Showing the first 500 sessions of this month.</p>
        }
      </section>

      <button type="button" class="fab" [class.running]="clockPanel()?.running()" (click)="clockPanel()?.open()">
        @if (clockPanel()?.running()) {
          <span aria-hidden="true">■</span> Clock out
        } @else {
          <span aria-hidden="true">▶</span> Clock in
        }
      </button>
    }

    @if (withdrawTarget(); as target) {
      <app-modal title="Withdraw session?" saveLabel="Withdraw" [busy]="withdrawing.busy()" [error]="withdrawing.error()" (closed)="withdrawTarget.set(null)" (saved)="withdraw(target)">
        <p>{{ target.taughtOn | day }}, {{ target.minutes }} min ({{ target.amount | money: true }}) will be taken back from approval and will not be paid.</p>
      </app-modal>
    }
  `,
})
export class TeacherDashboardComponent {
  private readonly api = inject(Api);
  private readonly notify = inject(Notify);

  protected readonly clock = clock;
  protected readonly duration = duration;
  protected readonly thisMonth = monthOf(today());
  protected readonly tabs: { view: View; label: string }[] = [
    { view: 'all', label: 'All' },
    { view: 'waiting', label: 'Waiting' },
    { view: 'approved', label: 'Approved' },
    { view: 'paid', label: 'Paid' },
  ];

  protected readonly overview = signal<PortalOverview | null>(null);
  protected readonly month = signal(this.thisMonth);
  protected readonly view = signal<View>('all');
  protected readonly search = signal('');
  protected readonly list = signal<PortalSessionList | null>(null);
  protected readonly error = signal('');
  protected readonly sessions = computed(() => this.list()?.items ?? []);
  protected readonly counts = computed(() => this.list()?.counts ?? { all: 0, waiting: 0, approved: 0, paid: 0, removed: 0 });
  protected readonly truncated = computed(() => this.sessions().length >= 500);
  protected readonly clockPanel = viewChild(ClockPanelComponent);
  protected readonly activeSubjects = computed(() => (this.overview()?.subjects ?? []).filter(s => s.active));
  protected readonly monthLabel = computed(() => {
    const [y, m] = this.month().split('-').map(Number);
    return new Date(y, m - 1, 1).toLocaleDateString('en-IN', { month: 'short', year: 'numeric' });
  });

  protected readonly withdrawing = new FormState();
  protected readonly withdrawTarget = signal<PortalSession | null>(null);

  constructor() {
    // Loads on start and again whenever the refresh button in the top bar is pressed.
    effect(() => {
      portalRefresh();
      untracked(() => this.load());
    });
  }

  protected load(): void {
    this.api.get<PortalOverview>('me/faculty', { month: this.month() }).subscribe({
      next: o => {
        this.error.set('');
        this.overview.set(o);
      },
      error: e => this.error.set(errorMessage(e)),
    });
    this.loadSessions();
  }

  protected loadSessions(): void {
    this.api.get<PortalSessionList>('me/faculty/sessions', { month: this.month(), view: this.view(), search: this.search() }).subscribe({
      next: list => this.list.set(list),
      error: e => this.error.set(errorMessage(e)),
    });
  }

  protected setView(view: View): void {
    this.view.set(view);
    this.loadSessions();
  }

  protected moveMonth(by: number): void {
    const next = shiftMonth(this.month(), by);
    if (next > this.thisMonth) return;
    this.month.set(next);
    this.load();
  }

  protected topicOf(s: PortalSession): string {
    return [s.subjectName, s.topic].filter(Boolean).join(' — ');
  }

  protected shortMonth(date: string): string {
    return new Date(`${date}T00:00:00Z`).toLocaleDateString('en-IN', { month: 'short', timeZone: 'UTC' });
  }

  protected stateLabel(s: PortalSession): string {
    return s.state === 'Approved' ? 'Approved · unpaid' : s.state === 'Waiting' ? 'Waiting approval' : s.state;
  }

  protected withdraw(target: PortalSession): void {
    this.withdrawing.submit(this.api.post(`me/faculty/sessions/${target.id}/withdraw`, {}), () => {
      this.withdrawTarget.set(null);
      this.notify.success('Session withdrawn');
      this.load();
    });
  }
}
