import { Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { forkJoin } from 'rxjs';

type ConnectionState = 'loading' | 'connected' | 'offline';

interface HealthResponse {
  status: string;
  service: string;
  utcTime: string;
}

interface WelcomeResponse {
  application: string;
  message: string;
  phase: string;
  modulesPlanned: string[];
}

@Component({
  selector: 'app-root',
  standalone: true,
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  private readonly http = inject(HttpClient);

  readonly state = signal<ConnectionState>('loading');
  readonly welcome = signal<WelcomeResponse | null>(null);
  readonly checkedAt = signal<string>('Not checked yet');
  readonly error = signal<string>('');

  constructor() {
    this.checkConnection();
  }

  checkConnection(): void {
    this.state.set('loading');
    this.error.set('');

    forkJoin({
      health: this.http.get<HealthResponse>('/api/health'),
      welcome: this.http.get<WelcomeResponse>('/api/welcome')
    }).subscribe({
      next: ({ health, welcome }) => {
        this.welcome.set(welcome);
        this.checkedAt.set(new Date(health.utcTime).toLocaleString());
        this.state.set(health.status === 'healthy' ? 'connected' : 'offline');
      },
      error: () => {
        this.state.set('offline');
        this.error.set('The API could not be reached. Verify the .NET server is running and try again.');
      }
    });
  }
}
