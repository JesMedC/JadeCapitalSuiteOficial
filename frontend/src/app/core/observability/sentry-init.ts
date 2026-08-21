import * as Sentry from '@sentry/angular';
import type { BrowserOptions } from '@sentry/browser';
import { frontendObservability } from './observability.generated';

export interface FrontendObservabilityConfig {
  readonly dsn: string;
  readonly release: string;
  readonly environment: string;
  readonly enabled: boolean;
}

export function initSentry(
  config: FrontendObservabilityConfig = frontendObservability,
  transport?: BrowserOptions['transport'],
): void {
  if (!config.enabled || config.dsn.trim() === '') {
    return;
  }

  Sentry.init({
    dsn: config.dsn,
    release: config.release,
    environment: config.environment,
    sendDefaultPii: false,
    integrations: (defaults) => defaults.filter(({ name }) => name !== 'BrowserSession'),
    tracesSampleRate: 0.1,
    transport,
    beforeSend: removeSensitiveEventData,
    beforeBreadcrumb: (breadcrumb) => ({
      category: breadcrumb.category,
      level: breadcrumb.level,
      timestamp: breadcrumb.timestamp,
      type: breadcrumb.type,
    }),
  });
}

function removeSensitiveEventData(event: Sentry.ErrorEvent): Sentry.ErrorEvent {
  delete event.user;
  delete event.request;
  delete event.extra;
  delete event.breadcrumbs;
  return event;
}
