import { Component, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Api, Notify, errorMessage } from '../../core/api';
import { ClockResult, ClockStatus } from '../../core/models';

/**
 * Where the admin's QR code leads when it is scanned with the phone's own camera app instead of the portal's scanner:
 * clocks the faculty member in, or out if a class is running, then returns to the dashboard.
 */
@Component({
  selector: 'app-teacher-scan',
  imports: [RouterLink],
  template: `
    <section class="teacher-panel scan-result">
      @if (error()) {
        <div class="teacher-empty">
          <div class="art" aria-hidden="true">!</div>
          <strong>Could not clock you {{ direction() }}</strong>
          <p>{{ error() }}</p>
          <p><a class="primary" routerLink="/teacher">Back to dashboard</a></p>
        </div>
      } @else {
        <div class="clock-wait">
          <div class="spinner" aria-hidden="true"></div>
          <p>Checking the code…</p>
        </div>
      }
    </section>
  `,
})
export class TeacherScanComponent {
  private readonly api = inject(Api);
  private readonly notify = inject(Notify);
  private readonly router = inject(Router);
  /** From the link in the QR code. */
  readonly code = input('');
  protected readonly error = signal('');
  protected readonly direction = signal<'in' | 'out'>('in');

  constructor() {
    queueMicrotask(() => void this.run());
  }

  private async run(): Promise<void> {
    if (!this.code()) {
      this.error.set('The link did not contain a check-in code. Scan the code on the admin’s screen again.');
      return;
    }
    try {
      const status = await firstValueFrom(this.api.get<ClockStatus>('me/faculty/clock'));
      const mode = status.running ? 'out' : 'in';
      this.direction.set(mode);
      const result = await firstValueFrom(this.api.post<ClockResult>(`me/faculty/clock/${mode}`, { method: 'Qr', code: this.code() }));
      this.notify.success(
        mode === 'in'
          ? 'Clocked in — your class timer has started'
          : result.cancelled
            ? 'Clock-in cancelled: the class was under a minute'
            : `Clocked out — ${result.minutes} min ${result.review === 'Approved' ? 'recorded and approved' : 'sent for approval'}`,
      );
      await this.router.navigateByUrl('/teacher', { replaceUrl: true });
    } catch (e) {
      this.error.set(errorMessage(e));
    }
  }
}
