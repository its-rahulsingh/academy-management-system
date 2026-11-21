import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, FormState, Notify, errorMessage } from '../../core/api';
import { ClockResult, LiveClass, LiveClasses } from '../../core/models';
import { Ticker, stopwatch, timeOfDay } from '../../shared/clock';
import { UI } from '../../shared/ui';

/** Classes running right now, with live timers. Used on the dashboard and on the Class check-in page. */
@Component({
  selector: 'app-live-classes',
  imports: [UI, RouterLink],
  template: `
    <section class="surface live-classes">
      <div class="surface-title">
        <h2><span class="live-dot" [class.on]="items().length > 0" aria-hidden="true"></span> Classes running now</h2>
        <span class="live-count">{{ items().length }}</span>
      </div>
      @if (error()) {
        <div class="alert" role="alert">{{ error() }}</div>
      }
      @if (overdueCount()) {
        <div class="alert" role="status">
          ⚠ {{ overdueCount() }} {{ overdueCount() === 1 ? 'class has' : 'classes have' }} run longer than {{ maxHours() }} —
          the faculty member probably forgot to clock out. They have been emailed; you can also stop the class below.
        </div>
      }
      @for (c of items(); track c.id) {
        <div class="live-row" [class.overdue]="c.overdue">
          <div class="mini-avatar">{{ initials(c.facultyName) }}</div>
          <div class="live-main">
            <strong>{{ c.facultyName }}</strong>
            <small>
              {{ c.subjectName || 'Subject not given' }} · from {{ timeOfDay(c.inAtUtc) }} ·
              {{ c.inMethod === 'Qr' ? 'QR' + (c.verifiedBy ? ' (' + c.verifiedBy + ')' : '') : 'location, ' + (c.inDistanceMeters ?? 0) + ' m away' }}
            </small>
            @if (c.overdue) {
              <small class="negative">Longer than {{ maxHours() }} — probably forgot to clock out{{ c.reminded ? ' · reminder emailed' : '' }}</small>
            }
          </div>
          <b class="live-timer">{{ stopwatch(ticker.since(c.inAtUtc)) }}</b>
          @if (canStop()) {
            <button type="button" class="table-action danger-text" (click)="openStop(c)">Stop</button>
          }
        </div>
      } @empty {
        <p class="empty">No class is running at the moment.</p>
      }
      @if (needsEnd().length) {
        <h3 class="section-title">Not clocked out — needs an end time</h3>
        <p class="helper">Left running at midnight and closed automatically. Nothing is paid until the end time is entered (by you or the faculty member).</p>
        @for (n of needsEnd(); track n.id) {
          <div class="live-row overdue">
            <div class="mini-avatar">{{ initials(n.facultyName) }}</div>
            <div class="live-main">
              <strong>{{ n.facultyName }}</strong>
              <small>{{ dayOf(n.inAtUtc) }} from {{ timeOfDay(n.inAtUtc) }}{{ n.subjectName ? ' · ' + n.subjectName : '' }}</small>
            </div>
            @if (canStop()) {
              <button type="button" class="table-action" (click)="openEnd(n)">Enter end time</button>
            }
          </div>
        }
      }
      @if (showLink() && (items().length || needsEnd().length)) {
        <a class="text-button" routerLink="/app/attendance">Open class check-in →</a>
      }
    </section>

    @if (endTarget(); as n) {
      <app-modal title="When did the class end?" saveLabel="Save end time" [busy]="stopping.busy()" [error]="stopping.error()" (closed)="endTarget.set(null)" (saved)="saveEnd(n.id)">
        <p class="helper">{{ n.facultyName }}'s class on {{ dayOf(n.inAtUtc) }} started {{ timeOfDay(n.inAtUtc) }}. It goes to the approval queue on the Faculty page.</p>
        <label for="liveEnd">Ended at *</label>
        <input id="liveEnd" type="time" [(ngModel)]="endTime" />
      </app-modal>
    }

    @if (stopTarget(); as target) {
      <app-modal title="Stop this class" saveLabel="Stop class" [danger]="true" [busy]="stopping.busy()" [error]="stopping.error()" (closed)="stopTarget.set(null)" (saved)="stop(target)">
        <p>
          {{ target.facultyName }}'s class started at {{ timeOfDay(target.inAtUtc) }} and has been running for {{ stopwatch(ticker.since(target.inAtUtc)) }}.
          Use this when they forgot to clock out. The time goes to the approval queue on the Faculty page.
        </p>
        <label for="stopEnd">When did the class actually end?</label>
        <input id="stopEnd" type="time" [(ngModel)]="endTime" />
        <div class="helper">Leave empty to end it now.</div>
      </app-modal>
    }
  `,
})
export class LiveClassesComponent {
  private readonly api = inject(Api);
  private readonly notify = inject(Notify);
  protected readonly ticker = inject(Ticker);
  protected readonly stopwatch = stopwatch;
  protected readonly timeOfDay = timeOfDay;

  readonly canStop = input(false);
  readonly showLink = input(false);
  protected readonly items = signal<LiveClass[]>([]);
  protected readonly needsEnd = signal<LiveClasses['needsEndTime']>([]);
  protected readonly maxMinutes = signal(180);
  protected readonly overdueCount = computed(() => this.items().filter(c => c.overdue).length);
  protected readonly maxHours = computed(() => {
    const m = this.maxMinutes();
    return m % 60 ? `${Math.floor(m / 60)} h ${m % 60} min` : `${m / 60} ${m === 60 ? 'hour' : 'hours'}`;
  });
  protected readonly endTarget = signal<LiveClasses['needsEndTime'][number] | null>(null);
  protected endTime = '';
  protected readonly error = signal('');
  protected readonly stopTarget = signal<LiveClass | null>(null);
  protected readonly stopping = new FormState();

  constructor() {
    this.load();
    // New clock-ins and clock-outs show up without reloading the page.
    const timer = setInterval(() => this.load(), 15000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  load(): void {
    this.api.get<LiveClasses>('attendance/live').subscribe({
      next: live => {
        this.ticker.sync(live.serverNowUtc);
        this.error.set('');
        this.items.set(live.items);
        this.needsEnd.set(live.needsEndTime);
        this.maxMinutes.set(live.maxClassMinutes);
      },
      error: e => this.error.set(errorMessage(e)),
    });
  }

  protected initials(name: string): string {
    return name.split(/\s+/).filter(Boolean).slice(0, 2).map(p => p[0]).join('').toUpperCase();
  }

  protected dayOf(utc: string): string {
    return new Date(utc).toLocaleDateString('en-IN', { day: '2-digit', month: 'short' });
  }

  protected openEnd(n: LiveClasses['needsEndTime'][number]): void {
    this.endTime = '';
    this.stopping.show();
    this.endTarget.set(n);
  }

  protected saveEnd(id: string): void {
    if (!this.endTime) return this.stopping.fail('Enter the time the class ended.');
    this.stopping.submit(this.api.post<ClockResult>(`attendance/${id}/end`, { endTime: this.endTime }), result => {
      this.endTarget.set(null);
      this.notify.success(`${result.minutes} min sent for approval`);
      this.load();
    });
  }

  protected openStop(target: LiveClass): void {
    this.endTime = '';
    this.stopping.show();
    this.stopTarget.set(target);
  }

  protected stop(target: LiveClass): void {
    this.stopping.submit(this.api.post<ClockResult>(`attendance/${target.id}/stop`, { endTime: this.endTime || null }), result => {
      this.stopTarget.set(null);
      this.notify.success(result.cancelled ? 'Class stopped (under a minute, nothing recorded)' : `Class stopped — ${result.minutes} min sent for approval`);
      this.load();
    });
  }
}
