export const environment = {
  production: false,
  apiBaseUrl: '/api',
  // Wave 12 slice 12.1 — Sentry DSN placeholder. The build-time
  // `angular.json` `define` substitution is the canonical mechanism
  // (see src/app/core/observability/sentry-init.ts); this field
  // mirrors the same value for consumers that prefer reading from
  // `environment` instead of a global constant.
  sentryDsn: '',
};
