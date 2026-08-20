import { bootstrapApplication } from '@angular/platform-browser';
import { App } from './app/app';
import { appConfig } from './app/app.config';
// Wave 12 slice 12.1 — Sentry init runs BEFORE bootstrap so the
// @sentry/angular error handler + trace provider are registered
// before any user code can throw. Conditional init: skipped entirely
// when window.__SENTRY_DSN__ is unset.
import { initSentry } from './app/core/observability/sentry-init';

initSentry();

bootstrapApplication(App, appConfig).catch((err) => console.error(err));
