import { Component, computed, inject, input, output, signal } from '@angular/core';
import { Api, FormState, Notify, errorMessage } from '../../core/api';
import { ClockMethod, ClockResult, ClockStatus } from '../../core/models';
import { Ticker, stopwatch, timeOfDay } from '../../shared/clock';
import { QrScannerComponent, checkInCodeFrom } from '../../shared/qr-scanner';
import { UI, today } from '../../shared/ui';

type Step = 'choose' | 'scan' | 'locating' | 'sending';

/** Reads the phone's position once, as precisely as it can. */
function currentPosition(): Promise<GeolocationPosition> {
  return new Promise((resolve, reject) => {
    if (!navigator.geolocation) return reject(new Error('This browser cannot share its location.'));
    navigator.geolocation.getCurrentPosition(resolve, reject, { enableHighAccuracy: true, timeout: 20000, maximumAge: 0 });
  });
}

function locationError(error: unknown): string {
  if (typeof GeolocationPositionError !== 'undefined' && error instanceof GeolocationPositionError) {
    if (error.code === error.PERMISSION_DENIED) return 'Location access was blocked. Allow location for this site in your browser settings and try again.';
    if (error.code === error.TIMEOUT) return 'Your location took too long to find. Step near a window or turn on GPS and try again.';
    return 'Your location could not be found. Turn on GPS / location services and try again.';
  }
  return error instanceof Error ? error.message : 'Your location could not be found.';
}

/**
 * Clock in / clock out. The admin's QR code is the normal way in and out; the phone's location is offered only when
 * no admin is free, and a class clocked that way waits for approval.
 */
@Component({
  selector: 'app-clock-panel',
  imports: [UI, QrScannerComponent],
  template: `
    @if (status(); as s) {
      @for (n of s.needsEndTime; track n.id) {
        <section class="clock-card warning">
          <div class="clock-main">
            <small>NOT CLOCKED OUT</small>
            <strong>Class on {{ dayOf(n.inAtUtc) }} from {{ timeOfDay(n.inAtUtc) }}{{ n.subjectName ? ' · ' + n.subjectName : '' }}</strong>
            <span>It was still running at midnight, so it was closed. It is not paid until you enter when it ended.</span>
          </div>
          <button type="button" class="clock-button" (click)="openEndTime(n)">Enter end time</button>
        </section>
      }
      @if (s.running; as r) {
        <section class="clock-card running">
          <div class="clock-dot" aria-hidden="true"></div>
          <div class="clock-main">
            <small>IN CLASS{{ r.subjectName ? ' · ' + r.subjectName.toUpperCase() : '' }}</small>
            <strong class="clock-timer" aria-live="off">{{ stopwatch(ticker.since(r.inAtUtc)) }}</strong>
            <span>Started {{ timeOfDay(r.inAtUtc) }} · {{ r.inMethod === 'Qr' ? 'checked in by QR' : 'checked in by location' }}</span>
          </div>
          <button type="button" class="clock-button out" (click)="open('out')">■ Clock out</button>
        </section>
      } @else {
        <section class="clock-card">
          <div class="clock-main">
            <small>NOT IN CLASS</small>
            <strong>Clock in when your class starts</strong>
            <span>Scan the QR code on the admin's screen. Your time is counted by the system.</span>
          </div>
          <button type="button" class="clock-button" [disabled]="!s.canClockIn" (click)="open('in')">▶ Clock in</button>
        </section>
        @if (!s.canClockIn) {
          <p class="helper">The academy has not set your hourly rate, or your record is marked as not teaching. Ask an admin.</p>
        }
      }
      @if (s.canClockIn) {
        <button type="button" class="text-button forgot-link" (click)="openRequest()">Forgot to clock in for a class? Tell the academy →</button>
      }
    }

    @if (mode(); as m) {
      <app-modal [title]="m === 'in' ? 'Clock in' : 'Clock out'" saveLabel="" [error]="error()" (closed)="close()">
        @if (step() === 'choose') {
          @if (m === 'in' && subjects().length) {
            <label for="clockSubject">Subject</label>
            <select id="clockSubject" [(ngModel)]="subjectId">
              <option value="">Not specified</option>
              @for (s of subjects(); track s.id) {
                <option [value]="s.id">{{ s.name }}</option>
              }
            </select>
          }
          @if (m === 'out') {
            <label for="clockTopic">What did you teach? (optional)</label>
            <input id="clockTopic" [(ngModel)]="topic" maxlength="200" placeholder="e.g. Batch A — Thermodynamics" />
            @if (overLimit() || endTimeOpen()) {
              <div class="late-box">
                @if (overLimit()) {
                  <p>You have been clocked in for <b>{{ stopwatch(ticker.since(status()!.running!.inAtUtc)) }}</b>, longer than a usual class. If it ended earlier, enter when.</p>
                }
                <label for="clockEnd">Class actually ended at (optional)</label>
                <input id="clockEnd" type="time" [(ngModel)]="endTime" />
                <div class="helper">Leave empty if the class has just ended. An entered time is sent for approval.</div>
              </div>
            } @else {
              <button type="button" class="text-button" (click)="endTimeOpen.set(true)">Class ended earlier? Enter the real end time</button>
            }
          }
          <button type="button" class="primary wide clock-choice" (click)="step.set('scan')">
            ⌗ Scan the admin's QR code
            @if (m === 'out' && (overLimit() || endTime)) {
              <small>Recommended · this class still goes for approval ({{ endTime ? 'end time entered by hand' : 'longer than usual' }})</small>
            } @else {
              <small>Recommended · {{ m === 'in' ? 'proves you are at the academy' : 'the class is approved straight away' }}</small>
            }
          </button>
          @if (status()?.locationAvailable) {
            <div class="clock-or"><span>Admin not available?</span></div>
            <button type="button" class="secondary wide clock-choice" (click)="useLocation()">
              ⌖ {{ m === 'in' ? 'Clock in' : 'Clock out' }} with my location
              <small>Works within {{ status()?.radiusMeters }} m of the academy · the class waits for approval</small>
            </button>
          } @else {
            <p class="helper">The academy has not set up location clock-in, so ask an admin to show the QR code.</p>
          }
        } @else if (step() === 'scan') {
          <app-qr-scanner (scanned)="onScanned($event)" (failed)="cameraFailed.set(true)" />
          <div class="button-row">
            <button type="button" class="text-button" (click)="step.set('choose')">← Back</button>
            @if (status()?.locationAvailable && cameraFailed()) {
              <button type="button" class="secondary" (click)="useLocation()">Use my location instead</button>
            }
          </div>
        } @else {
          <div class="clock-wait">
            <div class="spinner" aria-hidden="true"></div>
            <p>{{ step() === 'locating' ? 'Finding your location…' : m === 'in' ? 'Clocking you in…' : 'Clocking you out…' }}</p>
          </div>
        }
      </app-modal>
    }

    @if (endTarget(); as n) {
      <app-modal title="When did the class end?" saveLabel="Save end time" [busy]="ending.busy()" [error]="ending.error()" (closed)="endTarget.set(null)" (saved)="saveEndTime(n.id)">
        <p class="helper">Class on {{ dayOf(n.inAtUtc) }}, started {{ timeOfDay(n.inAtUtc) }}{{ n.subjectName ? ' · ' + n.subjectName : '' }}. It will be sent for approval.</p>
        <label for="endedAt">Ended at *</label>
        <input id="endedAt" type="time" [(ngModel)]="endTime" />
        <label for="endTopic">What did you teach? (optional)</label>
        <input id="endTopic" [(ngModel)]="topic" maxlength="200" />
      </app-modal>
    }

    @if (request.open()) {
      <app-modal title="Forgot to clock in" saveLabel="Send for approval" [busy]="request.busy()" [error]="request.error()" (closed)="request.close()" (saved)="sendRequest()">
        <p class="helper">For a class you taught but did not clock into. The academy checks every request before it is paid.</p>
        <label for="reqDate">Date *</label>
        <input id="reqDate" type="date" [max]="todayDate" [(ngModel)]="req.date" />
        <div class="two-fields">
          <div><label for="reqStart">Started at *</label><input id="reqStart" type="time" [(ngModel)]="req.startTime" /></div>
          <div><label for="reqEnd">Ended at *</label><input id="reqEnd" type="time" [(ngModel)]="req.endTime" /></div>
        </div>
        @if (subjects().length) {
          <label for="reqSubject">Subject</label>
          <select id="reqSubject" [(ngModel)]="req.subjectId">
            <option value="">Not specified</option>
            @for (s of subjects(); track s.id) {
              <option [value]="s.id">{{ s.name }}</option>
            }
          </select>
        }
        <label for="reqTopic">What did you teach? (optional)</label>
        <input id="reqTopic" [(ngModel)]="req.topic" maxlength="200" />
        <label for="reqReason">Why wasn't it clocked in? *</label>
        <input id="reqReason" [(ngModel)]="req.reason" maxlength="500" placeholder="e.g. Phone battery was dead" />
      </app-modal>
    }
  `,
})
export class ClockPanelComponent {
  private readonly api = inject(Api);
  private readonly notify = inject(Notify);
  protected readonly ticker = inject(Ticker);
  protected readonly stopwatch = stopwatch;
  protected readonly timeOfDay = timeOfDay;

  /** The faculty member's subjects, for picking one at clock-in. */
  readonly subjects = input<{ id: string; name: string }[]>([]);
  /** Fires after a clock-in or clock-out, so the page can refresh its figures. */
  readonly changed = output<void>();

  protected readonly status = signal<ClockStatus | null>(null);
  protected readonly mode = signal<'in' | 'out' | null>(null);
  protected readonly step = signal<Step>('choose');
  protected readonly error = signal('');
  protected readonly cameraFailed = signal(false);
  readonly running = computed(() => !!this.status()?.running);
  protected subjectId = '';
  protected topic = '';
  protected endTime = '';
  protected readonly endTimeOpen = signal(false);
  protected readonly todayDate = today();
  protected readonly overLimit = computed(() => {
    const s = this.status();
    return !!s?.running && this.ticker.since(s.running.inAtUtc) > s.maxClassMinutes * 60000;
  });
  protected readonly endTarget = signal<ClockStatus['needsEndTime'][number] | null>(null);
  protected readonly ending = new FormState();
  protected readonly request = new FormState();
  protected req = { date: today(), startTime: '', endTime: '', subjectId: '', topic: '', reason: '' };

  constructor() {
    this.load();
  }

  load(): void {
    this.api.get<ClockStatus>('me/faculty/clock').subscribe({
      next: s => {
        this.ticker.sync(s.serverNowUtc);
        this.status.set(s);
      },
      error: e => this.notify.success(errorMessage(e)),
    });
  }

  /** Opens the clock-in or clock-out dialog; the page's floating button calls this too. */
  open(mode?: 'in' | 'out'): void {
    const m = mode ?? (this.running() ? 'out' : 'in');
    if (m === 'in' && !this.status()?.canClockIn) return;
    const subjects = this.subjects();
    this.subjectId = subjects.length === 1 ? subjects[0].id : '';
    this.topic = '';
    this.endTime = '';
    this.endTimeOpen.set(false);
    this.error.set('');
    this.cameraFailed.set(false);
    this.step.set('choose');
    this.mode.set(m);
  }

  protected close(): void {
    if (this.step() === 'sending' || this.step() === 'locating') return;
    this.mode.set(null);
  }

  protected onScanned(text: string): void {
    this.send('Qr', { code: checkInCodeFrom(text) });
  }

  protected async useLocation(): Promise<void> {
    this.error.set('');
    this.step.set('locating');
    try {
      const position = await currentPosition();
      this.send('Location', {
        latitude: position.coords.latitude,
        longitude: position.coords.longitude,
        accuracy: position.coords.accuracy,
      });
    } catch (error) {
      this.step.set('choose');
      this.error.set(locationError(error));
    }
  }

  private send(method: ClockMethod, proof: Record<string, unknown>): void {
    const m = this.mode();
    if (!m) return;
    this.step.set('sending');
    const body = m === 'in'
      ? { method, ...proof, subjectId: this.subjectId || null }
      : { method, ...proof, topic: this.topic, endTime: this.endTime || null };
    this.api.post<ClockResult | { inAtUtc: string }>(`me/faculty/clock/${m}`, body).subscribe({
      next: result => {
        this.mode.set(null);
        this.notify.success(this.message(m, method, result));
        this.load();
        this.changed.emit();
      },
      error: e => {
        this.error.set(errorMessage(e));
        this.step.set('choose');
      },
    });
  }

  protected dayOf(utc: string): string {
    return new Date(utc).toLocaleDateString('en-IN', { day: '2-digit', month: 'short' });
  }

  protected openEndTime(n: ClockStatus['needsEndTime'][number]): void {
    this.endTime = '';
    this.topic = '';
    this.ending.show();
    this.endTarget.set(n);
  }

  protected saveEndTime(id: string): void {
    if (!this.endTime) return this.ending.fail('Enter the time the class ended.');
    this.ending.submit(this.api.post<ClockResult>(`me/faculty/clock/${id}/end`, { endTime: this.endTime, topic: this.topic }), r => {
      this.endTarget.set(null);
      this.notify.success(`${r.minutes} min sent for approval`);
      this.load();
      this.changed.emit();
    });
  }

  protected openRequest(): void {
    const subjects = this.subjects();
    this.req = { date: today(), startTime: '', endTime: '', subjectId: subjects.length === 1 ? subjects[0].id : '', topic: '', reason: '' };
    this.request.show();
  }

  protected sendRequest(): void {
    const r = this.req;
    if (!r.date || !r.startTime || !r.endTime) return this.request.fail('Enter the date, and when the class started and ended.');
    if (r.endTime <= r.startTime) return this.request.fail('The end time must be after the start time.');
    if (!r.reason.trim()) return this.request.fail('Say why the class was not clocked in.');
    this.request.submit(this.api.post<ClockResult>('me/faculty/clock/request', { ...r, subjectId: r.subjectId || null }), result => {
      this.notify.success(`${result.minutes} min sent to the academy for approval`);
      this.changed.emit();
    });
  }

  private message(mode: 'in' | 'out', method: ClockMethod, result: ClockResult | { inAtUtc: string }): string {
    if (mode === 'in') return method === 'Qr' ? 'Clocked in — your class timer has started' : 'Clocked in by location — your class timer has started';
    const r = result as ClockResult;
    if (r.cancelled) return 'Clock-in cancelled: the class was under a minute';
    if (this.endTime) return `Clocked out — ${r.minutes} min, with the end time you entered, sent for approval`;
    return r.review === 'Approved' ? `Clocked out — ${r.minutes} min recorded and approved` : `Clocked out — ${r.minutes} min sent for approval`;
  }
}
