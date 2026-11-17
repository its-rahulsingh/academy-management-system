import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { inject, provideAppInitializer } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { App } from './app/app';
import { routes } from './app/app.routes';
import { academicYearInterceptor } from './app/core/academic-year.service';
import { sessionInterceptor } from './app/core/api';
import { AuthService } from './app/core/auth.service';

bootstrapApplication(App, {
  providers: [
    provideHttpClient(withInterceptors([academicYearInterceptor, sessionInterceptor])),
    provideRouter(routes, withComponentInputBinding(), withInMemoryScrolling({ anchorScrolling: 'enabled', scrollPositionRestoration: 'top' })),
    // Resolve who is signed in before the first route guard runs.
    provideAppInitializer(() => inject(AuthService).refresh()),
  ],
}).catch(error => console.error(error));
