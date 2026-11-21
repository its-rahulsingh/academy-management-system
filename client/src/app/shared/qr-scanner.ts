import { Component, ElementRef, OnDestroy, afterNextRender, output, signal, viewChild } from '@angular/core';
import jsQR from 'jsqr';

/** The check-in code inside a scanned QR: either a link ending in ?code=… or the bare code. */
export function checkInCodeFrom(text: string): string {
  try {
    return new URL(text).searchParams.get('code') ?? text;
  } catch {
    return text.trim();
  }
}

/**
 * Live camera QR reader. Uses the back camera, reads a few frames a second and emits the first code it sees.
 * Needs HTTPS (or localhost) and the viewer's permission to use the camera.
 */
@Component({
  selector: 'app-qr-scanner',
  template: `
    <div class="scanner">
      <video #video playsinline muted></video>
      <div class="scanner-frame" aria-hidden="true"></div>
      @if (status()) {
        <p class="scanner-status">{{ status() }}</p>
      }
    </div>
  `,
})
export class QrScannerComponent implements OnDestroy {
  readonly scanned = output<string>();
  readonly failed = output<string>();
  protected readonly status = signal('Starting the camera…');
  private readonly video = viewChild.required<ElementRef<HTMLVideoElement>>('video');
  private stream?: MediaStream;
  private frame = 0;
  private done = false;
  private readonly canvas = document.createElement('canvas');

  constructor() {
    afterNextRender(() => void this.start());
  }

  private async start(): Promise<void> {
    if (!navigator.mediaDevices?.getUserMedia) {
      this.fail('This browser cannot use the camera here. Open the site over HTTPS in Chrome or Safari.');
      return;
    }
    try {
      this.stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: 'environment' } }, audio: false });
    } catch (error) {
      const denied = error instanceof DOMException && (error.name === 'NotAllowedError' || error.name === 'SecurityError');
      this.fail(denied ? 'Camera access was blocked. Allow the camera for this site and try again.' : 'No camera could be started on this device.');
      return;
    }
    if (this.done) return this.stop();
    const video = this.video().nativeElement;
    video.srcObject = this.stream;
    await video.play().catch(() => undefined);
    this.status.set('Point the camera at the QR code on the admin’s screen');
    this.loop();
  }

  private loop(): void {
    const video = this.video().nativeElement;
    let last = 0;
    const tick = (time: number) => {
      if (this.done) return;
      this.frame = requestAnimationFrame(tick);
      // About six reads a second is plenty and keeps phones cool.
      if (time - last < 160 || video.readyState < video.HAVE_ENOUGH_DATA) return;
      last = time;
      const width = Math.min(640, video.videoWidth);
      const height = Math.round((video.videoHeight / video.videoWidth) * width);
      if (!width || !height) return;
      this.canvas.width = width;
      this.canvas.height = height;
      const context = this.canvas.getContext('2d', { willReadFrequently: true });
      if (!context) return;
      context.drawImage(video, 0, 0, width, height);
      const found = jsQR(context.getImageData(0, 0, width, height).data, width, height, { inversionAttempts: 'dontInvert' });
      if (found?.data) {
        this.done = true;
        this.status.set('Code read');
        this.stop();
        this.scanned.emit(found.data);
      }
    };
    this.frame = requestAnimationFrame(tick);
  }

  private fail(message: string): void {
    this.status.set(message);
    this.failed.emit(message);
  }

  private stop(): void {
    cancelAnimationFrame(this.frame);
    this.stream?.getTracks().forEach(track => track.stop());
    this.stream = undefined;
  }

  ngOnDestroy(): void {
    this.done = true;
    this.stop();
  }
}
