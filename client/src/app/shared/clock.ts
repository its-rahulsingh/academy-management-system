import { Injectable, OnDestroy, signal } from '@angular/core';

/**
 * A once-a-second tick for running-class timers. Timers count from the server's clock-in time, corrected by how far
 * this device's clock is from the server's, so a phone with the wrong time still shows the right duration.
 */
@Injectable({ providedIn: 'root' })
export class Ticker implements OnDestroy {
  readonly now = signal(Date.now());
  private offset = 0;
  private readonly timer = setInterval(() => this.now.set(Date.now()), 1000);

  /** Call with the server's "now" from any response that has one. */
  sync(serverNowUtc: string): void {
    const server = Date.parse(serverNowUtc);
    if (!Number.isNaN(server)) this.offset = server - Date.now();
  }

  /** Milliseconds since the given UTC time, on the server's clock. Reads `now`, so templates re-render each second. */
  since(utc: string): number {
    return Math.max(0, this.now() + this.offset - Date.parse(utc));
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }
}

/** 4,329,000 ms → "1:12:09"; under an hour → "12:09". */
export function stopwatch(ms: number): string {
  const total = Math.floor(ms / 1000);
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const two = (n: number) => String(n).padStart(2, '0');
  return h ? `${h}:${two(m)}:${two(s)}` : `${m}:${two(s)}`;
}

/** "2026-11-21T10:32:00Z" → "4:02 pm" in the viewer's time zone. */
export function timeOfDay(utc: string): string {
  return new Date(utc).toLocaleTimeString('en-IN', { hour: 'numeric', minute: '2-digit' }).toLowerCase();
}
