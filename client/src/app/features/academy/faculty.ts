import { Component, computed, inject, signal } from '@angular/core';
import { Api, FormState, Notify, PagedList, errorMessage } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { Faculty, FacultyPayType, FacultySession, PAYMENT_METHODS, Paged, Payout, Permission, SubjectRef } from '../../core/models';
import { UI, today } from '../../shared/ui';
import { clock } from '../teacher/teacher-dashboard';
import { SubjectPickerComponent } from './subjects';

/** Same rounding as the server: minutes × hourly rate ÷ 60, to the paisa. */
function sessionPay(minutes: number, hourlyRate: number): number {
  return Math.round((minutes * hourlyRate * 100) / 60) / 100;
}

@Component({
  selector: 'app-faculty',
  imports: [UI, SubjectPickerComponent],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">ACADEMY WORKSPACE</div>
        <h1>Faculty</h1>
        <p>Package or hourly pay, teaching time, what has been paid and what is owed.</p>
      </div>
      <div class="page-actions">
        @if (can(permission.PayoutsWrite) && hourly().length) {
          <button type="button" class="secondary" (click)="openSession()">+ Log teaching time</button>
        }
        @if (can(permission.PayoutsWrite)) {
          <button type="button" class="secondary" (click)="openPayout()">+ Record payout</button>
        }
        @if (can(permission.FacultyWrite)) {
          <button type="button" class="primary" (click)="openForm()">+ Add faculty</button>
        }
      </div>
    </div>

    @if (error()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }

    @if (can(permission.PayoutsWrite) && waiting.total()) {
      <section class="surface review-queue">
        <div class="surface-title">
          <h2>Waiting for approval</h2>
          <small class="muted">{{ waiting.total() }} {{ waiting.total() === 1 ? 'class needs' : 'classes need' }} checking: clocked by location, longer than usual, times entered by hand, or stopped by an admin</small>
        </div>
        <div class="table-scroll">
          <table>
            <thead><tr><th>FACULTY</th><th>DATE</th><th>TIME</th><th>AMOUNT</th><th></th></tr></thead>
            <tbody>
              @for (s of waiting.items(); track s.id) {
                <tr>
                  <td>
                    <strong>{{ s.facultyName }}</strong><small>{{ sessionLabel(s) || 'No topic given' }}</small>
                    @if (s.reviewNote) {
                      <small class="review-note">{{ s.reviewNote }}</small>
                    }
                  </td>
                  <td>{{ s.taughtOn | day }}</td>
                  <td>{{ timeLabel(s) }}<small>{{ proofLabel(s) }}</small></td>
                  <td>{{ s.amount | money: true }}<small>{{ s.hourlyRate | money }} / hour</small></td>
                  <td class="row-actions">
                    <button type="button" class="table-action" [disabled]="reviewing()" (click)="approve(s)">✓ Approve</button>
                    <button type="button" class="table-action danger-text" (click)="rejectTarget.set(s)">Reject</button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <app-pager [page]="waiting.page()" [total]="waiting.total()" (pageChange)="waiting.load($event)" />
      </section>
    }

    <div class="stats four">
      <article class="stat white"><div class="stat-top">FACULTY</div><div class="stat-value">{{ faculty().length }}</div><small>{{ hourly().length }} paid hourly</small></article>
      <article class="stat white"><div class="stat-top">EARNED</div><div class="stat-value">{{ earned() | money }}</div><small>Packages + logged sessions</small></article>
      <article class="stat green"><div class="stat-top">PAID</div><div class="stat-value">{{ paid() | money }}</div></article>
      <article class="stat pink"><div class="stat-top">OWED</div><div class="stat-value">{{ earned() - paid() | money }}</div></article>
    </div>

    <div class="surface table-scroll">
      <table>
        <thead><tr><th>FACULTY</th><th>PAY</th><th>EARNED</th><th>PAID</th><th>OWED</th><th>STATUS</th><th></th></tr></thead>
        <tbody>
          @for (f of faculty(); track f.id) {
            <tr [class.dimmed]="!f.active">
              <td>
                <strong>{{ f.fullName }}</strong>
                @if (f.subjects.length) {
                  <small>{{ subjectNames(f) }}</small>
                } @else {
                  <small>{{ f.specialization || f.phone || f.email || '—' }}</small>
                }
                @if (f.login) {
                  <small class="muted">Portal: {{ f.login.email }}{{ f.login.active ? '' : ' (disabled)' }}</small>
                }
              </td>
              <td>
                @if (f.payType === 'Hourly') {
                  <strong>{{ f.hourlyRate | money }} / hour</strong><small>{{ f.minutesTaught }} min approved{{ f.pendingSessions ? ' · ' + f.pendingSessions + ' waiting' : '' }}</small>
                } @else {
                  <strong>Package</strong><small>{{ f.agreedPayout | money }}</small>
                }
              </td>
              <td>{{ f.earned | money }}</td>
              <td class="positive">{{ f.paid | money }}</td>
              <td class="negative">{{ f.earned - f.paid | money }}</td>
              <td><app-status [value]="f.active ? 'Active' : 'Inactive'" /></td>
              <td class="row-actions">
                @if (can(permission.PayoutsWrite) && f.payType === 'Hourly') {
                  <button type="button" class="table-action" (click)="openSession(f)">Log time</button>
                }
                @if (can(permission.PayoutsWrite) && f.earned - f.paid > 0) {
                  <button type="button" class="table-action" (click)="openPayout(f)">Pay</button>
                }
                @if (can(permission.UsersManage) && !f.login) {
                  <button type="button" class="table-action" (click)="openLogin(f)">Give portal login</button>
                }
                @if (can(permission.FacultyWrite)) {
                  <button type="button" class="table-action" (click)="openForm(f)">Edit →</button>
                }
              </td>
            </tr>
          } @empty {
            <tr><td colspan="7" class="empty">No faculty added yet.</td></tr>
          }
        </tbody>
      </table>
    </div>

    @if (can(permission.PayoutsRead) && (hourly().length || sessions.total())) {
      <section class="surface gap-top">
        <div class="surface-title"><h2>Teaching log</h2><small class="muted">Hourly faculty, this academic year</small></div>
        <div class="table-scroll">
          <table>
            <thead><tr><th>FACULTY</th><th>DATE</th><th>TIME</th><th>RATE</th><th>AMOUNT</th><th>STATUS</th><th></th></tr></thead>
            <tbody>
              @for (s of sessions.items(); track s.id) {
                <tr [class.dimmed]="s.status === 'Voided' || s.review === 'Rejected'">
                  <td><strong>{{ s.facultyName }}</strong><small>{{ s.status === 'Voided' ? 'Voided: ' + s.voidReason : s.review === 'Rejected' ? 'Rejected: ' + s.reviewNote : sessionLabel(s) }}</small></td>
                  <td>{{ s.taughtOn | day }}</td>
                  <td>{{ timeLabel(s) }}</td>
                  <td>{{ s.hourlyRate | money }} / hour</td>
                  <td>{{ s.amount | money: true }}</td>
                  <td><app-status [value]="sessionStatus(s)" /></td>
                  <td class="row-actions">
                    @if (s.status === 'Posted' && s.review !== 'Rejected' && can(permission.LedgerCorrect)) {
                      <button type="button" class="table-action danger-text" (click)="voidSessionTarget.set(s)">Void</button>
                    }
                  </td>
                </tr>
              } @empty {
                <tr><td colspan="7" class="empty">No teaching time logged in this year yet.</td></tr>
              }
            </tbody>
          </table>
        </div>
        <app-pager [page]="sessions.page()" [total]="sessions.total()" (pageChange)="sessions.load($event)" />
      </section>
    }

    @if (can(permission.PayoutsRead)) {
      <section class="surface gap-top">
        <div class="surface-title"><h2>Payout history</h2></div>
        <div class="table-scroll">
          <table>
            <thead><tr><th>FACULTY</th><th>DATE</th><th>METHOD</th><th>AMOUNT</th><th>STATUS</th><th></th></tr></thead>
            <tbody>
              @for (p of payouts.items(); track p.id) {
                <tr [class.dimmed]="p.status === 'Voided'">
                  <td><strong>{{ p.facultyName }}</strong><small>{{ p.status === 'Voided' ? 'Voided: ' + p.voidReason : p.notes }}</small></td>
                  <td>{{ p.paidOn | day }}</td>
                  <td>{{ p.method }}</td>
                  <td>{{ p.amount | money: true }}</td>
                  <td><app-status [value]="p.status" /></td>
                  <td class="row-actions">
                    @if (p.status === 'Posted' && can(permission.LedgerCorrect)) {
                      <button type="button" class="table-action danger-text" (click)="voidTarget.set(p)">Void</button>
                    }
                  </td>
                </tr>
              } @empty {
                <tr><td colspan="6" class="empty">No payouts recorded yet.</td></tr>
              }
            </tbody>
          </table>
        </div>
        <app-pager [page]="payouts.page()" [total]="payouts.total()" (pageChange)="payouts.load($event)" />
      </section>
    }

    @if (form.open()) {
      <app-modal [title]="model.id ? 'Edit faculty member' : 'Add faculty member'" [busy]="form.busy()" [error]="form.error()" (closed)="form.close()" (saved)="save()">
        <label for="name">Full name *</label>
        <input id="name" [(ngModel)]="model.fullName" maxlength="150" />
        <div class="two-fields">
          <div><label for="email">Email</label><input id="email" type="email" [(ngModel)]="model.email" /></div>
          <div><label for="phone">Phone</label><input id="phone" [(ngModel)]="model.phone" maxlength="32" /></div>
        </div>
        <label>Subjects taught</label>
        <app-subject-picker [subjects]="subjects()" [(selected)]="model.subjectIds" label="Subjects taught" />
        <label for="spec">Specialisation / notes</label>
        <input id="spec" [(ngModel)]="model.specialization" placeholder="e.g. IIT-JEE mentor" maxlength="150" />

        <fieldset class="choice-group">
          <legend>How is this faculty member paid? *</legend>
          <label class="choice"><input type="radio" name="payType" value="Package" [(ngModel)]="model.payType" /> <span><strong>Package</strong><small>A fixed amount, e.g. an annual package</small></span></label>
          <label class="choice"><input type="radio" name="payType" value="Hourly" [(ngModel)]="model.payType" /> <span><strong>Hourly</strong><small>Paid for the minutes they teach</small></span></label>
        </fieldset>
        @if (model.payType === 'Package') {
          <label for="agreed">Package amount (₹) *</label>
          <input id="agreed" type="number" min="0" step="0.01" [(ngModel)]="model.agreedPayout" />
        } @else {
          <label for="rate">Rate per hour (₹) *</label>
          <input id="rate" type="number" min="0.01" step="0.01" [(ngModel)]="model.hourlyRate" />
          <div class="helper">Log each class in minutes; pay is worked out from this rate. Changing the rate later does not re-price time already logged.</div>
        }
        @if (model.id) {
          <label class="check"><input type="checkbox" [(ngModel)]="model.active" /> Currently teaching</label>
        }
      </app-modal>
    }

    @if (session.open()) {
      <app-modal title="Log teaching time" saveLabel="Log time" [busy]="session.busy()" [error]="session.error()" (closed)="session.close()" (saved)="saveSession()">
        <label for="sessionWho">Faculty member *</label>
        <select id="sessionWho" [(ngModel)]="sessionModel.facultyId">
          <option value="" disabled>Select faculty</option>
          @for (f of hourly(); track f.id) {
            <option [value]="f.id">{{ f.fullName }} — {{ f.hourlyRate | money }} / hour</option>
          }
        </select>
        <div class="two-fields">
          <div>
            <label for="minutes">Time taught (minutes) *</label>
            <div class="input-suffix">
              <input id="minutes" type="number" min="1" max="1440" step="1" inputmode="numeric" [(ngModel)]="sessionModel.minutes" placeholder="e.g. 85" />
              <span>min</span>
            </div>
          </div>
          <div><label for="taughtOn">Date</label><input id="taughtOn" type="date" [(ngModel)]="sessionModel.taughtOn" /></div>
        </div>
        @if (sessionFaculty()?.subjects?.length) {
          <label for="sessionSubject">Subject</label>
          <select id="sessionSubject" [(ngModel)]="sessionModel.subjectId">
            <option value="">Not specified</option>
            @for (s of sessionFaculty()!.subjects; track s.id) {
              <option [value]="s.id">{{ s.name }}</option>
            }
          </select>
        }
        <label for="topic">Class or topic</label>
        <input id="topic" [(ngModel)]="sessionModel.topic" maxlength="200" placeholder="e.g. Physics — Batch A" />
        @if (sessionFaculty(); as f) {
          @if (validMinutes()) {
            <p class="helper calc">{{ sessionModel.minutes }} min × {{ f.hourlyRate | money }} / hour = <strong>{{ sessionAmount() | money: true }}</strong></p>
          }
        }
      </app-modal>
    }

    @if (payout.open()) {
      <app-modal title="Record faculty payout" saveLabel="Record payout" [busy]="payout.busy()" [error]="payout.error()" (closed)="payout.close()" (saved)="savePayout()">
        <label for="who">Faculty member *</label>
        <select id="who" [(ngModel)]="payoutModel.facultyId">
          <option value="" disabled>Select faculty</option>
          @for (f of faculty(); track f.id) {
            <option [value]="f.id">{{ f.fullName }} — owed {{ f.earned - f.paid | money }}</option>
          }
        </select>
        <div class="two-fields">
          <div><label for="amount">Amount (₹) *</label><input id="amount" type="number" min="0.01" step="0.01" [(ngModel)]="payoutModel.amount" /></div>
          <div>
            <label for="method">Method *</label>
            <select id="method" [(ngModel)]="payoutModel.method">
              @for (m of methods; track m) {
                <option>{{ m }}</option>
              }
            </select>
          </div>
        </div>
        <label for="date">Date</label>
        <input id="date" type="date" [(ngModel)]="payoutModel.paidOn" />
        <label for="notes">Notes</label>
        <textarea id="notes" rows="2" maxlength="1200" [(ngModel)]="payoutModel.notes"></textarea>
      </app-modal>
    }

    @if (voidTarget(); as target) {
      <app-reason-dialog
        title="Void payout"
        [description]="'Payout to ' + target.facultyName + ' stays on record, marked as voided.'"
        confirmLabel="Void payout"
        [busy]="voiding.busy()"
        [error]="voiding.error()"
        (cancelled)="voidTarget.set(null)"
        (confirmed)="confirmVoid(target, $event)"
      />
    }

    @if (login.open()) {
      <app-modal title="Give portal login" saveLabel="Create login" [busy]="login.busy()" [error]="login.error()" (closed)="login.close()" (saved)="saveLogin()">
        <p class="helper">
          {{ loginModel.name }} will sign in with your academy code and this email, log their own teaching time and see their own pay.
          They cannot see anything else in the academy.
        </p>
        <label for="loginEmail">Email *</label>
        <input id="loginEmail" type="email" [(ngModel)]="loginModel.email" autocomplete="off" />
        <label for="loginPassword">Initial password * (at least 5 characters)</label>
        <input id="loginPassword" type="password" autocomplete="new-password" [(ngModel)]="loginModel.password" />
        <div class="helper">Share it privately. They must choose their own password at first sign-in. Reset it later from the Users screen.</div>
      </app-modal>
    }

    @if (rejectTarget(); as target) {
      <app-reason-dialog
        title="Reject session"
        [description]="rejectText(target)"
        confirmLabel="Reject"
        [busy]="voiding.busy()"
        [error]="voiding.error()"
        (cancelled)="rejectTarget.set(null)"
        (confirmed)="confirmReject(target, $event)"
      />
    }

    @if (voidSessionTarget(); as target) {
      <app-reason-dialog
        title="Void teaching time"
        [description]="target.minutes + ' min logged for ' + target.facultyName + ' stays on record, marked as voided, and no longer counts towards pay.'"
        confirmLabel="Void entry"
        [busy]="voiding.busy()"
        [error]="voiding.error()"
        (cancelled)="voidSessionTarget.set(null)"
        (confirmed)="confirmVoidSession(target, $event)"
      />
    }
  `,
})
export class FacultyComponent {
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(Notify);

  protected readonly permission = Permission;
  protected readonly methods = PAYMENT_METHODS;
  protected readonly faculty = signal<Faculty[]>([]);
  protected readonly hourly = computed(() => this.faculty().filter(f => f.payType === 'Hourly'));
  protected readonly error = signal('');
  protected readonly earned = computed(() => this.faculty().reduce((sum, f) => sum + f.earned, 0));
  protected readonly paid = computed(() => this.faculty().reduce((sum, f) => sum + f.paid, 0));
  protected readonly payouts = new PagedList<Payout>((page, pageSize) => this.api.get<Paged<Payout>>('payouts', { page, pageSize }));
  protected readonly sessions = new PagedList<FacultySession>((page, pageSize) =>
    this.api.get<Paged<FacultySession>>('faculty-sessions', { page, pageSize }),
  );
  /** Sessions faculty submitted from their portal, waiting for someone to check them. */
  protected readonly waiting = new PagedList<FacultySession>((page, pageSize) =>
    this.api.get<Paged<FacultySession>>('faculty-sessions', { page, pageSize, review: 'Pending' }),
  );
  protected readonly reviewing = signal(false);
  protected readonly rejectTarget = signal<FacultySession | null>(null);
  protected readonly login = new FormState();
  protected loginModel = { facultyId: '', name: '', email: '', password: '' };

  protected readonly form = new FormState();
  protected model = this.blank();
  protected readonly payout = new FormState();
  protected payoutModel = { facultyId: '', amount: 0, method: 'Bank Transfer', paidOn: today(), notes: '' };
  protected readonly session = new FormState();
  protected sessionModel = { facultyId: '', subjectId: '', minutes: null as number | null, taughtOn: today(), topic: '' };
  protected readonly subjects = signal<SubjectRef[]>([]);
  protected readonly voidTarget = signal<Payout | null>(null);
  protected readonly voidSessionTarget = signal<FacultySession | null>(null);
  protected readonly voiding = new FormState();

  constructor() {
    this.load();
  }

  protected can(permission: string): boolean {
    return this.auth.can(permission);
  }

  private load(): void {
    this.api.get<Faculty[]>('faculty').subscribe({ next: f => this.faculty.set(f), error: e => this.error.set(errorMessage(e)) });
    if (this.can(Permission.PayoutsRead)) {
      this.payouts.load();
      this.sessions.load();
    }
    if (this.can(Permission.PayoutsWrite)) this.waiting.load();
  }

  private blank() {
    return {
      id: '', fullName: '', email: '', phone: '', specialization: '',
      payType: 'Package' as FacultyPayType, agreedPayout: 0, hourlyRate: 0, active: true, subjectIds: [] as string[],
    };
  }

  protected subjectNames(f: Faculty): string {
    return f.subjects.map(s => s.name).join(', ');
  }

  protected sessionLabel(s: FacultySession): string {
    return [s.subjectName, s.topic].filter(Boolean).join(' — ');
  }

  protected timeLabel(s: FacultySession): string {
    return s.startTime ? `${clock(s.startTime)} – ${clock(s.endTime)} · ${s.minutes} min` : `${s.minutes} min`;
  }

  /** How the faculty member proved they were at the academy. */
  protected proofLabel(s: FacultySession): string {
    const c = s.clock;
    if (!c) return s.reviewNote;
    if (c.inMethod === 'Manual') return 'Manual request — forgot to clock in';
    const part = (method: string | null, distance: number | null) =>
      method === 'Qr' ? 'QR' : method === 'Location' ? `location (${distance ?? '?'} m away)` : method === 'Staff' ? 'stopped by admin'
        : method === 'Auto' ? 'not clocked out (closed at midnight)' : '—';
    return `In: ${part(c.inMethod, c.inDistanceMeters)} · Out: ${part(c.outMethod, c.outDistanceMeters)}`;
  }

  protected sessionStatus(s: FacultySession): string {
    if (s.status === 'Voided') return 'Voided';
    return s.review === 'Pending' ? 'Waiting approval' : s.review === 'Rejected' ? 'Rejected' : 'Approved';
  }

  // ---- Portal sessions and logins ----

  protected approve(s: FacultySession): void {
    this.reviewing.set(true);
    this.api.post(`faculty-sessions/${s.id}/approve`, {}).subscribe({
      next: () => {
        this.reviewing.set(false);
        this.notify.success(`${s.minutes} min for ${s.facultyName} approved`);
        this.load();
      },
      error: e => {
        this.reviewing.set(false);
        this.error.set(errorMessage(e));
        this.load();
      },
    });
  }

  protected rejectText(s: FacultySession): string {
    return `${s.minutes} min from ${s.facultyName} will not count towards pay. They will see your reason in their portal.`;
  }

  protected confirmReject(target: FacultySession, reason: string): void {
    this.voiding.submit(this.api.post(`faculty-sessions/${target.id}/reject`, { reason }), () => {
      this.rejectTarget.set(null);
      this.notify.success('Session rejected');
      this.load();
    });
  }

  protected openLogin(f: Faculty): void {
    this.loginModel = { facultyId: f.id, name: f.fullName, email: f.email, password: '' };
    this.login.show();
  }

  protected saveLogin(): void {
    const { facultyId, email, password } = this.loginModel;
    if (!email.trim()) return this.login.fail('Enter the email they will sign in with.');
    if (password.length < 5) return this.login.fail('The password must contain at least 5 characters.');
    this.login.submit(this.api.post(`faculty/${facultyId}/login`, { email, password }), () => {
      this.notify.success(`Portal login created for ${this.loginModel.name}`);
      this.load();
    });
  }

  protected openForm(f?: Faculty): void {
    this.api.get<SubjectRef[]>('subjects').subscribe({ next: s => this.subjects.set(s), error: e => this.form.fail(errorMessage(e)) });
    this.model = f
      ? {
          id: f.id, fullName: f.fullName, email: f.email, phone: f.phone, specialization: f.specialization,
          payType: f.payType, agreedPayout: f.agreedPayout, hourlyRate: f.hourlyRate, active: f.active,
          subjectIds: f.subjects.map(s => s.id),
        }
      : this.blank();
    this.form.show();
  }

  protected save(): void {
    const { id, ...body } = this.model;
    if (!body.fullName.trim()) return this.form.fail('Enter a name.');
    if (body.payType === 'Hourly' && !(Number(body.hourlyRate) > 0)) return this.form.fail('Enter the rate per hour.');
    this.form.submit(id ? this.api.put(`faculty/${id}`, body) : this.api.post('faculty', body), () => {
      this.notify.success('Faculty member saved');
      this.load();
    });
  }

  // ---- Teaching time ----

  protected sessionFaculty(): Faculty | undefined {
    return this.hourly().find(f => f.id === this.sessionModel.facultyId);
  }

  protected validMinutes(): boolean {
    const minutes = Number(this.sessionModel.minutes);
    return Number.isInteger(minutes) && minutes >= 1 && minutes <= 1440;
  }

  protected sessionAmount(): number {
    const f = this.sessionFaculty();
    return f ? sessionPay(Number(this.sessionModel.minutes), f.hourlyRate) : 0;
  }

  protected openSession(f?: Faculty): void {
    const facultyId = f?.id ?? (this.hourly().length === 1 ? this.hourly()[0].id : '');
    // Someone who teaches a single subject has it filled in.
    const own = this.hourly().find(h => h.id === facultyId)?.subjects ?? [];
    this.sessionModel = { facultyId, subjectId: own.length === 1 ? own[0].id : '', minutes: null, taughtOn: today(), topic: '' };
    this.session.show();
  }

  protected saveSession(): void {
    if (!this.sessionModel.facultyId) return this.session.fail('Select a faculty member.');
    if (!this.validMinutes()) return this.session.fail('Enter the time taught as whole minutes, e.g. 85.');
    const own = this.sessionFaculty()?.subjects ?? [];
    const subjectId = own.some(s => s.id === this.sessionModel.subjectId) ? this.sessionModel.subjectId : null;
    this.session.submit(this.api.post('faculty-sessions', { ...this.sessionModel, subjectId, minutes: Number(this.sessionModel.minutes) }), () => {
      this.notify.success(`${this.sessionModel.minutes} min logged`);
      this.load();
    });
  }

  protected confirmVoidSession(target: FacultySession, reason: string): void {
    this.voiding.submit(this.api.post(`faculty-sessions/${target.id}/void`, { reason }), () => {
      this.voidSessionTarget.set(null);
      this.notify.success('Teaching time voided');
      this.load();
    });
  }

  // ---- Payouts ----

  protected openPayout(f?: Faculty): void {
    this.payoutModel = { facultyId: f?.id ?? '', amount: f ? f.earned - f.paid : 0, method: 'Bank Transfer', paidOn: today(), notes: '' };
    this.payout.show();
  }

  protected savePayout(): void {
    if (!this.payoutModel.facultyId) return this.payout.fail('Select a faculty member.');
    if (!(Number(this.payoutModel.amount) > 0)) return this.payout.fail('Enter an amount greater than zero.');
    this.payout.submit(this.api.post('payouts', this.payoutModel), () => {
      this.notify.success('Payout recorded');
      this.load();
    });
  }

  protected confirmVoid(target: Payout, reason: string): void {
    this.voiding.submit(this.api.post(`payouts/${target.id}/void`, { reason }), () => {
      this.voidTarget.set(null);
      this.notify.success('Payout voided');
      this.load();
    });
  }
}
