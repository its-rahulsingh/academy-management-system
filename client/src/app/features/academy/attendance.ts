import { Component, DestroyRef, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import QRCode from 'qrcode';
import { Api, errorMessage } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { AttendanceSettings, CheckInQr, Permission } from '../../core/models';
import { UI } from '../../shared/ui';
import { LiveClassesComponent } from './live-classes';

/**
 * The admin's check-in desk: a QR code that changes every few seconds, for faculty to scan when they clock in or out,
 * and the classes running right now.
 */
@Component({
  selector: 'app-attendance',
  imports: [UI, RouterLink, LiveClassesComponent],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">ACADEMY WORKSPACE</div>
        <h1>Class check-in</h1>
        <p>Faculty scan this code to clock in when a class starts and to clock out when it ends. Classes clocked in and out with it are approved automatically.</p>
      </div>
    </div>

    @if (error()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }

    <div class="checkin-grid">
      <section class="surface checkin-qr" #qrPanel>
        <div class="surface-title">
          <h2>Scan to clock in or out</h2>
          <button type="button" class="secondary" (click)="fullscreen()">⛶ Full screen</button>
        </div>
        <div class="qr-frame">
          @if (image()) {
            <img [src]="image()" alt="Check-in QR code" width="320" height="320" />
          } @else {
            <div class="qr-placeholder">Loading…</div>
          }
        </div>
        <div class="qr-countdown" aria-hidden="true"><span [style.width.%]="progress()"></span></div>
        <p class="muted">New code in {{ secondsLeft() }} s · each code works for one minute, so a photo of it is no use later.</p>
        <ol class="qr-steps">
          <li>Faculty open their portal and tap <b>Clock in</b> (or <b>Clock out</b>).</li>
          <li>They choose <b>Scan the admin's QR code</b> and point the camera here.</li>
          <li>Their phone's own camera app works too — it opens the portal and clocks them in.</li>
        </ol>
      </section>

      <div>
        <app-live-classes [canStop]="true" />
        <section class="surface gap-top">
          <div class="surface-title"><h2>When no admin is free</h2></div>
          @if (settings(); as s) {
            @if (s.enabled) {
              <p class="muted">Faculty can clock in by location within <b>{{ s.radiusMeters }} m</b> of the academy. Those classes wait for approval on the Faculty page.</p>
            } @else {
              <p class="muted">Location clock-in is off, so faculty can only clock in with this QR code.</p>
            }
            @if (canSetLocation) {
              <a class="text-button" routerLink="/app/profile">Set the academy's location →</a>
            }
          }
        </section>
      </div>
    </div>
  `,
})
export class AttendanceComponent {
  private readonly api = inject(Api);
  private readonly panel = viewChild<ElementRef<HTMLElement>>('qrPanel');
  protected readonly canSetLocation = inject(AuthService).can(Permission.BrandingManage);
  protected readonly image = signal('');
  protected readonly error = signal('');
  protected readonly settings = signal<AttendanceSettings | null>(null);
  private readonly refreshSeconds = signal(20);
  private readonly fetchedAt = signal(Date.now());
  private readonly now = signal(Date.now());
  protected readonly secondsLeft = computed(() =>
    Math.max(0, Math.ceil(this.refreshSeconds() - (this.now() - this.fetchedAt()) / 1000)),
  );
  protected readonly progress = computed(() => (100 * this.secondsLeft()) / this.refreshSeconds());

  constructor() {
    this.loadCode();
    this.api.get<AttendanceSettings>('attendance/settings').subscribe({ next: s => this.settings.set(s) });
    const timer = setInterval(() => {
      this.now.set(Date.now());
      if (this.secondsLeft() === 0) this.loadCode();
    }, 1000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  private loading = false;

  private loadCode(): void {
    if (this.loading) return;
    this.loading = true;
    this.api.get<CheckInQr>('attendance/qr').subscribe({
      next: async qr => {
        this.loading = false;
        this.error.set('');
        this.refreshSeconds.set(qr.refreshSeconds);
        this.fetchedAt.set(Date.now());
        this.image.set(await QRCode.toDataURL(qr.url, { width: 640, margin: 1, errorCorrectionLevel: 'M' }));
      },
      error: e => {
        this.loading = false;
        this.error.set(errorMessage(e));
        // Try again shortly rather than on every tick.
        this.fetchedAt.set(Date.now() - (this.refreshSeconds() - 5) * 1000);
      },
    });
  }

  protected fullscreen(): void {
    void this.panel()?.nativeElement.requestFullscreen?.();
  }
}
