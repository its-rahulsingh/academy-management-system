import { Component, inject, signal } from '@angular/core';
import { Api, Notify, errorMessage } from '../../core/api';
import { AttendanceSettings } from '../../core/models';
import { UI } from '../../shared/ui';

/**
 * Where the academy is, so faculty can clock in by location when no admin is free to show the QR code.
 * Easiest to set by pressing "Use my current location" while standing in the academy.
 */
@Component({
  selector: 'app-location-settings',
  imports: [UI],
  template: `
    <div class="surface-title"><h2>Faculty clock-in</h2></div>
    <label for="maxClass">Longest normal class</label>
    <select id="maxClass" [(ngModel)]="maxClassMinutes">
      @for (m of maxOptions; track m) {
        <option [ngValue]="m">{{ m < 60 ? m + ' min' : m / 60 + (m === 60 ? ' hour' : ' hours') }}</option>
      }
    </select>
    <div class="helper">
      A class running longer than this always needs approval (even when clocked by QR), and the faculty member is emailed a reminder to clock out.
      Classes still running at midnight are closed automatically and are not paid until someone enters when they ended.
    </div>
    <label class="check gap-top"><input type="checkbox" [(ngModel)]="autoApprovePackage" /> Approve package faculty's classes automatically</label>
    <div class="helper">
      Package faculty are not paid per class, so their QR or location clock-ins are taken as attendance without a review.
      Over-long classes, times typed in by hand and forgotten clock-in requests still wait for approval.
    </div>
    <h3 class="section-title">Location (when no admin is free)</h3>
    <p class="muted">
      Faculty normally clock in by scanning the QR code on <b>Class check-in</b>. When no admin is free, they can clock in
      with their phone's location instead — but only within this distance of the academy, and those classes wait for approval.
    </p>
    @if (error()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }
    <div class="two-fields">
      <div><label for="lat">Latitude</label><input id="lat" type="number" step="0.000001" [(ngModel)]="latitude" placeholder="e.g. 28.631500" /></div>
      <div><label for="lon">Longitude</label><input id="lon" type="number" step="0.000001" [(ngModel)]="longitude" placeholder="e.g. 77.216700" /></div>
    </div>
    <label for="radius">Allowed distance (metres)</label>
    <input id="radius" type="number" min="30" max="5000" step="10" [(ngModel)]="radius" />
    <div class="helper">100–200 m suits most buildings. Phone GPS indoors can be 20–60 m off.</div>
    <div class="button-row gap-top">
      <button type="button" class="secondary" [disabled]="busy()" (click)="useHere()">⌖ Use my current location</button>
      <button type="button" class="primary" [disabled]="busy()" (click)="save()">Save clock-in settings</button>
      @if (enabled()) {
        <button type="button" class="text-button danger-text" [disabled]="busy()" (click)="turnOff()">Turn off location clock-in</button>
      }
    </div>
    @if (enabled() && latitude !== null && longitude !== null) {
      <p class="muted gap-top">
        On: within {{ radius }} m of
        <a [href]="'https://www.google.com/maps?q=' + latitude + ',' + longitude" target="_blank" rel="noopener">{{ latitude }}, {{ longitude }}</a>.
      </p>
    }
  `,
})
export class LocationSettingsComponent {
  private readonly api = inject(Api);
  private readonly notify = inject(Notify);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly enabled = signal(false);
  protected latitude: number | null = null;
  protected longitude: number | null = null;
  protected radius = 150;
  protected maxClassMinutes = 180;
  protected autoApprovePackage = true;
  protected readonly maxOptions = [60, 90, 120, 150, 180, 240, 300, 360, 480];

  constructor() {
    this.api.get<AttendanceSettings>('attendance/settings').subscribe({
      next: s => this.apply(s),
      error: e => this.error.set(errorMessage(e)),
    });
  }

  private apply(s: AttendanceSettings): void {
    this.latitude = s.latitude;
    this.longitude = s.longitude;
    this.radius = s.radiusMeters;
    this.maxClassMinutes = s.maxClassMinutes;
    this.autoApprovePackage = s.autoApprovePackageClasses;
    if (!this.maxOptions.includes(s.maxClassMinutes)) this.maxOptions.push(s.maxClassMinutes);
    this.enabled.set(s.enabled);
  }

  protected useHere(): void {
    if (!navigator.geolocation) return this.error.set('This browser cannot share its location.');
    this.busy.set(true);
    this.error.set('');
    navigator.geolocation.getCurrentPosition(
      position => {
        this.busy.set(false);
        this.latitude = Math.round(position.coords.latitude * 1e6) / 1e6;
        this.longitude = Math.round(position.coords.longitude * 1e6) / 1e6;
        this.notify.success(`Location found (accurate to about ${Math.round(position.coords.accuracy)} m). Press Save location to keep it.`);
      },
      () => {
        this.busy.set(false);
        this.error.set('Your location could not be read. Allow location for this site, or type the latitude and longitude from Google Maps.');
      },
      { enableHighAccuracy: true, timeout: 20000, maximumAge: 0 },
    );
  }

  protected save(): void {
    if ((this.latitude === null) !== (this.longitude === null)) return this.error.set('Enter both the latitude and the longitude, or use your current location.');
    this.put(this.latitude, this.longitude, this.latitude === null ? 'Clock-in settings saved' : 'Clock-in settings saved — faculty can clock in by location');
  }

  protected turnOff(): void {
    this.put(null, null, 'Location clock-in turned off — faculty can only clock in by QR code');
  }

  private put(latitude: number | null, longitude: number | null, message: string): void {
    this.busy.set(true);
    this.error.set('');
    this.api.put<AttendanceSettings>('attendance/settings', {
      latitude, longitude, radiusMeters: Number(this.radius), maxClassMinutes: Number(this.maxClassMinutes),
      autoApprovePackageClasses: this.autoApprovePackage,
    }).subscribe({
      next: s => {
        this.busy.set(false);
        this.apply(s);
        this.notify.success(message);
      },
      error: e => {
        this.busy.set(false);
        this.error.set(errorMessage(e));
      },
    });
  }
}
