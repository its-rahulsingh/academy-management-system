import { HttpErrorResponse } from '@angular/common/http';
import { Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, errorMessage } from '../../core/api';
import { AdmissionForm } from '../../core/models';
import { BrandMarkComponent, MoneyPipe } from '../../shared/ui';

/**
 * The public admission form, opened from the academy's link or QR code. No sign-in.
 * Built for phones first: most people arrive by scanning the QR code.
 */
@Component({
  selector: 'app-apply',
  imports: [FormsModule, BrandMarkComponent, MoneyPipe],
  template: `
    <div class="apply-page">
      @if (notFound()) {
        <section class="apply-card">
          <h1>Form not found</h1>
          <p class="muted">This admission link is not valid. Check the link with the academy.</p>
        </section>
      } @else if (form(); as f) {
        <section class="apply-card">
          <header class="apply-header">
            <app-brand-mark [name]="f.academy.name" [logoUrl]="f.academy.logoUrl" />
            <div>
              <h1>{{ f.academy.name }}</h1>
              @if (f.academy.tagline) {
                <p>{{ f.academy.tagline }}</p>
              }
            </div>
          </header>

          @if (submitted()) {
            <div class="success apply-done" role="status">
              <strong>Application received.</strong>
              <span>Thank you, {{ name }}. {{ f.academy.name }} will contact you on {{ phone }} once it has been reviewed.</span>
            </div>
            <button type="button" class="secondary wide" (click)="another()">Submit another application</button>
          } @else if (!f.open) {
            <div class="alert" role="status">Online admissions are closed right now. Please contact the academy directly.</div>
          } @else {
            <h2>Admission form</h2>
            <p class="muted">Fill in the student's details. The academy will review your application and get in touch.</p>
            @if (error()) {
              <div class="alert" role="alert">{{ error() }}</div>
            }
            <form (ngSubmit)="submit()" novalidate>
              <label for="name">Student's full name *</label>
              <input id="name" name="name" [(ngModel)]="name" maxlength="150" autocomplete="name" required />

              <label for="phone">Phone number *</label>
              <input id="phone" name="phone" type="tel" [(ngModel)]="phone" maxlength="32" autocomplete="tel" inputmode="tel" required />

              <label for="email">Email</label>
              <input id="email" name="email" type="email" [(ngModel)]="email" maxlength="240" autocomplete="email" />

              <label for="course">Course *</label>
              <select id="course" name="course" [(ngModel)]="courseId" required>
                <option value="" disabled>Select a course</option>
                @for (c of f.courses; track c.id) {
                  <option [value]="c.id">{{ c.name }} — {{ c.fee | money }}</option>
                }
              </select>

              <label for="guardian">Parent or guardian's name</label>
              <input id="guardian" name="guardian" [(ngModel)]="guardianName" maxlength="150" />

              <label for="address">Address</label>
              <textarea id="address" name="address" rows="2" [(ngModel)]="address" maxlength="400" autocomplete="street-address"></textarea>

              <label for="message">Anything the academy should know?</label>
              <textarea id="message" name="message" rows="2" [(ngModel)]="message" maxlength="800" placeholder="e.g. preferred batch timing"></textarea>

              <!-- Hidden from people; automated spam fills it in and is quietly ignored. -->
              <div class="hp-field" aria-hidden="true">
                <label for="website">Website</label>
                <input id="website" name="website" [(ngModel)]="website" tabindex="-1" autocomplete="off" />
              </div>

              <button type="submit" class="primary wide" [disabled]="busy()">{{ busy() ? 'Submitting…' : 'Submit application' }}</button>
            </form>
          }
        </section>
        <p class="apply-foot">Powered by AcademyFlow</p>
      } @else if (error()) {
        <section class="apply-card"><div class="alert" role="alert">{{ error() }}</div></section>
      } @else {
        <section class="apply-card"><p class="muted">Loading…</p></section>
      }
    </div>
  `,
})
export class ApplyComponent {
  private readonly api = inject(Api);

  /** Academy code from the URL. */
  readonly code = input.required<string>();

  protected readonly form = signal<AdmissionForm | null>(null);
  protected readonly notFound = signal(false);
  protected readonly error = signal('');
  protected readonly busy = signal(false);
  protected readonly submitted = signal(false);

  protected name = '';
  protected phone = '';
  protected email = '';
  protected courseId = '';
  protected guardianName = '';
  protected address = '';
  protected message = '';
  protected website = '';

  constructor() {
    effect(() => {
      this.api.get<AdmissionForm>(`public/admissions/${encodeURIComponent(this.code())}`).subscribe({
        next: f => {
          this.form.set(f);
          if (f.courses.length === 1) this.courseId = f.courses[0].id;
        },
        error: e => (e instanceof HttpErrorResponse && e.status === 404 ? this.notFound.set(true) : this.error.set(errorMessage(e))),
      });
    });
  }

  protected submit(): void {
    if (!this.name.trim()) return this.error.set("Enter the student's full name.");
    if (this.phone.replace(/\D/g, '').length < 6) return this.error.set('Enter a phone number the academy can reach you on.');
    if (!this.courseId) return this.error.set('Select a course.');

    this.busy.set(true);
    this.error.set('');
    this.api
      .post(`public/admissions/${encodeURIComponent(this.code())}`, {
        fullName: this.name, phone: this.phone, email: this.email, courseId: this.courseId,
        guardianName: this.guardianName, address: this.address, message: this.message, website: this.website,
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.submitted.set(true);
          window.scrollTo({ top: 0 });
        },
        error: e => {
          this.busy.set(false);
          this.error.set(errorMessage(e));
        },
      });
  }

  protected another(): void {
    this.name = this.phone = this.email = this.guardianName = this.address = this.message = '';
    this.submitted.set(false);
  }
}
