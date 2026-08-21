import * as Sentry from '@sentry/angular';
import { createTransport, type Transport } from '@sentry/core';
import { initSentry, type FrontendObservabilityConfig } from './sentry-init';

const enabledConfig: FrontendObservabilityConfig = {
  dsn: 'https://public@example.invalid/42',
  release: 'jade-web@12.2.0',
  environment: 'production',
  enabled: true,
};

describe('frontend Sentry', () => {
  afterEach(async () => {
    await Sentry.close(1000);
  });

  it.each(['', '   '])('is a no-op for an empty DSN (%p)', (dsn) => {
    initSentry({ ...enabledConfig, dsn, enabled: false });

    expect(Sentry.isInitialized()).toBe(false);
  });

  it('captures an uncaught error with release and original TypeScript context but no PII', async () => {
    const envelopes: string[] = [];
    initSentry(enabledConfig, makeRecordingTransport(envelopes));
    Sentry.setUser({ id: 'user-42', email: 'private@example.test', ip_address: '203.0.113.42' });
    Sentry.setExtra('authorization', 'Bearer private-token');
    Sentry.addBreadcrumb({ message: 'cookie=session-private', data: { body: 'private-body' } });

    const suppressJsdomReport = (event: ErrorEvent) => event.preventDefault();
    window.addEventListener('error', suppressJsdomReport);
    dispatchUncaughtHarnessError();
    window.removeEventListener('error', suppressJsdomReport);
    expect(await Sentry.flush(1000)).toBe(true);

    const payload = envelopes.find((envelope) => envelope.includes('"type":"event"'));
    expect(payload).toEqual(expect.any(String));
    expect(payload).toContain('jade-web@12.2.0');
    expect(payload).toContain('sentry-init.spec.ts');
    expect(payload).toContain('dispatchUncaughtHarnessError');
    const transportOutput = envelopes.join('\n');
    for (const sensitive of [
      'user-42', 'private@example.test', '203.0.113.42',
      'private-token', 'session-private', 'private-body',
    ]) {
      expect(transportOutput).not.toContain(sensitive);
    }
  });
});

function dispatchUncaughtHarnessError(): void {
  const error = new Error('deterministic frontend failure');
  window.dispatchEvent(new ErrorEvent('error', { error, message: error.message }));
}

function makeRecordingTransport(envelopes: string[]): (options: never) => Transport {
  return (options) => createTransport(options, ({ body }) => {
    envelopes.push(body as string);
    return Promise.resolve({ statusCode: 200 });
  });
}
