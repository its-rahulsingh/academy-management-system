import { Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AcademicYearService } from '../../core/academic-year.service';
import { Api, FormState, Notify, PagedList, errorMessage } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { Course, Paged, Permission, Student, StudentDetail, StudentLookup } from '../../core/models';
import { UI, today } from '../../shared/ui';
import { PaymentDialogComponent, PaymentSaved } from './payment-dialog';

interface VoidTarget {
  kind: 'payment' | 'adjustment';
  id: string;
  label: string;
}

@Component({
  selector: 'app-students',
  imports: [RouterLink, UI, PaymentDialogComponent],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">ACADEMY WORKSPACE</div>
        <h1>Students</h1>
        <p>Admissions, fee position and collection.</p>
      </div>
      <div class="page-actions">
        @if (can(permission.RemindersSend)) {
          <button type="button" class="secondary" [disabled]="reminding()" (click)="remindAll()">✉ Remind all with dues</button>
        }
        @if (can(permission.StudentsWrite)) {
          <button type="button" class="primary" [disabled]="years.isClosed()" [title]="years.isClosed() ? 'This academic year has ended' : ''" (click)="openForm()">
            + Add student
          </button>
        }
      </div>
    </div>

    @if (list.error() || pageError()) {
      <div class="alert" role="alert">{{ list.error() || pageError() }}</div>
    }

    <div class="filter-bar">
      <input placeholder="⌕  Search name, phone, email, course…" [ngModel]="search()" (ngModelChange)="onSearch($event)" />
      <select [ngModel]="status()" (ngModelChange)="status.set($event); list.reload()" aria-label="Fee status">
        <option>All</option><option>Paid</option><option>Partial</option><option>Pending</option>
      </select>
      <select [ngModel]="courseId()" (ngModelChange)="courseId.set($event); list.reload()" aria-label="Course">
        <option value="">All courses</option>
        @for (course of courses(); track course.id) {
          <option [value]="course.id">{{ course.name }}</option>
        }
      </select>
    </div>

    <div class="surface table-scroll">
      <table>
        <thead>
          <tr><th>STUDENT</th><th>COURSE</th><th>PAYABLE</th><th>PAID</th><th>PENDING</th><th>STATUS</th><th></th></tr>
        </thead>
        <tbody>
          @for (s of list.items(); track s.id) {
            <tr [class.dimmed]="!s.active">
              <td><strong>{{ s.fullName }}</strong><small>{{ s.phone || s.email || '—' }}{{ s.active ? '' : ' · inactive' }}</small></td>
              <td>{{ s.courseName }}</td>
              <td>{{ s.payable | money }}</td>
              <td class="positive">{{ s.paid | money }}</td>
              <td class="negative">{{ s.pending | money }}</td>
              <td><app-status [value]="s.status" /></td>
              <td class="row-actions">
                @if (s.pending > 0 && can(permission.PaymentsWrite)) {
                  <button type="button" class="table-action" (click)="collect(s)">Collect</button>
                }
                <button type="button" class="table-action" (click)="openDetail(s.id)">Open →</button>
              </td>
            </tr>
          } @empty {
            <tr><td colspan="7" class="empty">{{ list.loading() ? 'Loading…' : 'No students found.' }}</td></tr>
          }
        </tbody>
      </table>
      <app-pager [page]="list.page()" [total]="list.total()" (pageChange)="list.load($event)" />
    </div>

    @if (form.open()) {
      <app-modal [title]="model.id ? 'Edit student' : 'Add student to ' + (years.selected()?.name ?? '')" [busy]="form.busy()" [error]="form.error()" (closed)="form.close()" (saved)="save()">
        <label for="fullName">Full name *</label>
        <input id="fullName" [(ngModel)]="model.fullName" maxlength="150" />
        <div class="two-fields">
          <div><label for="email">Email</label><input id="email" type="email" [(ngModel)]="model.email" /></div>
          <div><label for="phone">Phone</label><input id="phone" [(ngModel)]="model.phone" maxlength="32" /></div>
        </div>
        <div class="two-fields">
          <div>
            <label for="course">Course *</label>
            <select id="course" [(ngModel)]="model.courseId" (ngModelChange)="courseChanged()" [disabled]="feeLocked()">
              <option value="" disabled>Select course</option>
              @for (course of courses(); track course.id) {
                @if (course.active || course.id === model.courseId) {
                  <option [value]="course.id">{{ course.name }}</option>
                }
              }
            </select>
          </div>
          <div>
            <label for="fee">Agreed fee (₹) *</label>
            <input id="fee" type="number" min="0" step="0.01" [(ngModel)]="model.feeAmount" [disabled]="feeLocked()" />
          </div>
        </div>
        @if (feeLocked()) {
          <div class="helper">Fee and course are locked because payments exist. Use a discount or surcharge to change what is owed.</div>
        }
        <label for="admission">Admission date</label>
        <input id="admission" type="date" [(ngModel)]="model.admissionDate" />
        <label for="remarks">Remarks</label>
        <textarea id="remarks" rows="2" maxlength="1200" [(ngModel)]="model.remarks"></textarea>
      </app-modal>
    }

    @if (login.open()) {
      <app-modal title="Give student portal login" saveLabel="Create login" [busy]="login.busy()" [error]="login.error()" (closed)="login.close()" (saved)="saveLogin()">
        <p class="helper">
          {{ loginModel.name }} will sign in with your academy code and this email to see their own fees, receipts and test results.
          They cannot see anything else.
        </p>
        <label for="studentLoginEmail">Email *</label>
        <input id="studentLoginEmail" type="email" [(ngModel)]="loginModel.email" autocomplete="off" />
        <label for="studentLoginPassword">Initial password * (at least 5 characters)</label>
        <input id="studentLoginPassword" type="password" autocomplete="new-password" [(ngModel)]="loginModel.password" />
        <div class="helper">Share it with the student or parent. They choose their own password at first sign-in.</div>
      </app-modal>
    }

    @if (detail(); as d) {
      <app-modal [title]="d.student.fullName" saveLabel="" [wide]="true" [error]="detailError()" (closed)="closeDetail()">
        <div class="mini-stats">
          <div><small>AGREED FEE</small><b>{{ d.student.feeAmount | money }}</b></div>
          <div><small>ADJUSTMENTS</small><b>{{ d.student.surcharges - d.student.discounts | money }}</b></div>
          <div><small>PAID</small><b class="positive">{{ d.student.paid | money }}</b></div>
          <div><small>PENDING</small><b class="negative">{{ d.student.pending | money }}</b></div>
        </div>
        <p class="helper">
          {{ d.student.courseName }} · {{ d.student.academicYearName }} · admitted {{ d.student.admissionDate | day }}
          @if (d.student.phone) { · {{ d.student.phone }} }
          @if (d.student.email) { · {{ d.student.email }} }
          @if (d.login) { · Portal login: {{ d.login.email }}{{ d.login.active ? '' : ' (disabled)' }} }
        </p>

        <div class="button-row">
          @if (can(permission.PaymentsWrite) && d.student.pending > 0) {
            <button type="button" class="primary" (click)="collect(d.student)">Collect fee</button>
          }
          @if (can(permission.LedgerCorrect)) {
            <button type="button" class="secondary" (click)="openAdjustment('Discount')">Give discount</button>
            <button type="button" class="secondary" (click)="openAdjustment('Surcharge')">Add surcharge</button>
            @if (d.student.paid > 0) {
              <button type="button" class="secondary" (click)="refund(d.student)">Refund</button>
            }
          }
          @if (can(permission.RemindersSend) && d.student.pending > 0 && d.student.email) {
            <button type="button" class="secondary" (click)="remind(d.student)">✉ Send reminder</button>
          }
          @if (can(permission.StudentsWrite)) {
            <button type="button" class="secondary" (click)="openForm(d.student)">Edit details</button>
          }
          @if (can(permission.UsersManage) && !d.login) {
            <button type="button" class="secondary" (click)="openLogin(d.student)">Give portal login</button>
          }
          @if (can(permission.TestsRead)) {
            <a class="secondary" [routerLink]="['/app/tests/report-cards', d.student.id]">Report card</a>
            <button type="button" class="secondary" (click)="setActive(d.student, !d.student.active)">
              {{ d.student.active ? 'Mark inactive' : 'Mark active' }}
            </button>
          }
        </div>

        <h3 class="section-title">Payments and refunds</h3>
        <div class="table-scroll">
          <table class="compact">
            <thead><tr><th>RECEIPT</th><th>DATE</th><th>METHOD</th><th>AMOUNT</th><th></th></tr></thead>
            <tbody>
              @for (p of d.payments; track p.id) {
                <tr [class.dimmed]="p.status === 'Voided'">
                  <td><strong>{{ p.receipt }}</strong><small>{{ p.status === 'Voided' ? 'Voided: ' + p.voidReason : p.notes }}</small></td>
                  <td>{{ p.paidOn | day }}</td>
                  <td>{{ p.method }}</td>
                  <td [class.positive]="p.kind === 'Payment'" [class.negative]="p.kind === 'Refund'">
                    {{ p.kind === 'Refund' ? '−' : '' }}{{ p.amount | money: true }}
                  </td>
                  <td class="row-actions">
                    <a class="table-action" [routerLink]="['/app/payments', p.id, 'receipt']">Receipt</a>
                    @if (p.status === 'Posted' && can(permission.LedgerCorrect)) {
                      <button type="button" class="table-action danger-text" (click)="voidTarget.set({ kind: 'payment', id: p.id, label: p.receipt })">Void</button>
                    }
                  </td>
                </tr>
              } @empty {
                <tr><td colspan="5" class="empty">Nothing recorded yet.</td></tr>
              }
            </tbody>
          </table>
        </div>

        @if (d.adjustments.length) {
          <h3 class="section-title">Discounts and surcharges</h3>
          <div class="table-scroll">
            <table class="compact">
              <thead><tr><th>TYPE</th><th>REASON</th><th>AMOUNT</th><th></th></tr></thead>
              <tbody>
                @for (a of d.adjustments; track a.id) {
                  <tr [class.dimmed]="a.status === 'Voided'">
                    <td><strong>{{ a.kind }}</strong><small>{{ a.createdAtUtc | day }}</small></td>
                    <td>{{ a.status === 'Voided' ? 'Voided: ' + a.voidReason : a.reason }}</td>
                    <td>{{ a.amount | money: true }}</td>
                    <td class="row-actions">
                      @if (a.status === 'Posted' && can(permission.LedgerCorrect)) {
                        <button type="button" class="table-action danger-text" (click)="voidTarget.set({ kind: 'adjustment', id: a.id, label: a.kind })">Void</button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </app-modal>
    }

    @if (adjustment.open()) {
      <app-modal [title]="adjustmentModel.kind === 'Discount' ? 'Give discount' : 'Add surcharge'" [busy]="adjustment.busy()" [error]="adjustment.error()" (closed)="adjustment.close()" (saved)="saveAdjustment()">
        <label for="adjAmount">Amount (₹) *</label>
        <input id="adjAmount" type="number" min="0.01" step="0.01" [(ngModel)]="adjustmentModel.amount" />
        <label for="adjReason">Reason *</label>
        <textarea id="adjReason" rows="2" maxlength="500" [(ngModel)]="adjustmentModel.reason" placeholder="e.g. Sibling scholarship"></textarea>
      </app-modal>
    }

    @if (paying(); as p) {
      <app-payment-dialog [student]="p.student" [refund]="p.refund" (closed)="paying.set(null)" (saved)="paid($event, p.refund)" />
    }

    @if (voidTarget(); as target) {
      <app-reason-dialog
        [title]="'Void ' + target.label"
        description="The entry stays on record, marked as voided, and no longer counts towards the balance."
        confirmLabel="Void entry"
        [busy]="voiding.busy()"
        [error]="voiding.error()"
        (cancelled)="voidTarget.set(null)"
        (confirmed)="confirmVoid(target, $event)"
      />
    }
  `,
})
export class StudentsComponent {
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(Notify);
  protected readonly years = inject(AcademicYearService);

  /** Set by the dashboard's "Add student" shortcut (?new=1). */
  readonly openNew = input<string>(undefined, { alias: 'new' });

  protected readonly permission = Permission;
  protected readonly search = signal('');
  protected readonly status = signal('All');
  protected readonly courseId = signal('');
  protected readonly courses = signal<Course[]>([]);
  protected readonly pageError = signal('');
  protected readonly reminding = signal(false);

  protected readonly list = new PagedList<Student>((page, pageSize) =>
    this.api.get<Paged<Student>>('students', { page, pageSize, search: this.search(), status: this.status(), courseId: this.courseId() }),
  );

  protected readonly form = new FormState();
  protected readonly feeLocked = signal(false);
  protected model = this.blank();

  protected readonly detail = signal<StudentDetail | null>(null);
  protected readonly login = new FormState();
  protected loginModel = { studentId: '', name: '', email: '', password: '' };
  protected readonly detailError = signal('');
  protected readonly adjustment = new FormState();
  protected adjustmentModel = { kind: 'Discount' as 'Discount' | 'Surcharge', amount: 0, reason: '' };
  protected readonly paying = signal<{ student: StudentLookup; refund: boolean } | null>(null);
  protected readonly voidTarget = signal<VoidTarget | null>(null);
  protected readonly voiding = new FormState();
  private searchTimer?: ReturnType<typeof setTimeout>;

  constructor() {
    this.list.load();
    this.api.get<Course[]>('courses').subscribe({ next: courses => this.courses.set(courses), error: e => this.pageError.set(errorMessage(e)) });
    effect(() => {
      if (this.openNew() && this.can(Permission.StudentsWrite) && !this.years.isClosed()) this.openForm();
    });
  }

  protected can(permission: string): boolean {
    return this.auth.can(permission);
  }

  protected onSearch(text: string): void {
    this.search.set(text);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.list.reload(), 300);
  }

  // ---- Add / edit ----

  private blank() {
    return { id: '', fullName: '', email: '', phone: '', courseId: '', feeAmount: 0, admissionDate: today(), remarks: '' };
  }

  protected openForm(student?: Student): void {
    this.model = student
      ? { id: student.id, fullName: student.fullName, email: student.email, phone: student.phone, courseId: student.courseId, feeAmount: student.feeAmount, admissionDate: student.admissionDate, remarks: student.remarks }
      : this.blank();
    this.feeLocked.set(!!student && student.paid > 0);
    this.form.show();
  }

  protected courseChanged(): void {
    const course = this.courses().find(c => c.id === this.model.courseId);
    if (course && !this.model.id) this.model.feeAmount = course.fee;
  }

  protected save(): void {
    const { id, ...body } = this.model;
    if (!body.fullName.trim()) return this.form.fail("Enter the student's name.");
    if (!body.courseId) return this.form.fail('Select a course.');

    this.form.submit(id ? this.api.put(`students/${id}`, body) : this.api.post('students', body), () => {
      this.notify.success('Student saved');
      this.list.load();
      if (id) this.openDetail(id);
    });
  }

  // ---- Detail and ledger ----

  protected openDetail(id: string): void {
    this.detailError.set('');
    this.api.get<StudentDetail>(`students/${id}`).subscribe({
      next: detail => this.detail.set(detail),
      error: e => this.pageError.set(errorMessage(e)),
    });
  }

  protected openLogin(student: Student): void {
    this.loginModel = { studentId: student.id, name: student.fullName, email: student.email, password: '' };
    this.login.show();
  }

  protected saveLogin(): void {
    const { studentId, name, email, password } = this.loginModel;
    if (!email.trim()) return this.login.fail('Enter the email the student will sign in with.');
    if (password.length < 5) return this.login.fail('The password must contain at least 5 characters.');
    this.login.submit(this.api.post(`students/${studentId}/login`, { email, password }), () => {
      this.notify.success(`Portal login created for ${name}`);
      this.openDetail(studentId);
    });
  }

  protected closeDetail(): void {
    this.detail.set(null);
  }

  /** Reloads whatever is on screen after the ledger changed. */
  private refresh(): void {
    this.list.load();
    const open = this.detail();
    if (open) this.openDetail(open.student.id);
  }

  protected collect(student: Student): void {
    this.paying.set({ student: this.asLookup(student), refund: false });
  }

  protected refund(student: Student): void {
    this.paying.set({ student: this.asLookup(student), refund: true });
  }

  private asLookup(s: Student): StudentLookup {
    return { id: s.id, fullName: s.fullName, courseName: s.courseName, pending: s.pending, paid: s.paid };
  }

  protected paid(saved: PaymentSaved, refund: boolean): void {
    this.paying.set(null);
    this.notify.success(`${refund ? 'Refund' : 'Payment'} recorded — receipt ${saved.receipt}`);
    this.refresh();
  }

  protected openAdjustment(kind: 'Discount' | 'Surcharge'): void {
    this.adjustmentModel = { kind, amount: 0, reason: '' };
    this.adjustment.show();
  }

  protected saveAdjustment(): void {
    const student = this.detail()?.student;
    if (!student) return;
    if (!(Number(this.adjustmentModel.amount) > 0)) return this.adjustment.fail('Enter an amount greater than zero.');
    if (this.adjustmentModel.reason.trim().length < 3) return this.adjustment.fail('Give a reason.');

    this.adjustment.submit(this.api.post(`students/${student.id}/adjustments`, this.adjustmentModel), () => {
      this.notify.success(`${this.adjustmentModel.kind} recorded`);
      this.refresh();
    });
  }

  protected confirmVoid(target: VoidTarget, reason: string): void {
    const path = target.kind === 'payment' ? `payments/${target.id}/void` : `students/adjustments/${target.id}/void`;
    this.voiding.submit(this.api.post(path, { reason }), () => {
      this.voidTarget.set(null);
      this.notify.success(`${target.label} voided`);
      this.refresh();
    });
  }

  protected setActive(student: Student, active: boolean): void {
    this.api.put(`students/${student.id}/active`, { active }).subscribe({
      next: () => {
        this.notify.success(active ? 'Student marked active' : 'Student marked inactive');
        this.refresh();
      },
      error: e => this.detailError.set(errorMessage(e)),
    });
  }

  // ---- Reminders ----

  protected remind(student: Student): void {
    this.api.post(`students/${student.id}/reminder`).subscribe({
      next: () => this.notify.success(`Reminder queued for ${student.email}`),
      error: e => this.detailError.set(errorMessage(e)),
    });
  }

  protected remindAll(): void {
    if (!confirm('Email a fee reminder to every active student who has dues and an email address?')) return;
    this.reminding.set(true);
    this.api.post<{ queued: number }>('students/reminders').subscribe({
      next: result => {
        this.reminding.set(false);
        this.notify.success(result.queued ? `${result.queued} reminder(s) queued` : 'No students with dues have an email address');
      },
      error: e => {
        this.reminding.set(false);
        this.pageError.set(errorMessage(e));
      },
    });
  }
}
