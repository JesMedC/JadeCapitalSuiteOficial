export const environment = {
  production: true,
  apiBaseUrl: '/api',
  // Wave 12 slice 12.1 — Production Sentry DSN placeholder. Ops sets
  // via CI/CD build arg (the angular.json `define.__SENTRY_DSN__`
  // value). Empty by default; CI replaces with the real DSN.
  sentryDsn: '',
};
