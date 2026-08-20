// Wave 12 slice 12.1 — Frontend Sentry initialisation.
//
// Conditional init: Sentry SDK is initialised ONLY when the build-time
// `__SENTRY_DSN__` constant is a non-empty string. The constant is
// supplied via `angular.json` -> `architect.build.options.define`:
//   * Dev builds: __SENTRY_DSN__ = '' (empty; silent skip)
//   * Production builds (CI/CD): __SENTRY_DSN__ is replaced by the
//     `SENTRY_DSN` build arg via a sed step in
//     `infrastructure/Dockerfile.frontend.prod` (or whatever the
//     ops build pipeline uses — see CHANGELOG).
//
// When Sentry is not initialised, this file is a no-op and
// `@sentry/angular`'s `createErrorHandler({ showDialog: false })`
// in app.config.ts is replaced by Angular's default `ErrorHandler`.
//
// GDPR Art. 5 — data minimisation: `sendDefaultPii: false` so we never
// forward the visitor's IP / cookies / user identifiers to Sentry by
// default. Operations must add an EU-residency Sentry project before
// enabling in production.

import * as Sentry from '@sentry/angular';

declare const __SENTRY_DSN__: string;
declare const __APP_ENV__: string;

export function initSentry(): void {
  const dsn = __SENTRY_DSN__;
  if (!dsn || dsn.trim() === '') {
    // Silent skip — matches the BE Program.cs silent-skip contract.
    return;
  }

  Sentry.init({
    dsn,
    environment: __APP_ENV__ || 'development',
    tracesSampleRate: 0.1,
    sendDefaultPii: false,
  });
}
