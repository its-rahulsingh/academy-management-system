import { Component, computed, inject, signal } from '@angular/core';
import QRCode from 'qrcode';
import { AcademicYearService } from '../../core/academic-year.service';
import { Api, FormState, Notify, errorMessage } from '../../core/api';
import { AdmissionApplication, AdmissionQueue, AdmissionSettings, AdmissionStatus, Course } from '../../core/models';
import { UI, today } from '../../shared/ui';

const TABS: AdmissionStatus[] = ['Pending', 'Approved', 'Rejected'];

/**
 * Online admissions: the public form's link and QR code, and the queue of applications to review.
 * An approved application becomes a student in the selected academic year.
 */
@Component({
  selector: 'app-admissions',
  imports: [UI],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">ACADEMY WORKSPACE</div>
        <h1>Online admissions</h1>
        <p>Share the form link or QR code. Applications wait here until you approve them.</p>
      </div>
    </div>

    @if (error()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }

    @if (settings(); as s) {
      <section class="surface share-card">
        <div class="share-qr">
          @if (qr()) {
            <img [src]="qr()" alt="QR code that opens the admission form" width="168" height="168" />
          }
        </div>
        <div class="share-body">
          <h2>Admission form</h2>
          <p class="muted">Anyone with this link can apply — no sign-in needed. Print the QR code for your notice board or front desk.</p>
          <div class="link-box">
            <input [value]="link()" readonly aria-label="Admission form link" (focus)="$any($event.target).select()" />
            <button type="button" class="secondary" (click)="copy()">{{ copied() ? 'Copied ✓' : 'Copy link' }}</button>
          </div>
          <div class="button-row tight">
            <a class="secondary button-link" [href]="link()" target="_blank" rel="noopener">Open form ↗</a>
            @if (qr()) {
              <a class="secondary button-link" [href]="qr()" [download]="'admission-qr-' + s.code + '.png'">Download QR code</a>
            }
          </div>
          <label class="check toggle-line">
            <input type="checkbox" [ngModel]="s.open" (ngModelChange)="setOpen($event)" [disabled]="savingOpen()" />
            {{ s.open ? 'Accepting applications' : 'Form is closed — visitors see that admissions are closed' }}
          </label>
        </div>
      </section>
    }

    <section class="surface">
      <div class="tabs" role="tablist">
        @for (tab of tabs; track tab) {
          <button type="button" role="tab" [class.active]="status() === tab" [attr.aria-selected]="status() === tab" (click)="show(tab)">
            {{ tab }} <span class="count">{{ queue()?.counts?.[tab] ?? 0 }}</span>
          </button>
        }
      </div>
      <div class="table-scroll">
        <table>
          <thead>
            <tr><th>APPLICANT</th><th>COURSE</th><th>DETAILS</th><th>{{ status() === 'Pending' ? 'RECEIVED' : 'REVIEWED' }}</th><th></th></tr>
          </thead>
          <tbody>
            @for (a of queue()?.page?.items ?? []; track a.id) {
              <tr>
                <td><strong>{{ a.fullName }}</strong><small>{{ a.phone }}{{ a.email ? ' · ' + a.email : '' }}</small></td>
                <td>{{ a.courseName }}<small>{{ a.courseFee | money }}</small></td>
                <td class="details-cell">
                  @if (a.guardianName) { <small>Guardian: {{ a.guardianName }}</small> }
                  @if (a.address) { <small>{{ a.address }}</small> }
                  @if (a.message) { <small>“{{ a.message }}”</small> }
                  @if (a.status === 'Rejected') { <small class="negative">Rejected: {{ a.reviewNote }}</small> }
                  @if (!a.guardianName && !a.address && !a.message && a.status !== 'Rejected') { — }
                </td>
                <td>{{ (a.status === 'Pending' ? a.createdAtUtc : a.reviewedAtUtc) | day }}</td>
                <td class="row-actions">
                  @if (a.status === 'Pending') {
                    <button type="button" class="table-action" (click)="openApprove(a)">Approve</button>
                    <button type="button" class="table-action danger-text" (click)="rejecting.set(a)">Reject</button>
                  }
                </td>
              </tr>
            } @empty {
              <tr><td colspan="5" class="empty">{{ emptyText() }}</td></tr>
            }
          </tbody>
        </table>
      </div>
      <app-pager [page]="page()" [total]="queue()?.page?.total ?? 0" (pageChange)="load($event)" />
    </section>

    @if (approve.open() && approving(); as a) {
      <app-modal [title]="'Approve ' + a.fullName" saveLabel="Admit student" [busy]="approve.busy()" [error]="approve.error()" (closed)="approve.close()" (saved)="saveApprove(a)">
        <p class="helper">
          Creates the student in academic year <strong>{{ years.selected()?.name }}</strong>.
          @if (years.isClosed()) { That year has ended — switch to the current year first. }
        </p>
        <label for="course">Course *</label>
        <select id="course" [(ngModel)]="approveModel.courseId" (ngModelChange)="courseChanged()">
          @for (c of activeCourses(); track c.id) {
            <option [value]="c.id">{{ c.name }} — {{ c.fee | money }}</option>
          }
        </select>
        <div class="two-fields">
          <div><label for="fee">Fee agreed (₹) *</label><input id="fee" type="number" min="0" step="0.01" [(ngModel)]="approveModel.feeAmount" /></div>
          <div><label for="admitted">Admission date</label><input id="admitted" type="date" [(ngModel)]="approveModel.admissionDate" /></div>
        </div>
        <p class="helper">Guardian, address and the applicant's note are copied into the student's remarks.</p>
      </app-modal>
    }

    @if (rejecting(); as a) {
      <app-reason-dialog
        [title]="'Reject ' + a.fullName"
        description="The application is kept on the Rejected tab with your reason. The applicant is not notified automatically."
        confirmLabel="Reject application"
        [busy]="reject.busy()"
        [error]="reject.error()"
        (cancelled)="rejecting.set(null)"
        (confirmed)="saveReject(a, $event)"
      />
    }
  `,
})
export class AdmissionsComponent {
  private readonly api = inject(Api);
  private readonly notify = inject(Notify);
  protected readonly years = inject(AcademicYearService);

  protected readonly tabs = TABS;
  protected readonly settings = signal<AdmissionSettings | null>(null);
  protected readonly queue = signal<AdmissionQueue | null>(null);
  protected readonly status = signal<AdmissionStatus>('Pending');
  protected readonly page = signal(1);
  protected readonly error = signal('');
  protected readonly qr = signal('');
  protected readonly copied = signal(false);
  protected readonly savingOpen = signal(false);
  protected readonly courses = signal<Course[]>([]);
  protected readonly activeCourses = computed(() => this.courses().filter(c => c.active));

  protected readonly approve = new FormState();
  protected readonly approving = signal<AdmissionApplication | null>(null);
  protected approveModel = { courseId: '', feeAmount: 0, admissionDate: today() };
  protected readonly reject = new FormState();
  protected readonly rejecting = signal<AdmissionApplication | null>(null);

  protected readonly link = computed(() => {
    const s = this.settings();
    return s ? `${window.location.origin}${s.path}` : '';
  });
  protected readonly emptyText = computed(() =>
    this.status() === 'Pending' ? 'No applications waiting. Share the link or QR code to receive some.' : `No ${this.status().toLowerCase()} applications.`,
  );

  constructor() {
    this.api.get<AdmissionSettings>('admissions/settings').subscribe({
      next: s => {
        this.settings.set(s);
        void this.renderQr();
      },
      error: e => this.error.set(errorMessage(e)),
    });
    this.api.get<Course[]>('courses').subscribe({ next: c => this.courses.set(c), error: e => this.error.set(errorMessage(e)) });
    this.load();
  }

  protected load(page = this.page()): void {
    this.api.get<AdmissionQueue>('admissions', { status: this.status(), page, pageSize: 25 }).subscribe({
      next: q => {
        this.queue.set(q);
        this.page.set(q.page.page);
      },
      error: e => this.error.set(errorMessage(e)),
    });
  }

  protected show(tab: AdmissionStatus): void {
    this.status.set(tab);
    this.load(1);
  }

  private async renderQr(): Promise<void> {
    try {
      // Error correction "M" survives a smudged or partly covered print; the margin keeps phones' scanners happy.
      this.qr.set(await QRCode.toDataURL(this.link(), { errorCorrectionLevel: 'M', margin: 2, width: 512 }));
    } catch {
      this.qr.set('');
    }
  }

  protected async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.link());
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    } catch {
      this.error.set('Copy did not work in this browser. Select the link and copy it by hand.');
    }
  }

  protected setOpen(open: boolean): void {
    this.savingOpen.set(true);
    this.api.put<{ open: boolean }>('admissions/settings', { open }).subscribe({
      next: r => {
        this.savingOpen.set(false);
        this.settings.update(s => (s ? { ...s, open: r.open } : s));
        this.notify.success(r.open ? 'Admission form opened' : 'Admission form closed');
      },
      error: e => {
        this.savingOpen.set(false);
        this.error.set(errorMessage(e));
      },
    });
  }

  protected openApprove(a: AdmissionApplication): void {
    const course = this.activeCourses().find(c => c.id === a.courseId) ?? this.activeCourses()[0];
    this.approveModel = { courseId: course?.id ?? '', feeAmount: course?.fee ?? 0, admissionDate: today() };
    this.approving.set(a);
    this.approve.show();
  }

  protected courseChanged(): void {
    const course = this.courses().find(c => c.id === this.approveModel.courseId);
    if (course) this.approveModel.feeAmount = course.fee;
  }

  protected saveApprove(a: AdmissionApplication): void {
    if (!this.approveModel.courseId) return this.approve.fail('Select a course.');
    if (this.years.isClosed()) return this.approve.fail('This academic year has ended. Switch to the current year to admit students.');
    this.approve.submit(this.api.post(`admissions/${a.id}/approve`, this.approveModel), () => {
      this.approving.set(null);
      this.notify.success(`${a.fullName} admitted as a student`);
      this.load();
    });
  }

  protected saveReject(a: AdmissionApplication, reason: string): void {
    this.reject.submit(this.api.post(`admissions/${a.id}/reject`, { reason }), () => {
      this.rejecting.set(null);
      this.notify.success('Application rejected');
      this.load();
    });
  }
}
