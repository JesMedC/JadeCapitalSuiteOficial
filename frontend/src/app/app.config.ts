import { ApplicationConfig, ErrorHandler, provideZoneChangeDetection } from '@angular/core';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import * as Sentry from '@sentry/angular';

import { routes } from './app.routes';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { errorInterceptor } from './core/interceptors/error.interceptor';
import { frontendObservability } from './core/observability/observability.generated';

// Wave 12 slice 12.1 — Sentry error handler is wired ONLY when the DSN
// is set at build time (via angular.json -> define). When the DSN is
// empty (the dev / CI default), we fall back to Angular's default
// ErrorHandler so unhandled exceptions land in the browser console.
// Matches the BE silent-skip contract.
const sentryDsn: string = frontendObservability.dsn;
const errorHandler: ErrorHandler = sentryDsn && sentryDsn.trim() !== ''
  ? Sentry.createErrorHandler({ showDialog: false })
  : new ErrorHandler();

export const appConfig: ApplicationConfig = {
  providers: [
    { provide: ErrorHandler, useValue: errorHandler },
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(
      routes,
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'top', anchorScrolling: 'enabled' }),
    ),
    provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
  ],
};
