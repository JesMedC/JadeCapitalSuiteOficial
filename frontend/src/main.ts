import { bootstrapApplication } from '@angular/platform-browser';
import { App } from './app/app';
import { appConfig } from './app/app.config';
// Initialize from generated build configuration before application code runs.
import { initSentry } from './app/core/observability/sentry-init';

initSentry();

bootstrapApplication(App, appConfig).catch((err) => console.error(err));
